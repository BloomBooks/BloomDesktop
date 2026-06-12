// Helpers that replace the React-17 ReactDOM.render / ReactDOM.unmountComponentAtNode
// APIs (removed/deprecated in React 18's createRoot world) while preserving the two
// behaviors our existing call sites relied on:
//   1. Calling render repeatedly on the same container updates it in place. The old
//      ReactDOM.render did this implicitly; createRoot() must only be called once per
//      container, so we cache a Root per container and re-use it on subsequent renders.
//   2. A few call sites used the *return value* of ReactDOM.render to get the mounted
//      class-component instance and immediately call methods on it. createRoot().render()
//      returns void and mounts asynchronously, so renderForInstance() uses a ref plus
//      flushSync to hand back the instance synchronously.

import * as React from "react";
import { flushSync } from "react-dom";
import { createRoot, Root } from "react-dom/client";

const rootsByContainer = new WeakMap<Element | DocumentFragment, Root>();

function getOrCreateRoot(container: Element | DocumentFragment): Root {
    let root = rootsByContainer.get(container);
    if (!root) {
        root = createRoot(container);
        rootsByContainer.set(container, root);
    }
    return root;
}

// Drop-in replacement for ReactDOM.render(element, container). Re-rendering into the
// same container reuses its Root, matching the old update-in-place behavior. The container
// type allows null (as ReactDOM.render's did, since callers commonly pass
// document.getElementById(...)); a null container fails fast with a clear error.
export function renderRoot(
    element: React.ReactNode,
    container: Element | DocumentFragment | null,
): void {
    if (!container) throw new Error("renderRoot called with a null container.");
    getOrCreateRoot(container).render(element);
}

// Drop-in replacement for ReactDOM.unmountComponentAtNode(container). Returns true if a
// root was found and unmounted, like the old API returned whether a component was unmounted.
export function unmountRoot(
    container: Element | DocumentFragment | null,
): boolean {
    if (!container) return false;
    const root = rootsByContainer.get(container);
    if (!root) return false;
    root.unmount();
    rootsByContainer.delete(container);
    return true;
}

// Synchronously mounts a class component and returns its instance. Replaces the legacy
// `const instance = ReactDOM.render(<Comp/>, container)` pattern. flushSync forces React
// to commit (and thus attach the ref) before we return, so callers can drive the instance
// imperatively right away, just as they did under React 17.
export function renderForInstance<T>(
    element: React.ReactElement,
    container: Element | DocumentFragment,
): T {
    let instance = null as unknown as T;
    const elementWithRef = React.cloneElement(element, {
        ref: (r: T) => {
            if (r) instance = r;
        },
        // cloneElement's typing doesn't know our element accepts a ref; the call sites
        // only ever pass class components, which always do.
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any);
    const root = getOrCreateRoot(container);
    flushSync(() => root.render(elementWithRef));
    return instance;
}
