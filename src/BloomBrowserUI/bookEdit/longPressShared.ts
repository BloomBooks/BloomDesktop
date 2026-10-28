export const isLongPressEvaluating: string = "isLongPressEvaluating";

// Elements KeymanWeb has attached to (see keymanWebIntegration.ts). Long-press's
// alternate-character popup offers variants of the PHYSICAL key pressed, which is
// meaningless once KMW is remapping that key to something else, and the two
// libraries would otherwise both try to handle the same keydown/keyup events.
// This extends the existing BL-1071 policy (disable long-press window-wide when a
// system input processor is active) to individual-field granularity for KMW.
// Tracked here (not a DOM attribute — the DOM gets saved into the book) so
// jquery.longpress.js can consult it without importing keymanWebIntegration.ts
// (which would create a circular import back into bloomEditing.ts).
const kmwAttachedEditables = new WeakSet<HTMLElement>();

export function setKmwAttached(editable: HTMLElement): void {
    kmwAttachedEditables.add(editable);
}

export function isKmwAttached(element: EventTarget | null): boolean {
    return !!element && kmwAttachedEditables.has(element as HTMLElement);
}
