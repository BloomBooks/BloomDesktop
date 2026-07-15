// Non-React lifecycle owner for the per-editable React controls (see
// EditableControls.tsx). This module inserts, positions, updates, and removes a
// small container div for each qualifying .bloom-editable, and drives the
// React root inside it. It replaces the old jQuery AddLanguageTags mechanism
// (which set a data-languageTipContent attribute rendered by a CSS ::after).
//
// The container is inserted as a sibling immediately after the editable (inside
// its .bloom-translationGroup), NOT as a child. This inherits the BL-12118
// safety of the #formatButton precedent: ckeditor only ever touches the
// editables themselves, and putting interactive UI inside a contentEditable
// broke ctrl-A in WebView2. The container carries the bloom-ui class, so it is
// stripped from the saved book HTML by both the C# save path and Cleanup() in
// bloomEditing.ts, guaranteeing the book is never polluted.

import * as React from "react";
import { renderRoot, unmountRoot } from "../../utils/reactRender";
import { kCanvasElementSelector } from "../toolbox/canvas/canvasElementConstants";
import { EditableControls } from "./EditableControls";

export { getLanguageDisplayName } from "./EditableControls";

const kControlsContainerClass = "bloom-editableControls";

// Every editable we currently manage, mapped to its controls container. A plain
// Map (not WeakMap) so we can iterate it to reposition/cleanup; entries for
// editables removed from the DOM are pruned lazily (isConnected checks) and on
// cleanup.
const containersByEditable = new Map<HTMLElement, HTMLElement>();

// One ResizeObserver per translation group; disconnected and rebuilt whenever
// the set of managed editables changes.
let observers: ResizeObserver[] = [];

// The editable that currently has focus (drives the keyboard indicator). Null
// when focus is not in any editable.
let focusedEditable: HTMLElement | null = null;

// Guards one-time wiring of the document-level focusout listener.
let focusoutListenerAttached = false;

// Decide whether the language tag should be visible for this editable. Ported
// from the old AddLanguageTags qualification rules (bloomEditing.ts): the
// keyboard indicator may still be shown even when the tag is not, so this only
// governs the tag.
function shouldShowTag(editable: HTMLElement): boolean {
    const lang = editable.getAttribute("lang");
    // "*" and empty are "any language" placeholders; "z" is the never-visible
    // prototype-block marker (and looking it up would produce a
    // missing-localization toast).
    if (!lang || lang === "*" || lang === "z") return false;

    // bloom-hideLanguageNameDisplay on the editable or any ancestor turns tags
    // off (e.g. for a whole page). closest() covers the self case too.
    if (editable.closest(".bloom-hideLanguageNameDisplay")) return false;

    // Inside a canvas element the language is shown in the context controls box,
    // not as a corner tag.
    if (editable.closest("[data-bubble]")) return false;

    // With a really small box the tag fights for space with hint bubbles, so we
    // suppress it — but not inside canvas elements, where boxes are small by
    // design and the tag lives elsewhere anyway.
    const inCanvasElement = !!editable.closest(kCanvasElementSelector);
    if (editable.offsetWidth < 100 && !inCanvasElement) return false;

    return true;
}

// Render (or re-render) the React controls for one editable with current props.
function renderControls(editable: HTMLElement): void {
    const container = containersByEditable.get(editable);
    if (!container) return;
    renderRoot(
        React.createElement(EditableControls, {
            editable,
            focused: editable === focusedEditable,
            showTag: shouldShowTag(editable),
        }),
        container,
    );
}

// Position one container to overlay its editable exactly. The container is
// absolutely positioned and shares an offset parent with the editable (they are
// siblings), so the editable's own offset* box — already in the page's
// unscaled coordinate space — positions it correctly at any zoom without any
// getBoundingClientRect/scale arithmetic. The right-aligned strip inside then
// sits in the editable's lower-right corner. Hidden editables (offsetHeight 0)
// hide their strip.
function positionContainer(
    editable: HTMLElement,
    container: HTMLElement,
): void {
    if (editable.offsetHeight === 0) {
        container.style.display = "none";
        return;
    }
    container.style.display = "";
    container.style.top = editable.offsetTop + "px";
    container.style.left = editable.offsetLeft + "px";
    container.style.width = editable.offsetWidth + "px";
    container.style.height = editable.offsetHeight + "px";
}

// Disconnect and rebuild the ResizeObservers over all currently-managed,
// still-connected editables. We observe each translation group AND each of its
// editables: typing that grows one box changes the group's size (moving later
// boxes) and also the box's own size, and both need the strips realigned.
function rebuildPositioning(): void {
    observers.forEach((o) => o.disconnect());
    observers = [];

    // Prune editables that have left the DOM, then group the rest by their
    // translation group (falling back to the parent element for the rare
    // page types that have no translation group, e.g. arithmetic templates).
    const groups = new Map<HTMLElement, HTMLElement[]>();
    for (const [editable, container] of containersByEditable) {
        if (!editable.isConnected) {
            unmountRoot(container);
            container.remove();
            containersByEditable.delete(editable);
            continue;
        }
        const group = (editable.closest(".bloom-translationGroup") ??
            editable.parentElement) as HTMLElement | null;
        if (!group) continue;
        const list = groups.get(group) ?? [];
        list.push(editable);
        groups.set(group, list);
    }

    for (const [group, editables] of groups) {
        const observer = new ResizeObserver(() => {
            editables.forEach((editable) => {
                const container = containersByEditable.get(editable);
                if (container) positionContainer(editable, container);
            });
        });
        observer.observe(group);
        editables.forEach((editable) => {
            observer.observe(editable);
            const container = containersByEditable.get(editable);
            if (container) positionContainer(editable, container);
        });
        observers.push(observer);
    }
}

// When focus leaves all editables (e.g. to the toolbox), drop the keyboard
// indicator on the previously-focused field.
function attachFocusoutListenerOnce(): void {
    if (focusoutListenerAttached) return;
    focusoutListenerAttached = true;
    document.body.addEventListener("focusout", () => {
        // The related focus target isn't reliably available synchronously; check
        // on the next tick where document.activeElement, is settled.
        window.setTimeout(() => {
            const active = document.activeElement;
            if (!active || !active.closest(".bloom-editable")) {
                const previous = focusedEditable;
                focusedEditable = null;
                if (previous) renderControls(previous);
            }
        }, 0);
    });
}

// Ensure a controls container exists for every qualifying editable in the
// container, render the React controls into each, and (re)establish
// positioning. Idempotent: an editable that already has a container is simply
// re-rendered, not duplicated. Called from bloomEditing SetupElements, which
// covers page bootstrap as well as newly-added canvas elements and image
// descriptions.
export function setupEditableControls(container: HTMLElement): void {
    attachFocusoutListenerOnce();

    const editables = Array.from(
        container.querySelectorAll<HTMLElement>(
            ".bloom-editable[contenteditable=true]",
        ),
    );
    for (const editable of editables) {
        // Prototype blocks (lang "z") are never visible; don't waste a React
        // root on them.
        if (editable.getAttribute("lang") === "z") continue;

        let controlsContainer = containersByEditable.get(editable);
        if (!controlsContainer) {
            controlsContainer = document.createElement("div");
            controlsContainer.className = `bloom-ui ${kControlsContainerClass}`;
            controlsContainer.setAttribute("contenteditable", "false");
            editable.insertAdjacentElement("afterend", controlsContainer);
            containersByEditable.set(editable, controlsContainer);
        }
        renderControls(editable);
    }

    rebuildPositioning();
}

// Called from bloomEditing's focusin handler after keyboard attachment, so the
// focused field's keyboard indicator can appear (and the previously-focused
// field's disappear).
export function notifyEditableFocused(editable: HTMLElement): void {
    const previous = focusedEditable;
    focusedEditable = editable;
    if (previous && previous !== editable) renderControls(previous);
    renderControls(editable);
}

// Unmount every React root and remove every container. Called from
// removeEditingDebris before the page is captured for saving; the bloom-ui
// stripping is the backstop, but unmounting first keeps React from touching a
// detached DOM.
export function cleanupEditableControls(): void {
    observers.forEach((o) => o.disconnect());
    observers = [];
    for (const [, container] of containersByEditable) {
        unmountRoot(container);
        container.remove();
    }
    containersByEditable.clear();
    focusedEditable = null;
}
