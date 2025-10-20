/**
 * Entry point for the Vite dev server used for manual testing of React components.
 *
 * This file:
 * - Sets up jQuery mocks for the localization system
 * - Enables localization bypass for testing
 * - Renders components based on __TEST_ELEMENT__ injection (for automated tests)
 * - Provides a component map for manual testing
 *
 * To run: `yarn dev` from the component-tester folder
 * Then open http://127.0.0.1:5173/ in your browser
 */

import * as React from "react";
import * as ReactDOM from "react-dom";
// import { StyledEngineProvider, ThemeProvider } from "@mui/material/styles";
// import { lightTheme } from "../../../../bloomMaterialUITheme";
import {
    RegistrationContents,
    createEmptyRegistrationInfo,
} from "../registration/registrationContents";
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

const rootElement = document.getElementById("root");

if (!rootElement) {
    throw new Error("Root element was not found.");
}

// Check if test config was injected
const testConfig = (window as any).__TEST_CONFIG__ as undefined;

bypassLocalization(true);

// Component map for testing
// Add components here as needed for manual testing
const componentMap: Record<string, React.ComponentType<any>> = {
    RegistrationContents,
};

// Check if test element was injected
const testElement = (window as any).__TEST_ELEMENT__ as
    | { type: string; props: any }
    | undefined;

let componentToRender: React.ReactElement;

if (testElement) {
    // Test mode: render the injected element with its props
    const Component = componentMap[testElement.type];
    if (!Component) {
        throw new Error(
            `Component "${testElement.type}" not found in component map. ` +
                `Available components: ${Object.keys(componentMap).join(", ")}`,
        );
    }
    componentToRender = React.createElement(Component, testElement.props);
} else {
    // Default mode for manual testing: render RegistrationContents
    // This provides a working example when you just run `yarn dev`
    componentToRender = (
        <RegistrationContents
            initialInfo={createEmptyRegistrationInfo()}
            emailRequiredForTeamCollection={false}
            mayChangeEmail={true}
            onSubmit={(info) => console.log("Submitted:", info)}
        />
    );
}

// Render the component
ReactDOM.render(
    // <StyledEngineProvider injectFirst>
    //     <ThemeProvider theme={lightTheme}>{componentToRender}</ThemeProvider>
    // </StyledEngineProvider>,
    <>{componentToRender}</>,
    rootElement,
);
