// Shared entry point bootstrap for Vite-based React components in WinForms.
// This module handles jQuery global setup and component mounting logic.

import * as jQuery from "jquery";
import * as React from "react";
import * as ReactDOM from "react-dom";

// Ensure jQuery is available globally for legacy scripts (e.g., jquery-ui)
// After jQuery is evaluated, expose globals expected by legacy code
// (Vite evaluates imports depth-first in source order).
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(window as any).jQuery = jQuery;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(window as any).$ = jQuery;

declare global {
    interface Window {
        __reactControlProps__?: Record<string, unknown>;
        wireUpRootComponentFromWinforms?: (
            root: HTMLElement,
            props: Record<string, unknown>,
        ) => void;
        // Legacy globals for libraries expecting jQuery on window
        jQuery?: unknown;
        $?: unknown;
    }
}

/**
 * Bootstrap a React component as an entry point for WinForms integration.
 * @param Component - The React component to mount
 * @param rootElementId - The ID of the root DOM element (default: "reactRoot")
 */
export const bootstrapReactComponent = (
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    Component: React.ComponentType<any>,
    rootElementId = "reactRoot",
): void => {
    // Props can be provided from the host (WinForms) via a global.
    // This mirrors the existing WireUpForWinforms path but keeps it fully inside Vite.
    const props = window.__reactControlProps__ ?? {};

    function mount() {
        const rootDiv = document.getElementById(rootElementId);
        if (!rootDiv) return;

        // If the legacy wire-up function exists, prefer it for parity.
        const wireUp = window.wireUpRootComponentFromWinforms;
        if (typeof wireUp === "function") {
            wireUp(rootDiv, props);
        } else {
            ReactDOM.render(React.createElement(Component), rootDiv);
        }
    }

    if (document.readyState === "loading") {
        window.addEventListener("DOMContentLoaded", mount);
    } else {
        mount();
    }
};
