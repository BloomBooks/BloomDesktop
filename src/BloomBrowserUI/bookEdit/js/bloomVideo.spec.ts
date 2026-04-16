import { describe, expect, it } from "vitest";
import {
    removeTransientVideoTimestampParams,
    stripTransientVideoTimestampParam,
} from "./bloomVideo";

describe("bloomVideo transient video timestamp cleanup", () => {
    it("removes only the transient video timestamp param", () => {
        const cleaned = stripTransientVideoTimestampParam(
            "video.mp4?persist=true&bloomVideoTransientTimestamp=123#t=1,2",
        );

        expect(cleaned).toBe("video.mp4?persist=true#t=1,2");
    });

    it("leaves urls without the transient param unchanged", () => {
        const cleaned = stripTransientVideoTimestampParam(
            "video.mp4?persist=true#t=1,2",
        );

        expect(cleaned).toBe("video.mp4?persist=true#t=1,2");
    });

    it("cleans transient params from video sources in the page", () => {
        document.body.innerHTML =
            '<div><video src="movie.mp4?bloomVideoTransientTimestamp=7"></video><video><source src="clip.mp4?keep=1&bloomVideoTransientTimestamp=8#t=2,4"></source></video></div>';

        removeTransientVideoTimestampParams(document.body);

        const videos = Array.from(document.body.querySelectorAll("video"));
        expect(videos[0].getAttribute("src")).toBe("movie.mp4");
        expect(videos[1].querySelector("source")?.getAttribute("src")).toBe(
            "clip.mp4?keep=1#t=2,4",
        );
    });
});
