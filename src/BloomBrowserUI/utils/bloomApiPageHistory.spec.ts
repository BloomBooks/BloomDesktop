import { afterEach, describe, expect, it, vi } from "vitest";
import axios, { AxiosRequestConfig, AxiosResponse } from "axios";

const history = vi.hoisted(() => ({ clearCount: 0 }));

vi.mock("../bookEdit/pageHistory/pageHistoryAccess", () => ({
    clearUndoAndRedoIfAny: () => {
        history.clearCount++;
    },
}));

import { requestEndsPageHistory } from "./bloomApi";

afterEach(() => {
    history.clearCount = 0;
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
            "aiImageEditor/commit",
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
            "editView/addImageFromUrl",
            "audio/startRecord",
            "audio/deleteSegment",
            "fileIO/copyFile",
            "signLanguage/deleteVideo",
            "readers/io/readerToolSettings",
        ]) {
            expect(requestEndsPageHistory("post", "/bloom/api/" + path)).toBe(
                true,
            );
        }
    });

    it("ignores the query string, the host, a trailing slash and case", () => {
        expect(
            requestEndsPageHistory(
                "post",
                "/bloom/api/aiImageEditor/commit?session=abc",
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
});
