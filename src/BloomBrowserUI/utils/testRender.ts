// Test-only helper for the render-into-a-container idiom our vitest component tests share.
// (Not part of reactRender.ts because this registers a vitest afterEach hook on import, so
// it must only ever be imported from test files.)
//
// renderTestRoot() mounts an element into a fresh <div> appended to document.body, flushing
// React 18's asynchronous initial render with act() so the DOM is ready when the test
// asserts, and returns that container div for querying. Every container it creates is
// automatically unmounted and removed after each test — simply importing this module
// registers the cleanup — so test files need no unmount/afterEach boilerplate of their own.

import { act } from "react";
import type { ReactNode } from "react";
import { afterEach } from "vitest";
import { renderRoot, unmountRoot } from "./reactRender";

const renderedContainers: HTMLDivElement[] = [];

// Renders the element into a new container div on document.body and returns the container.
export function renderTestRoot(element: ReactNode): HTMLDivElement {
    const container = document.createElement("div");
    document.body.appendChild(container);
    renderedContainers.push(container);
    // React 18's createRoot renders asynchronously; act() flushes the initial render
    // (and its effects) synchronously so the result is in the DOM when we return.
    act(() => {
        renderRoot(element, container);
    });
    return container;
}

// Unmounts and removes one renderTestRoot() container before the test ends. Rarely needed —
// the afterEach below already cleans everything up — but useful when a single test renders
// the same component twice with different props and wants the first copy gone.
export function unmountTestRoot(container: HTMLDivElement): void {
    unmountRoot(container);
    container.remove();
    const index = renderedContainers.indexOf(container);
    if (index >= 0) renderedContainers.splice(index, 1);
}

afterEach(() => {
    for (const container of renderedContainers) {
        unmountRoot(container);
        container.remove();
    }
    renderedContainers.length = 0;
    // Dialogs/popovers (MUI) portal their content directly onto document.body, outside the
    // mounted roots, so it must be cleaned up separately between tests.
    document.body.innerHTML = "";
});
