import { describe, it, expect, beforeEach, afterAll, vi } from "vitest";

// The paste's request to C#. The tests check what it asks for, and play C#'s part by calling
// endImagePaste, as changeImage and removeImageId do.
const api = vi.hoisted(() => ({ postJson: vi.fn() }));
vi.mock("../../utils/bloomApi", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../../utils/bloomApi")>()),
    postJson: api.postJson,
}));
// A paste holds a page-content delay until it ends, so that a save waits for the picture.
// bloomEditing imports bloomImages, so a mock built on importOriginal would load the real
// bloomEditing, and through it bloomImages, before the mock existed. Nothing here needs the rest
// of bloomEditing, so the mock is only these two.
const delays = vi.hoisted(() => ({ add: vi.fn(), remove: vi.fn() }));
vi.mock("./bloomEditing", () => ({
    addRequestPageContentDelay: delays.add,
    removeRequestPageContentDelay: delays.remove,
}));
// Showing a failed paste's message needs Bloom's message box, which is not here.
vi.mock("../../utils/bloomMessageBoxSupport", () => ({
    default: { CreateAndShowSimpleMessageBoxWithLocalizedText: vi.fn() },
}));
import {
    getTestRoot,
    cleanTestRoot,
    removeTestRoot,
} from "../../utils/testHelper";
import { doImageCommand, endImagePaste } from "./bloomImages";
// The paste reads the img's src with this jQuery plugin, which the real bloomEditing loads.
import "./jquery.hasAttr.js";

// An image on a page, as the image's Paste image command finds it.
function makeImage(): HTMLElement {
    getTestRoot().innerHTML =
        '<div class="bloom-page"><img src="old.png" alt=""></div>';
    return getTestRoot().querySelector("img") as HTMLElement;
}

// The imageId the nth paste (counting from 0) sent to C#.
function sentImageId(n: number): string {
    return (api.postJson.mock.calls[n][1] as { imageId: string }).imageId;
}

describe("doImageCommand paste", () => {
    beforeEach(() => {
        cleanTestRoot();
        api.postJson.mockReset();
        delays.add.mockReset();
        delays.remove.mockReset();
    });
    afterAll(removeTestRoot);

    it("makes a save wait until C# answers with the img's id", () => {
        const img = makeImage();

        doImageCommand(img, "paste");

        // Sanity check: C# was asked, with the id the img now has.
        expect(img.id).not.toBe("");
        expect(sentImageId(0)).toBe(img.id);
        expect(delays.add).toHaveBeenCalledWith(img.id);
        expect(delays.remove).not.toHaveBeenCalled();

        endImagePaste(img.id);

        expect(delays.remove).toHaveBeenCalledWith(img.id);
        // Ending it again, or ending an id no paste is waiting for, changes nothing.
        endImagePaste(img.id);
        endImagePaste("some-other-image");
        expect(delays.remove).toHaveBeenCalledTimes(1);
    });

    // Both pastes would put the same clipboard picture there, and C# would answer the second
    // with an id the first answer has already taken off the img.
    it("ignores a second paste over the same image until C# has answered the first", () => {
        const img = makeImage();

        doImageCommand(img, "paste");
        doImageCommand(img, "paste");

        expect(api.postJson).toHaveBeenCalledTimes(1);
        expect(delays.add).toHaveBeenCalledTimes(1);

        endImagePaste(img.id);
        doImageCommand(img, "paste");

        expect(api.postJson).toHaveBeenCalledTimes(2);
    });

    it("lets a waiting save go ahead when the request fails", () => {
        const img = makeImage();
        doImageCommand(img, "paste");
        const onError = api.postJson.mock.calls[0][3] as (
            error: unknown,
        ) => void;

        onError({ response: { data: "No image" } });

        expect(delays.remove).toHaveBeenCalledWith(img.id);
    });
});
