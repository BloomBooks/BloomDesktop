// Whether Bloom is holding the camera, as the Sign Language tool's document sees it.
//
// The Sign Language tool is the only part of Bloom that asks for the camera: it calls
// navigator.mediaDevices.getUserMedia in the toolbox's iframe and shows the stream in its monitor.
// Chromium lets go of the camera once every track of every stream it handed out has been stopped,
// so a track whose readyState is still "live" is a camera Bloom has not given back.
//
// A test cannot ask Windows which program has the camera, so watchCameraRequests wraps
// getUserMedia in the toolbox's document and keeps every stream it hands out. getLiveCameraTracks
// then counts the tracks that are still live, across every one of those streams, not just the one
// the monitor shows now: a stream the tool replaced without stopping would keep the camera too.
// Run under a Bloom launched with test.use({ fakeCamera: true }), where Chromium makes the camera up.

import type { Page } from "@playwright/test";
import { toolboxFrame } from "./toolbox";

interface ICameraWatch {
    streams: MediaStream[];
}

/**
 * Start keeping every stream the toolbox's document gets from getUserMedia, so getLiveCameraTracks
 * can count them. Call it before anything turns the camera on. Does nothing in a toolbox document
 * that is already watched; a toolbox that has reloaded since needs it again.
 */
export async function watchCameraRequests(page: Page): Promise<void> {
    await toolboxFrame(page).evaluate(() => {
        const w = window as unknown as { e2eCameraWatch?: ICameraWatch };
        if (w.e2eCameraWatch) return;
        const watch: ICameraWatch = { streams: [] };
        w.e2eCameraWatch = watch;
        const devices = navigator.mediaDevices;
        const getUserMedia = devices.getUserMedia.bind(devices);
        devices.getUserMedia = async (constraints) => {
            const stream = await getUserMedia(constraints);
            watch.streams.push(stream);
            return stream;
        };
    });
}

/** What the toolbox's document has had from the camera, and what it still holds. */
export interface ICameraUse {
    /** How many streams getUserMedia has handed the toolbox since watchCameraRequests. */
    streamsHandedOut: number;
    /** How many of those streams' tracks are still live, i.e. still holding the camera. */
    liveTracks: number;
}

/**
 * How many camera streams the toolbox has been handed since watchCameraRequests, and how many of
 * their tracks it still holds. Throws when the toolbox's document is not watched (it reloaded
 * after watchCameraRequests), since its count would then miss streams.
 */
export async function getCameraUse(page: Page): Promise<ICameraUse> {
    return toolboxFrame(page).evaluate(() => {
        const watch = (window as unknown as { e2eCameraWatch?: ICameraWatch })
            .e2eCameraWatch;
        if (!watch)
            throw new Error(
                "The toolbox's document is not watching the camera: it has reloaded since " +
                    "watchCameraRequests, so call that again before turning the camera on.",
            );
        return {
            streamsHandedOut: watch.streams.length,
            liveTracks: watch.streams
                .flatMap((s) => s.getTracks())
                .filter((t) => t.readyState === "live").length,
        };
    });
}
