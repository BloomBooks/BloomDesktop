import { post, postThatMightNavigate } from "../../utils/bloomApi";

// The code in this file supports operations on video panels in custom pages (and potentially elsewhere).
// It sets things up for the button (plural eventually) to appear when hovering over the video.
// Currently the button actions are entirely in C#.

import { getToolboxBundleExports } from "./bloomFrames";
import {
    SignLanguageToolControls,
    SignLanguageTool,
} from "../toolbox/signLanguage/signLanguageTool";
import { kGameToolId, kCanvasToolId } from "../toolbox/toolIds";
import { selectVideoContainer } from "./videoUtils";
import { getPlayIcon } from "../img/playIcon";
import { getPauseIcon } from "../img/pauseIcon";
import { getReplayIcon } from "../img/replayIcon";
import { kCanvasElementSelector } from "../toolbox/canvas/canvasElementUtils";
import $ from "jquery";

export function SetupVideoEditing(container) {
    $(container)
        .find(".bloom-videoContainer")
        .each((index, vc) => {
            SetupVideoContainer(vc);
        });
    Array.from(container.getElementsByTagName("video")).forEach(
        (videoElement: HTMLVideoElement) => {
            videoElement.removeAttribute("controls");
            // I don't think we need to do this in normal operation, but it's useful when
            // debugging, and just might prevent a problem in normal operation.
            videoElement.parentElement?.classList.remove("playing");
            videoElement.parentElement?.classList.remove("paused");
            const mouseDetector =
                videoElement.ownerDocument.createElement("div");
            mouseDetector.classList.add("bloom-videoMouseDetector");
            mouseDetector.classList.add("bloom-ui"); // don't save as part of document
            mouseDetector.addEventListener("click", handleVideoClick);
            videoElement.parentElement?.appendChild(mouseDetector);
            const playButton = wrapVideoIcon(
                mouseDetector,
                // Alternatively, we could import the Material UI icon, make this file a TSX, and use
                // ReactDom.render to render the icon into the div. But just creating the SVG
                // ourselves (as these methods do) seems more natural to me. We would not be using
                // React for anything except to make use of an image which unfortunately is only
                // available by default as a component.
                getPlayIcon("#ffffff", videoElement),
                "bloom-videoPlayIcon",
            );
            playButton.addEventListener("click", handlePlayClick);
            const pauseButton = wrapVideoIcon(
                mouseDetector,
                getPauseIcon("#ffffff", videoElement),
                "bloom-videoPauseIcon",
            );
            pauseButton.addEventListener("click", handlePauseClick);
            const replayButton = wrapVideoIcon(
                mouseDetector,
                getReplayIcon("#ffffff", videoElement),
                "bloom-videoReplayIcon",
            );
            replayButton.addEventListener("click", handleReplayClick);
        },
    );
}

function SetupVideoContainer(videoContainerDiv: Element) {
    const videoElts = videoContainerDiv.getElementsByTagName("video");
    for (let i = 0; i < videoElts.length; i++) {
        const video = videoElts[i] as HTMLVideoElement;
        // Early sign language code included this; now we do it only on hover.
        video.removeAttribute("controls");
        video.addEventListener("playing", (e) => videoPlayingEventHandler(e));
        video.addEventListener("ended", (e) => videoEndedEventHandler(e));
    }

    SetupClickToShowSignLanguageTool(videoContainerDiv);
}

function videoEndedEventHandler(e: Event) {
    const video = e.target as HTMLVideoElement;
    const start: number = getVideoStartSeconds(video);
    SignLanguageTool.setCurrentVideoPoint(start, video);
    video.parentElement?.classList.remove("playing");
}

function videoPlayingEventHandler(e: Event) {
    // The main purpose of this handler is to stop the playback when the video reaches
    // the endPoint set by the user, since the (e.g.) "#t=1.3,3.4" format seems to stop appropriately in Firefox,
    // but not in Geckofx. I've tested it in FF45 and FF63 and the video controls respect the segment timing.
    // It is conceivable that we won't need this code if we can figure out why Bloom's Geckofx isn't respecting
    // the timings. Currently running our code on Geckofx60 gets us a NotImplementedException inside of
    // the SignLanguageApi C# code (in Geckofx-Core).
    const video = e.currentTarget as HTMLVideoElement;
    video.parentElement?.classList.add("playing");
    video.parentElement?.classList.remove("paused");
    currentVideoElement = video;
    let end: number = getVideoEndSeconds(video);
    const untrimmedEndPoint: number = 0.0;
    if (end == untrimmedEndPoint) {
        // We can't just set the endpoint to equal the duration of the video here, because
        // we will be testing that the current playback time is greater than the endpoint.
        // Since we test for the end of the video every 1/10th of a second, set the endpoint
        // slightly more than 1/10th of a second from the end of the video.
        end = video.duration - 0.11;
    }
    resetToStartAfterPlayingToEndPoint(video, end);
}

function resetToStartAfterPlayingToEndPoint(
    video: HTMLVideoElement,
    endPoint: number,
) {
    window.setTimeout(() => {
        if (video.currentTime > endPoint) {
            video.pause();
            // For some unknown reason, this video seems to have passed its end point without
            // reaching the end and raising the ended event.
            // Raise the ended event in case anything is listening for it.
            const endedEvent = new Event("ended");
            video.dispatchEvent(endedEvent);
            SignLanguageTool.setCurrentVideoPoint(
                getVideoStartSeconds(video),
                video,
            );
        } else {
            // Check again in another 100ms.
            resetToStartAfterPlayingToEndPoint(video, endPoint);
        }
    }, 100);
}

function getVideoStartSeconds(videoElt: HTMLVideoElement): number {
    const src = SignLanguageTool.getSrcAttribute(videoElt);
    if (src === "") {
        return 0.0;
    }
    const urlTimingObj = SignLanguageTool.parseVideoSrcAttribute(src);
    return parseFloat(urlTimingObj.start);
}

function getVideoEndSeconds(videoElt: HTMLVideoElement): number {
    const src = SignLanguageTool.getSrcAttribute(videoElt);
    if (src === "") {
        return 0.0;
    }
    const urlTimingObj = SignLanguageTool.parseVideoSrcAttribute(src);
    return parseFloat(urlTimingObj.end);
}

function SetupClickToShowSignLanguageTool(videoContainerDiv: Element) {
    // if the user clicks on the video placeholder (or the video for that matter--see BL-6149),
    // bring up the sign language tool
    $(videoContainerDiv).click((ev) => {
        if ((ev.currentTarget as HTMLElement).closest(".drag-activity-play")) {
            return;
        }

        // In comic mode (canvas element tool), suppress the click handler of video-over-picture elements so it won't take us to the sign
        // language tool, but everywhere else we want a click on a video element to take us to the SL tool
        const toolbox = getToolboxBundleExports()?.getTheOneToolbox();
        const currentToolId = toolbox?.getCurrentTool()?.id();

        if (
            toolbox?.toolboxIsShowing() &&
            (currentToolId === kCanvasToolId ||
                currentToolId === kGameToolId) &&
            videoContainerDiv.closest(kCanvasElementSelector) // only ones actually in a canvas element
        ) {
            // Looks like a video-over-picture, and we're showing the canvas element or game tool. Don't switch to SL tool.
            return;
        }

        showSignLanguageTool();
    });
}

export function showSignLanguageTool() {
    getToolboxBundleExports()
        ?.getTheOneToolbox()
        .activateToolFromId(SignLanguageToolControls.kToolID);
}

export function doVideoCommand(
    videoContainer: Element,
    command: "choose" | "record" | "playEarlier" | "playLater",
) {
    if (command === "choose" && videoContainer) {
        post("signLanguage/importVideo", (result) => {
            if (result.data) {
                updateVideoInContainer(videoContainer, result.data);
                // Makes sure the page gets saved with a reference to the new video,
                // and incidentally that everything gets updated to be consistent with the
                // new state of things.
                postThatMightNavigate("common/saveChangesAndRethinkPageEvent");
            }
        });
    } else if (command === "record") {
        // There may be more than one video container on the page.  Make sure the
        // one we want to record into is selected.  See comments in BL-13930.
        selectVideoContainer(videoContainer);
        showSignLanguageTool();
    } else if (command === "playEarlier") {
        // Find the preceding video container element, if any, and move it after the current one
        const previousVideoContainer =
            findPreviousVideoContainer(videoContainer);
        if (previousVideoContainer) {
            SwapVideoPositionsInDom(previousVideoContainer, videoContainer);
        }
    } else if (command === "playLater") {
        // Find the next video container element, if any, and move it before the current one
        const nextVideoContainer = findNextVideoContainer(videoContainer);
        if (nextVideoContainer) {
            SwapVideoPositionsInDom(videoContainer, nextVideoContainer);
        }
    }
}

export function findNextVideoContainer(
    videoContainer: Element,
): Element | undefined {
    const canvasElement = videoContainer.closest(kCanvasElementSelector);
    if (canvasElement) {
        let next = canvasElement.nextElementSibling;
        while (next) {
            if (
                next.firstElementChild?.classList.contains(
                    "bloom-videoContainer",
                )
            ) {
                return next.firstElementChild;
            }
            next = next.nextElementSibling;
        }
    }
    return undefined;
}
export function findPreviousVideoContainer(
    videoContainer: Element,
): Element | undefined {
    const canvasElement = videoContainer.closest(kCanvasElementSelector);
    if (canvasElement) {
        let previous = canvasElement.previousElementSibling;
        while (previous) {
            if (
                previous.firstElementChild?.classList.contains(
                    "bloom-videoContainer",
                )
            ) {
                return previous.firstElementChild;
            }
            previous = previous.previousElementSibling;
        }
    }
    return undefined;
}
// Swap the positions of two video containers (actually their parent canvas elements) in the DOM.
function SwapVideoPositionsInDom(
    firstVideoContainer: Element,
    secondVideoContainer: Element,
) {
    const firstCanvasElement = firstVideoContainer.closest(
        kCanvasElementSelector,
    );
    const secondCanvasElement = secondVideoContainer.closest(
        kCanvasElementSelector,
    );
    if (!firstCanvasElement || !secondCanvasElement) {
        return;
    }
    const container = firstCanvasElement.parentElement;
    if (!container || container !== secondCanvasElement.parentElement) {
        return;
    }
    const thirdCanvasElement = secondCanvasElement.nextElementSibling; // may be null, but that's okay
    container.insertBefore(secondCanvasElement, firstCanvasElement);
    if (firstCanvasElement.nextElementSibling !== thirdCanvasElement) {
        container.insertBefore(firstCanvasElement, thirdCanvasElement);
    }
}

export function updateVideoInContainer(container: Element, url: string): void {
    let video = container.getElementsByTagName("video")[0];
    if (!video && container.ownerDocument) {
        video = container.ownerDocument.createElement("video");
        container.appendChild(video);
    }
    if (video) {
        let source = video.getElementsByTagName("source")[0];
        if (!source && container.ownerDocument) {
            source = container.ownerDocument.createElement("source");
            video.appendChild(source);
        }
        if (source) {
            source.setAttribute("src", url);
            // Transparent background videos allow the placeholder to show.  See BL-13918.
            container.classList.remove("bloom-noVideoSelected");
        }
    }
}

// configure one of the icons we display over videos. We put a div around it and apply
// various classes and append it to the parent of the video.
function wrapVideoIcon(
    parent: HTMLElement,
    icon: HTMLElement,
    iconClass: string,
): HTMLElement {
    const wrapper = parent.ownerDocument.createElement("div");
    wrapper.classList.add("bloom-videoControlContainer");
    wrapper.classList.add("bloom-ui"); // don't save as part of document
    wrapper.appendChild(icon);
    wrapper.classList.add(iconClass);
    icon.classList.add("bloom-videoControl");
    parent.appendChild(wrapper);
    return icon;
}

// The one we most recently started playing or paused. If we hit play on this one,
// we don't start from the beginning.
let currentVideoElement: HTMLVideoElement | null = null;

// Handles a click on the play button. This is ignored here if the video is in a canvas element
// and we're not in Play mode, so the CanvasElementManager can decide if it's a drag or a click.
// (This is also called by code in CanvasElementManager, when it determines that mouse activity
// on the button SHOULD be considered a click, not a drag of the canvas element. The event is
// then actually from the mouseup, and forcePlay is true.)
export function handlePlayClick(ev: MouseEvent, forcePlay?: boolean) {
    const video = (ev.target as HTMLElement)
        ?.closest(".bloom-videoContainer")
        ?.getElementsByTagName("video")[0];
    if (!video) {
        return; // should not happen
    }
    // If we're in a canvas element, we don't want this handler to play the video,
    // becuse the click might be a drag on the canvas element. We'll let CanvasElementManager
    // decide and call playVideo if appropriate. That is, if we're not in Play mode,
    // where dragging is not applicable, or being called FROM the CanvasElementManager.
    if (
        !forcePlay &&
        video.closest(kCanvasElementSelector) &&
        !video.closest(".drag-activity-play")
    ) {
        return;
    }
    ev.stopPropagation();
    ev.preventDefault();
    if (video !== currentVideoElement) {
        // a video we were not currently playing, start from the beginning.
        video.currentTime = 0;
    }
    play(video);
}

function handleReplayClick(ev: MouseEvent) {
    ev.stopPropagation();
    ev.preventDefault();
    const video = (ev.target as HTMLElement)
        ?.closest(".bloom-videoContainer")
        ?.getElementsByTagName("video")[0];
    if (!video) {
        return; // should not happen
    }
    video.currentTime = 0;
    play(video);
}

function play(video: HTMLVideoElement) {
    video.play();
}

// This is called when the user clicks the pause button on a video.
// Unlike when pause is done from the control bar, we add a class that shows some buttons.
function handlePauseClick(ev: MouseEvent) {
    ev.stopPropagation();
    ev.preventDefault();
    const video = (ev.target as HTMLElement)
        ?.closest(".bloom-videoContainer")
        ?.getElementsByTagName("video")[0];
    if (!video) return;
    // just possibly, the one we paused is not the one we most recently started playing.
    currentVideoElement = video;
    video?.parentElement?.classList.add("paused");
    video?.parentElement?.classList.remove("playing");
    video?.pause();
}

// In Bloom player, videos respond to a simple click on the video by either playing or
// pausing. In Bloom editor, we only want this behavior in Game Play mode, where we
// are simulating the player. (BloomPub preview uses actual BloomPlayer code). This would
// be a natural bit of code to put in dragActivityRuntime.ts, except we don't need
// it there, because BloomPlayer has this behavior for all videos, not just in Games.)
const handleVideoClick = (ev: MouseEvent) => {
    const video = ev.currentTarget as HTMLVideoElement;

    // If we're not in Play mode, we don't need these behaviors.
    // At least I don't think so. Outside Play mode, clicking on canvas elements is mainly about moving
    // them, but we have a visible Play button in case you want to play one. In BP (and Play mode), you
    // can't move them (unless one day we make them something you can drag to a target), so it
    // makes sense that a click anywhere on the video would play it; there's nothing else useful
    // to do in response.
    if (!video.closest(".drag-activity-play")) {
        return;
    }

    if (video.paused) {
        handlePlayClick(ev, true);
    } else {
        handlePauseClick(ev);
    }
};
