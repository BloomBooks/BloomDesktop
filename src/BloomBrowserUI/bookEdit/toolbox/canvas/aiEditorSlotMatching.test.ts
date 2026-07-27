import { describe, expect, test } from "vitest";

import { matchReplacementsToElements } from "./aiEditorSlotMatching";

// Unit tests for the current-page slot matcher used by the AI editor's commit (see
// aiEditorSlotMatching.ts and canvasControlRegistry.ts editWithAi). The tricky case is a page
// with several image slots that share a filename: distinct replacements must land on distinct
// elements, in slot (ordinal) order, not all collapse onto the first same-filename element.

// A minimal stand-in for a replacement and a live page element, so the test needs no DOM.
interface Repl {
    incomingId: string; // "{pageId}:{ordinal}"
    oldSrc: string; // filename the replacement wants to land on
    newSrc: string; // the replacement image (only used to identify the pair here)
}
interface El {
    filename: string; // filename the live element currently shows
    tag: string; // just a label so assertions can name the element
}

const ordinalOf = (r: Repl) =>
    parseInt(r.incomingId.split(":").pop() ?? "", 10) || 0;

// Run the matcher with the Repl/El accessors wired up the way the real caller does.
function match(replacements: Repl[], candidates: El[]) {
    return matchReplacementsToElements(
        replacements,
        ordinalOf,
        (r) => r.oldSrc,
        candidates,
        (e) => e.filename,
    );
}

describe("matchReplacementsToElements", () => {
    test("matches a single replacement to the element with the same filename", () => {
        const els: El[] = [
            { filename: "a.png", tag: "A" },
            { filename: "b.png", tag: "B" },
        ];
        const result = match(
            [{ incomingId: "p:1", oldSrc: "b.png", newSrc: "new-b.png" }],
            els,
        );

        expect(result).toHaveLength(1);
        expect(result[0].element.tag).toBe("B");
        expect(result[0].replacement.newSrc).toBe("new-b.png");
    });

    test("two same-filename slots get distinct elements, paired by ordinal order", () => {
        // Two placeholder slots that both currently show placeholder.png. The ordinal-0
        // replacement must take the first placeholder element and ordinal-1 the second —
        // neither collapsing onto the same element.
        const els: El[] = [
            { filename: "placeholder.png", tag: "first" },
            { filename: "placeholder.png", tag: "second" },
        ];
        // Deliberately pass them out of ordinal order to prove the matcher sorts.
        const result = match(
            [
                {
                    incomingId: "p:1",
                    oldSrc: "placeholder.png",
                    newSrc: "gen-1.png",
                },
                {
                    incomingId: "p:0",
                    oldSrc: "placeholder.png",
                    newSrc: "gen-0.png",
                },
            ],
            els,
        );

        expect(result).toHaveLength(2);
        // Applied in ascending ordinal order: ordinal 0 first, ordinal 1 second.
        expect(result[0].replacement.newSrc).toBe("gen-0.png");
        expect(result[0].element.tag).toBe("first");
        expect(result[1].replacement.newSrc).toBe("gen-1.png");
        expect(result[1].element.tag).toBe("second");
        // Sanity: the two replacements did NOT land on the same element.
        expect(result[0].element).not.toBe(result[1].element);
    });

    test("a replacement with no filename match is skipped, others still match", () => {
        const els: El[] = [{ filename: "a.png", tag: "A" }];
        const result = match(
            [
                {
                    incomingId: "p:0",
                    oldSrc: "missing.png",
                    newSrc: "gen-miss.png",
                },
                { incomingId: "p:1", oldSrc: "a.png", newSrc: "gen-a.png" },
            ],
            els,
        );

        expect(result).toHaveLength(1);
        expect(result[0].replacement.newSrc).toBe("gen-a.png");
        expect(result[0].element.tag).toBe("A");
    });

    test("more same-filename replacements than elements: extras drop out, no reuse", () => {
        const els: El[] = [{ filename: "dup.png", tag: "only" }];
        const result = match(
            [
                { incomingId: "p:0", oldSrc: "dup.png", newSrc: "gen-0.png" },
                { incomingId: "p:1", oldSrc: "dup.png", newSrc: "gen-1.png" },
            ],
            els,
        );

        // Only one element exists, so only the first (ordinal 0) replacement lands; the
        // second finds no unused same-filename element and is omitted rather than reusing.
        expect(result).toHaveLength(1);
        expect(result[0].replacement.newSrc).toBe("gen-0.png");
    });

    test("does not mutate the caller's replacements array order", () => {
        const replacements: Repl[] = [
            { incomingId: "p:2", oldSrc: "a.png", newSrc: "n2.png" },
            { incomingId: "p:0", oldSrc: "a.png", newSrc: "n0.png" },
        ];
        const els: El[] = [
            { filename: "a.png", tag: "A0" },
            { filename: "a.png", tag: "A2" },
        ];
        match(replacements, els);

        // The matcher sorts a copy; the original array keeps its input order.
        expect(replacements[0].incomingId).toBe("p:2");
        expect(replacements[1].incomingId).toBe("p:0");
    });
});
