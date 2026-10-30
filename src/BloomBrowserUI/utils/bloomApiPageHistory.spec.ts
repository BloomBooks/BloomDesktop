import { afterEach, describe, expect, it, vi } from "vitest";
import axios, { AxiosRequestConfig, AxiosResponse } from "axios";

// reasons: the reason given to each clearUndoAndRedo, for Bloom's log.
const history = vi.hoisted(() => ({ clearCount: 0, reasons: [] as string[] }));

vi.mock("../bookEdit/pageHistory/pageHistoryAccess", () => ({
    clearUndoAndRedo: (reason: string) => {
        history.clearCount++;
        history.reasons.push(reason);
    },
}));

import { requestEndsPageHistory } from "./bloomApi";

afterEach(() => {
    history.clearCount = 0;
    history.reasons = [];
    vi.restoreAllMocks();
});

describe("requestEndsPageHistory", () => {
    it("never ends the history for a request that only reads", () => {
        for (const method of ["get", "GET", "head", "options", undefined]) {
            expect(
                requestEndsPageHistory(method, "/bloom/api/audio/startRecord"),
            ).toBe(false);
        }
    });

    it("ends the history for a POST, PUT or DELETE that is not on the list", () => {
        expect(
            requestEndsPageHistory("post", "/bloom/api/editView/setTopic"),
        ).toBe(true);
        expect(
            requestEndsPageHistory(
                "post",
                "/bloom/api/audio/startRecord?id=i1234",
            ),
        ).toBe(true);
        expect(
            requestEndsPageHistory(
                "delete",
                "/bloom/api/readers/io/allowedWordsList",
            ),
        ).toBe(true);
        expect(
            requestEndsPageHistory("put", "/bloom/api/some/newRequest"),
        ).toBe(true);
    });

    it("keeps the history for the requests that MINIMUM item 7 names", () => {
        for (const path of [
            "imageGallery/imageGalleryResult",
            "editView/pasteImage",
            "editView/paste",
            "editView/topBarButtonClick",
            "audio/copyAudioFile",
            "fileIO/chooseFile",
            "editView/pageContent",
        ]) {
            expect(requestEndsPageHistory("post", "/bloom/api/" + path)).toBe(
                false,
            );
        }
    });

    it("ends the history for the requests of CODE-WALK Part 3 that change what the page names", () => {
        for (const path of [
            "changeLayout",
            "common/saveChangesAndRethinkPageEvent",
            "editView/topBar/layoutChoiceChange",
            "editView/topBar/contentLanguageUsageChange",
            "book/settings",
            "editView/setTopic",
            "editView/setCustomPageLayout",
            "copyrightAndLicense/imageCopyrightAndLicense",
            "toolbox/leveled",
            "audio/startRecord",
            "audio/deleteSegment",
            "fileIO/copyFile",
            "readers/io/readerToolSettings",
        ]) {
            expect(requestEndsPageHistory("post", "/bloom/api/" + path)).toBe(
                true,
            );
        }
    });

    it("keeps the history for recording and importing a sign language video, which write new files", () => {
        for (const path of [
            "signLanguage/recordedVideo",
            "signLanguage/chooseVideo",
            "signLanguage/processVideo",
            "signLanguage/cancelImportVideo",
        ]) {
            expect(requestEndsPageHistory("post", "/bloom/api/" + path)).toBe(
                false,
            );
        }
    });

    it("keeps the history for the progress dialog that closes after a sign language video is imported", () => {
        for (const path of [
            "progress/ready",
            "progress/cancel",
            "progress/closed",
        ]) {
            expect(requestEndsPageHistory("post", "/bloom/api/" + path)).toBe(
                false,
            );
        }
    });

    it("ends the history for the general copy of a file, which may replace one that a page names", () => {
        expect(
            requestEndsPageHistory("post", "/bloom/api/fileIO/copyFile"),
        ).toBe(true);
    });

    it("keeps the history for a link grid's thumbnail, which only rewrites a linked book's cover", () => {
        expect(
            requestEndsPageHistory(
                "post",
                "/bloom/api/editView/addImageFromUrl",
            ),
        ).toBe(false);
    });

    it("ends the history for an AI image editor commit, which may delete an ai-image file that it replaced on another page", () => {
        // AiImageEditorApi's DeleteSupersededAiImageFiles. An undo after it could put back a page
        // naming that file.
        expect(
            requestEndsPageHistory(
                "post",
                "/bloom/api/aiImageEditor/commit?session=abc",
            ),
        ).toBe(true);
    });

    it("keeps the history for the reader tools' language data, which goes into a collection file that no page names", () => {
        // Posted whenever the reader tools load the sample texts' words, as Talking Book's opening
        // can make them do.
        expect(
            requestEndsPageHistory(
                "post",
                "/bloom/api/readers/io/synphonyLanguageData",
            ),
        ).toBe(false);
    });

    it("ignores the query string, the host, a trailing slash and case", () => {
        expect(
            requestEndsPageHistory(
                "post",
                "/bloom/api/aiImageEditor/launch?session=abc",
            ),
        ).toBe(false);
        expect(
            requestEndsPageHistory(
                "post",
                "http://localhost:8089/bloom/api/audio/copyAudioFile?oldId=a&newId=b",
            ),
        ).toBe(false);
        expect(
            requestEndsPageHistory("post", "/bloom/api/editView/paste/"),
        ).toBe(false);
        expect(
            requestEndsPageHistory("post", "/bloom/api/app/UserSetting"),
        ).toBe(false);
        expect(
            requestEndsPageHistory(
                "post",
                "http://localhost:8089/bloom/api/editView/setTopic",
            ),
        ).toBe(true);
    });

    it("ignores requests that don't go to Bloom's API", () => {
        expect(
            requestEndsPageHistory("post", "https://example.org/bloom/upload"),
        ).toBe(false);
        expect(requestEndsPageHistory("post", undefined)).toBe(false);
    });
});

describe("the request interceptor", () => {
    // Answers every request without a server, and notes how many clears had happened when the
    // request would have gone out.
    let clearsWhenSent: number[] = [];
    const adapter = (config: AxiosRequestConfig): Promise<AxiosResponse> => {
        clearsWhenSent.push(history.clearCount);
        return Promise.resolve({
            data: "",
            status: 200,
            statusText: "OK",
            headers: {},
            config,
        });
    };

    afterEach(() => {
        clearsWhenSent = [];
    });

    it("ends the history before sending a POST that is not on the list", async () => {
        expect(history.clearCount).toBe(0);
        await axios.post("/bloom/api/editView/setTopic", "x", { adapter });
        expect(clearsWhenSent).toEqual([1]);
        expect(history.clearCount).toBe(1);
    });

    it("catches a DELETE sent with axios directly", async () => {
        await axios.delete("/bloom/api/readers/io/allowedWordsList", {
            adapter,
        });
        expect(history.clearCount).toBe(1);
    });

    it("leaves the history alone for a POST on the list and for a GET", async () => {
        await axios.post("/bloom/api/editView/isTextSelected", "true", {
            adapter,
        });
        await axios.get("/bloom/api/editView/setTopic", { adapter });
        expect(clearsWhenSent).toEqual([0, 0]);
        expect(history.clearCount).toBe(0);
    });

    it("names the request as the reason that it ends the history, for Bloom's log", async () => {
        await axios.post("/bloom/api/editView/setTopic/?topic=Fiction", "x", {
            adapter,
        });
        await axios.post("/bloom/api/editView/isTextSelected", "true", {
            adapter,
        });
        expect(history.reasons).toEqual(["request editview/settopic"]);
    });

    it("leaves the history alone for the two posts that report an uncaught JavaScript error", async () => {
        // The posts of lib/errorHandler.ts, which skips them in unit tests.
        const report = JSON.stringify({ message: "Uncaught Error", stack: "" });
        await axios.post("/bloom/api/common/preliminaryError", report, {
            adapter,
        });
        await axios.post("/bloom/api/common/error", report, { adapter });
        expect(clearsWhenSent).toEqual([0, 0]);
        expect(history.clearCount).toBe(0);
    });
});
