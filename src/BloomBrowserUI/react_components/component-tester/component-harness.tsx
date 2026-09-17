/**
 * Entry point for the Vite dev server used for manual testing of React components.
 *
 * This file:
 * - Sets up jQuery mocks for the localization system
 * - Enables localization bypass for testing
 * - Renders components based on __TEST_ELEMENT__ injection (for automated tests)
 * - Dynamically loads components defined via Playwright or manual configuration
 *
 * To run: `pnpm dev` from the component-tester folder
 * Then open http://127.0.0.1:5183/ in your browser
 */

import * as React from "react";
import $ from "jquery";
import { renderRoot } from "../../utils/reactRender";
// import { StyledEngineProvider, ThemeProvider } from "@mui/material/styles";
// import { lightTheme } from "../../../../bloomMaterialUITheme";
import { ComponentRenderRequest } from "./componentTypes";
import {
    getComponentRequestByName,
    listComponentNames,
} from "./component-registry";
import { bypassLocalization } from "../../lib/localizationManager/localizationManager";

// Expose the real jQuery as a global for the components under test.
//
// The localization system needs jQuery promises ($.Deferred), which real jQuery provides,
// so there is nothing here worth mocking. It matters that this is the genuine jQuery: some
// of our dependencies are jQuery *plugins* that attach themselves to $.fn at import time,
// reading the global rather than any module import. select2 is the case that bit us — under
// Vite it loads as an optimized ESM dep, so `module` is undefined and its UMD wrapper falls
// through to the browser-globals branch, which does `factory(jQuery)` against window.jQuery.
// A stand-in object with only Deferred on it has no `.fn`, so select2 threw at import time
// and took down every component whose graph reaches StyleEditor (e.g. anything under the
// toolbox). Assigning the real jQuery here keeps those plugins working.
//
// This runs before any component module is loaded: components are pulled in lazily by
// renderRequest() below, well after this module's top-level code.
window.$ = window.jQuery = $;

const manualConfigModules = import.meta.glob<ManualConfigModule>(
    "./manualConfig.ts",
    {
        eager: true,
    },
);

type ManualConfigModule = {
    manualComponent?: ComponentRenderRequest<any>;
};

const rootElement = document.getElementById("root");

if (!rootElement) {
    throw new Error("Root element was not found.");
}

bypassLocalization(true);

const testRequest = (window as any).__TEST_ELEMENT__ as
    | ComponentRenderRequest<any>
    | undefined;

const urlParams = new URLSearchParams(window.location.search);
const requestedComponentName = urlParams.get("component");

let pendingRequest: ComponentRenderRequest<any> | undefined = testRequest;
let pendingError: string | undefined;

if (!pendingRequest && requestedComponentName) {
    pendingRequest = getComponentRequestByName(requestedComponentName);
    if (!pendingRequest) {
        pendingError = `Component "${requestedComponentName}" was not found in the registry.`;
    }
}

const manualRequest = manualConfigModules["./manualConfig.ts"]?.manualComponent;

if (!pendingRequest && !pendingError && manualRequest) {
    pendingRequest = manualRequest;
}

if (pendingError) {
    renderInstructions(pendingError);
} else if (!pendingRequest) {
    renderInstructions();
} else {
    renderRoot(<div>Loading component…</div>, rootElement);
    void renderRequest(pendingRequest);
}

function renderInstructions(message?: string) {
    const componentNames = listComponentNames();

    renderRoot(
        <div style={{ fontFamily: "sans-serif" }}>
            <h1>Bloom React Component Tester</h1>
            {message ? <p>{message}</p> : null}
            {
                "Normally, this system is used to run playwright tests for individual components. You can also manually play with these registered components:"
            }
            {componentNames.length > 0 ? (
                <>
                    <ul>
                        {componentNames.map((name) => (
                            <li key={name}>
                                <a
                                    href={`/?component=${encodeURIComponent(name)}`}
                                >
                                    {name}
                                </a>
                            </li>
                        ))}
                    </ul>
                </>
            ) : null}
        </div>,
        rootElement,
    );
}

async function renderRequest(request: ComponentRenderRequest<any>) {
    try {
        const Component = await loadComponent(request.descriptor);
        const element = React.createElement(Component, request.props ?? {});

        renderRoot(
            // <StyledEngineProvider injectFirst>
            //     <ThemeProvider theme={lightTheme}>{element}</ThemeProvider>
            // </StyledEngineProvider>,
            <>{element}</>,
            rootElement,
        );
    } catch (error) {
        console.error("Component tester failed to render", error);
        renderError(error);
    }
}

async function loadComponent(descriptor: {
    modulePath: string;
    exportName?: string;
}): Promise<React.ComponentType<any>> {
    let moduleExports: Record<string, unknown>;
    try {
        // Use Vite's glob import for static analysis
        // Include all sibling component folders
        const modules = import.meta.glob<Record<string, unknown>>(
            "../**/*.{tsx,ts}",
            { eager: false },
        );

        // The modulePath should be relative to component-tester, e.g., "../registration/registrationContents"
        const moduleKey = descriptor.modulePath + ".tsx";
        const moduleKeyTs = descriptor.modulePath + ".ts";

        const moduleLoader = modules[moduleKey] || modules[moduleKeyTs];

        if (!moduleLoader) {
            throw new Error(
                `Module not found: ${descriptor.modulePath}\nAvailable modules: ${Object.keys(modules).slice(0, 10).join(", ")}...`,
            );
        }

        moduleExports = await moduleLoader();
    } catch (error) {
        const message =
            error instanceof Error
                ? `${error.message}${error.stack ? `\n${error.stack}` : ""}`
                : JSON.stringify(error);
        throw new Error(
            `Failed to load module "${descriptor.modulePath}": ${message}`,
        );
    }
    const exportKey = descriptor.exportName ?? "default";
    const candidate = moduleExports[exportKey];

    if (typeof candidate !== "function") {
        throw new Error(
            `Export "${exportKey}" was not found or is not a component in module "${descriptor.modulePath}".`,
        );
    }

    return candidate as React.ComponentType<any>;
}

function renderError(error: unknown) {
    const message =
        error instanceof Error
            ? (error.stack ?? error.message)
            : JSON.stringify(error, null, 2);

    renderRoot(
        <div>
            <h1>Component Tester Error</h1>
            <pre>{message}</pre>
        </div>,
        rootElement,
    );
}
