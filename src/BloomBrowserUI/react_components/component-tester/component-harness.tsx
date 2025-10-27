/**
 * Entry point for the Vite dev server used for manual testing of React components.
 *
 * This file:
 * - Sets up jQuery mocks for the localization system
 * - Enables localization bypass for testing
 * - Renders components based on __TEST_ELEMENT__ injection (for automated tests)
 * - Dynamically loads components defined via Playwright or manual configuration
 *
 * To run: `yarn dev` from the component-tester folder
 * Then open http://127.0.0.1:5173/ in your browser
 */

import * as React from "react";
import * as ReactDOM from "react-dom";
// import { StyledEngineProvider, ThemeProvider } from "@mui/material/styles";
// import { lightTheme } from "../../../../bloomMaterialUITheme";
import { ComponentRenderRequest } from "./componentTypes";
import {
    getComponentRequestByName,
    listComponentNames,
} from "./component-registry";
import { bypassLocalization } from "../../lib/localizationManager/localizationManager";

// Mock jQuery for localization system
// The localization system uses jQuery promises ($.Deferred) which need done/fail methods
(window as any).$ = (window as any).jQuery = {
    Deferred: () => {
        let resolveCallback: any;
        let rejectCallback: any;
        const promise = new Promise((resolve, reject) => {
            resolveCallback = resolve;
            rejectCallback = reject;
        });

        // Create the jQuery-style promise with done/fail methods
        const jQueryPromise = {
            done: (callback: any) => {
                promise.then(callback);
                return jQueryPromise;
            },
            fail: (callback: any) => {
                promise.catch(callback);
                return jQueryPromise;
            },
            then: (callback: any) => {
                promise.then(callback);
                return jQueryPromise;
            },
        };

        const deferred = {
            resolve: (value: any) => {
                resolveCallback(value);
                return deferred;
            },
            reject: (reason: any) => {
                rejectCallback(reason);
                return deferred;
            },
            fail: (callback: any) => {
                promise.catch(callback);
                return deferred;
            },
            promise: () => jQueryPromise,
        };

        return deferred;
    },
};

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
    ReactDOM.render(<div>Loading component…</div>, rootElement);
    void renderRequest(pendingRequest);
}

function renderInstructions(message?: string) {
    const componentNames = listComponentNames();

    ReactDOM.render(
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

        ReactDOM.render(
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
            error instanceof Error ? error.message : JSON.stringify(error);
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

    ReactDOM.render(
        <div>
            <h1>Component Tester Error</h1>
            <pre>{message}</pre>
        </div>,
        rootElement,
    );
}
