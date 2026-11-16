import { describe, expect, it } from "vitest";
import { countEstimatedBooks, IAppSizeEstimates } from "./AppSizeIndicator";
import {
    getProgressStageLabel,
    IProgressStageLabels,
} from "./appBuilderShared";

const estimates: IAppSizeEstimates = {
    books: [
        {
            bookId: "measured",
            folderPath: "c:/books/measured",
            title: "Measured",
            sizeBytes: 3_000_000,
            isActual: true,
        },
        {
            bookId: "estimated",
            folderPath: "c:/books/estimated",
            title: "Estimated",
            sizeBytes: 5_000_000,
            isActual: false,
        },
    ],
    estimatedAppOverheadBytes: 12_000_000,
    maxAppSizeBytes: 100_000_000,
};

describe("countEstimatedBooks", () => {
    it("counts only the app's books that have no actual size", () => {
        expect(
            countEstimatedBooks(estimates, [
                { bookId: "measured" },
                { bookId: "estimated" },
            ]),
        ).toBe(1);
    });

    it("matches books by folder when there is no id", () => {
        expect(
            countEstimatedBooks(estimates, [
                { folderPath: "c:/books/measured" },
            ]),
        ).toBe(0);
    });

    it("counts a book the server knows nothing about as an estimate", () => {
        expect(countEstimatedBooks(estimates, [{ bookId: "unknown" }])).toBe(1);
    });
});

describe("getProgressStageLabel for measuring", () => {
    const labels = {
        exportingBloompubs: "Making BloomPUBs",
        complete: "Done",
    } as IProgressStageLabels;

    it("shows the BloomPUB stage before the server reports one", () => {
        expect(getProgressStageLabel("measure", undefined, labels)).toBe(
            "Making BloomPUBs",
        );
    });

    it("shows the stage the server reports", () => {
        expect(getProgressStageLabel("measure", "complete", labels)).toBe(
            "Done",
        );
    });

    it("shows nothing when no action is running", () => {
        expect(
            getProgressStageLabel(undefined, "complete", labels),
        ).toBeUndefined();
    });
});
