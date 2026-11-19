// Compare rectangles the way a person judges a layout: by how they sit relative to each other.
//
// A test here never asserts a pixel value. The suite runs at whatever size the machine's window
// happens to be, Bloom scales the page it is editing by the zoom setting, and Windows scales
// everything again by the display's DPI, so "the first column is 150px wide" is a fact about the
// machine, not about Bloom. "The two panes fill the page with no gap and no overlap" and "the
// picture lies inside its box" are facts about Bloom, and they are what these check.
//
// The tolerance exists because those three scalings leave sub-pixel remainders: a boundary that
// the grid places at 150.5px is reported as 150.5 on one side and 150.5 on the other, but a value
// rounded on the way through CSS can land a fraction out. One pixel is far below anything a
// difference a person would notice, and far above the remainders.

import { expect } from "@playwright/test";

/** A rectangle in the page's own coordinates, as Playwright's boundingBox reports one. */
export interface IRect {
    x: number;
    y: number;
    width: number;
    height: number;
}

/** How far apart two edges may be and still count as the same edge. */
export const kEdgeTolerance = 1.5;

/** A rectangle's right edge. */
export function right(rect: IRect): number {
    return rect.x + rect.width;
}

/** A rectangle's bottom edge. */
export function bottom(rect: IRect): number {
    return rect.y + rect.height;
}

/** True when the two numbers are the same edge, allowing for sub-pixel scaling remainders. */
export function sameEdge(a: number, b: number): boolean {
    return Math.abs(a - b) <= kEdgeTolerance;
}

/** A rectangle written for a failure message, rounded, because the exact value is never the point. */
export function describeRect(rect: IRect): string {
    return (
        `x=${Math.round(rect.x)} y=${Math.round(rect.y)} ` +
        `w=${Math.round(rect.width)} h=${Math.round(rect.height)}`
    );
}

/** True when the two rectangles share any area. Touching edges do not count as overlapping. */
export function rectsOverlap(a: IRect, b: IRect): boolean {
    const overlapWidth =
        Math.min(right(a), right(b)) - Math.max(a.x, b.x) - kEdgeTolerance;
    const overlapHeight =
        Math.min(bottom(a), bottom(b)) - Math.max(a.y, b.y) - kEdgeTolerance;
    return overlapWidth > 0 && overlapHeight > 0;
}

/** One named rectangle, so a failure message can say which thing was where. */
export interface INamedRect {
    name: string;
    rect: IRect;
}

/**
 * Assert that no two of the rectangles share any area. Use this for affordances that a person has
 * to be able to hit independently: an element's resize handles, its "..." button, the canvas
 * element's own toolbar.
 */
export function expectNoOverlap(rects: INamedRect[], what: string): void {
    const overlapping: string[] = [];
    for (let i = 0; i < rects.length; i++)
        for (let j = i + 1; j < rects.length; j++)
            if (rectsOverlap(rects[i].rect, rects[j].rect))
                overlapping.push(
                    `${rects[i].name} (${describeRect(rects[i].rect)}) overlaps ` +
                        `${rects[j].name} (${describeRect(rects[j].rect)})`,
                );
    expect(
        overlapping,
        `${what}: these should not cover each other, because a person has to be able to click each ` +
            `one.\n  ${overlapping.join("\n  ")}`,
    ).toEqual([]);
}

/**
 * Assert that `inner` lies within `outer`. Use this for a picture inside its box, or a canvas
 * element inside its canvas: it says the thing is where it belongs without saying where that is.
 */
export function expectInside(
    inner: IRect,
    outer: IRect,
    innerName: string,
    outerName: string,
): void {
    const outside: string[] = [];
    if (inner.x < outer.x - kEdgeTolerance)
        outside.push(`its left edge is left of ${outerName}'s`);
    if (inner.y < outer.y - kEdgeTolerance)
        outside.push(`its top edge is above ${outerName}'s`);
    if (right(inner) > right(outer) + kEdgeTolerance)
        outside.push(`its right edge is right of ${outerName}'s`);
    if (bottom(inner) > bottom(outer) + kEdgeTolerance)
        outside.push(`its bottom edge is below ${outerName}'s`);
    expect(
        outside,
        `${innerName} (${describeRect(inner)}) is not inside ${outerName} ` +
            `(${describeRect(outer)}): ${outside.join(", ")}.`,
    ).toEqual([]);
}

/** Assert that the rectangle has not moved or changed size. */
export function expectSameRect(
    actual: IRect,
    expected: IRect,
    what: string,
): void {
    const moved =
        !sameEdge(actual.x, expected.x) ||
        !sameEdge(actual.y, expected.y) ||
        !sameEdge(actual.width, expected.width) ||
        !sameEdge(actual.height, expected.height);
    expect(
        moved,
        `${what} should not have moved or changed size, but it went from ` +
            `${describeRect(expected)} to ${describeRect(actual)}.`,
    ).toBe(false);
}
