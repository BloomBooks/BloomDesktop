import { act } from "react";
import { createRoot, Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// The Leveled Reader's arrows themselves, as a person sees them, while the tool restores a
// book's level: disabled until the restore finishes, then enabled again. (The model side is in
// readerToolRestoreDisablesArrows.spec.ts.)

// Requests for the default level are held here until the test answers them.
const requestsForDefault = vi.hoisted(
    () => [] as { url: string; answer: (defaultLevel: number) => void }[],
);

vi.mock("../../../utils/bloomApi", async (importOriginal) => ({
    ...((await importOriginal()) as object),
    get: vi.fn((url: string, success: (result: { data: string }) => void) => {
        requestsForDefault.push({
            url,
            answer: (defaultLevel) => success({ data: String(defaultLevel) }),
        });
    }),
    post: vi.fn((_url: string, success?: () => void) => success?.()),
    postString: vi.fn(),
}));

vi.mock("./readerTools", () => ({
    beginInitializeLeveledReaderTool: vi.fn(() => Promise.resolve()),
    beginInitializeDecodableReaderTool: vi.fn(() => Promise.resolve()),
}));

// The "Level {0}" and "of {0}" labels look themselves up in Bloom's localization; these
// tests are about the arrows.
vi.mock("../../../react_components/l10nComponents", async (importOriginal) => ({
    ...((await importOriginal()) as object),
    Span: (props: { children: string }) => <span>{props.children}</span>,
}));

import { getTheOneReaderToolsModel } from "./readerToolsModel";
import ReadersSynphonyWrapper from "./ReadersSynphonyWrapper";
import { LeveledReaderTool } from "./leveledReader/leveledReaderTool";
import { ReaderToolNav } from "./ReaderToolNav";

/** Settings with four levels. */
function makeSynphonyWithFourLevels(): ReadersSynphonyWrapper {
    const settings: any = {
        letters: "a b c d e f g h i j k l m n o p q r s t u v w x y z",
        moreWords: "cat sat bob fig",
        stages: [],
        levels: [1, 2, 3, 4].map(() => ({
            maxWordsPerSentence: 5,
            maxWordsPerPage: 10,
            maxWordsPerBook: 100,
            maxUniqueWordsPerBook: "",
            thingsToRemember: [""],
        })),
    };
    const synphony = new ReadersSynphonyWrapper();
    synphony.loadSettings(settings);
    return synphony;
}

let container: HTMLDivElement;
let root: Root;

function renderNav(): void {
    root.render(<ReaderToolNav isForLeveled={true} changeFunction={vi.fn()} />);
}

function arrow(which: "previous" | "next"): HTMLButtonElement {
    const button = container.querySelector<HTMLButtonElement>(
        `[data-testid="leveled-reader-level-${which}"]`,
    );
    if (!button) throw new Error(`The ${which} arrow is not showing.`);
    return button;
}

describe("the Leveled Reader's arrows while it restores a book's level", () => {
    beforeEach(async () => {
        requestsForDefault.length = 0;
        const model = getTheOneReaderToolsModel();
        model.clearForTest();
        model.setSynphony(makeSynphonyWithFourLevels());
        model.setLevelNumber(2, true);
        // The tool's controls component does this; here the nav stands in for it.
        model.refreshFuncLeveled = renderNav;
        container = document.createElement("div");
        document.body.appendChild(container);
        root = createRoot(container);
        await act(async () => renderNav());
    });

    afterEach(() => {
        act(() => root.unmount());
        container.remove();
        getTheOneReaderToolsModel().refreshFuncLeveled = undefined;
    });

    it("are disabled until the default level arrives, then enabled", async () => {
        // sanity check: on level 2 of 4, both arrows work before any restore
        expect(arrow("previous").disabled).toBe(false);
        expect(arrow("next").disabled).toBe(false);

        let restore: Promise<void> = Promise.resolve();
        await act(async () => {
            restore = new LeveledReaderTool().beginRestoreSettings({});
        });
        expect(requestsForDefault.map((r) => r.url)).toEqual([
            "readers/io/defaultLevel",
        ]);
        expect(arrow("previous").disabled).toBe(true);
        expect(arrow("next").disabled).toBe(true);
        expect(
            container
                .querySelector('[data-testid="leveled-reader-level"]')
                ?.getAttribute("data-restoring"),
        ).toBe("true");

        await act(async () => {
            requestsForDefault[0].answer(3);
            await restore;
        });

        expect(getTheOneReaderToolsModel().levelNumber).toBe(3);
        expect(arrow("previous").disabled).toBe(false);
        expect(arrow("next").disabled).toBe(false);
        expect(
            container
                .querySelector('[data-testid="leveled-reader-level"]')
                ?.getAttribute("data-restoring"),
        ).toBe("false");
    });
});
