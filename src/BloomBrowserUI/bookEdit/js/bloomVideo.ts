import "../../lib/jquery.resize"; // makes jquery resize work on all elements
import { get, post, postThatMightNavigate } from "../../utils/bloomApi";

// The code in this file supports operations on video panels in custom pages (and potentially elsewhere).
// It sets things up for the button (plural eventually) to appear when hovering over the video.
// Currently the button actions are entirely in C#.

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { getToolboxBundleExports } from "./bloomFrames";
import {
    SignLanguageToolControls,
    SignLanguageTool
} from "../toolbox/signLanguage/signLanguageTool";
import { kOverlayToolId } from "../toolbox/toolIds";
import { selectVideoContainer } from "./videoUtils";
import { getPlayIcon } from "../img/playIcon";
import { getPauseIcon } from "../img/pauseIcon";
import { getReplayIcon } from "../img/replayIcon";
import { kTextOverPictureSelector } from "./bubbleManager";

export function SetupVideoEditing(container) {
    get("settings/enterpriseEnabled", result => {
        const isEnterpriseEnabled: boolean = result.data;
        $(container)
            .find(".bloom-videoContainer")
            .each((index, vc) => {
                SetupVideoContainer(vc, isEnterpriseEnabled);
            });
    });
    Array.from(container.getElementsByTagName("video")).forEach(
        (videoElement: HTMLVideoElement) => {
            videoElement.removeAttribute("controls");
            // I don't think we need to do this in normal operation, but it's useful when
            // debugging, and just might prevent a problem in normal operation.
            videoElement.parentElement?.classList.remove("playing");
            videoElement.parentElement?.classList.remove("paused");
            videoElement.addEventListener("click", handleVideoClick);
            const playButton = wrapVideoIcon(
                videoElement,
                // Alternatively, we could import the Material UI icon, make this file a TSX, and use
                // ReactDom.render to render the icon into the div. But just creating the SVG
                // ourselves (as these methods do) seems more natural to me. We would not be using
                // React for anything except to make use of an image which unfortunately is only
                // available by default as a component.
                getPlayIcon("#ffffff", videoElement),
                "bloom-videoPlayIcon"
            );
            playButton.addEventListener("click", handlePlayClick);
            const pauseButton = wrapVideoIcon(
                videoElement,
                getPauseIcon("#ffffff", videoElement),
                "bloom-videoPauseIcon"
            );
            pauseButton.addEventListener("click", handlePauseClick);
            const replayButton = wrapVideoIcon(
                videoElement,
                getReplayIcon("#ffffff", videoElement),
                "bloom-videoReplayIcon"
            );
            replayButton.addEventListener("click", handleReplayClick);
        }
    );
}

function SetupVideoContainer(
    videoContainerDiv: Element,
    isEnterpriseEnabled: boolean
) {
    const videoElts = videoContainerDiv.getElementsByTagName("video");
    for (let i = 0; i < videoElts.length; i++) {
        const video = videoElts[i] as HTMLVideoElement;
        // Early sign language code included this; now we do it only on hover.
        video.removeAttribute("controls");
        video.addEventListener("playing", e => videoPlayingEventHandler(e));
        video.addEventListener("ended", e => videoEndedEventHandler(e));
    }

    SetupClickToShowSignLanguageTool(videoContainerDiv);

    // BL-6133 - Only set up the Change Video button on the container,
    //   if Enterprise features are enabled.
    if (isEnterpriseEnabled) {
        theOneLocalizationManager
            .asyncGetText(
                "EditTab.Toolbox.SignLanguage.ImportVideo",
                "Import Video",
                ""
            )
            .done(changeVideoText => {
                $(videoContainerDiv)
                    .mouseenter(function() {
                        const $this = $(this);

                        //SetImageTooltip(containerDiv, img);

                        // Enhance: we will have to do something about license information for videos, but it's complicated.
                        // I don't think we have fully determined how to store the information with the video, though I believe
                        // we can embed EXIF data as we do for pictures. But rights over a video are more complicated.
                        // Many people may have rights if they haven't been explicitly given up...producer, videographer,
                        // copyright owner of script, actors, owners of music used, copyright owner of work script is based on,
                        // possibly some subject matter may be copyright (the Eiffel tower at night is a notorious example).
                        // if (IsImageReal(img)) {
                        //     $this.prepend('<button class="editMetadataButton imageButton ' + buttonModifier + '" title="' +
                        //         theOneLocalizationManager.getText('EditTab.Image.EditMetadata') + '"></button>');
                        //     $this.find('.miniButton').each(function () {
                        //         $(this).removeClass('disabled');
                        //     });
                        // }

                        $this.addClass("hoverUp");
                    })
                    .mouseleave(function() {
                        const $this = $(this);
                        $this.removeClass("hoverUp");
                    });
            });
    }
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
    endPoint: number
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
                video
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

// Returns true if the element is a child-or-self of an image container
// (e.g. video-over-picture, text-over-picture)
function isOverPicture(element: Element): boolean {
    return !!element.closest(".bloom-imageContainer");
}

function SetupClickToShowSignLanguageTool(containerDiv: Element) {
    // if the user clicks on the video placeholder (or the video for that matter--see BL-6149),
    // bring up the sign language tool
    $(containerDiv).click(ev => {
        if ((ev.currentTarget as HTMLElement).closest(".drag-activity-play")) {
            return;
        }

        // In comic mode (overlay tool), suppress the click handler of video-over-picture elements so it won't take us to the sign
        // language tool, but everywhere else we want a click on a video element to take us to the SL tool
        const toolbox = getToolboxBundleExports()?.getTheOneToolbox();
        const currentToolId = toolbox?.getCurrentTool()?.id();

        if (
            toolbox?.toolboxIsShowing() &&
            currentToolId === kOverlayToolId &&
            isOverPicture(containerDiv)
        ) {
            // Looks like a video-over-picture, and we're showing the overlay tool. Don't switch to SL tool.
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
    command: "choose" | "record" | "playEarlier" | "playLater"
) {
    if (command === "choose" && videoContainer) {
        post("signLanguage/importVideo", result => {
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
        const previousVideoContainer = findPreviousVideoContainer(
            videoContainer
        );
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
    videoContainer: Element
): Element | undefined {
    const overlay = videoContainer.closest(".bloom-textOverPicture"); // unfortunate name for picture and video overlay containers
    if (overlay) {
        let next = overlay.nextElementSibling;
        while (next) {
            if (
                next.firstElementChild?.classList.contains(
                    "bloom-videoContainer"
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
    videoContainer: Element
): Element | undefined {
    const overlay = videoContainer.closest(".bloom-textOverPicture"); // unfortunate classname for picture and video overlay containers
    if (overlay) {
        let previous = overlay.previousElementSibling;
        while (previous) {
            if (
                previous.firstElementChild?.classList.contains(
                    "bloom-videoContainer"
                )
            ) {
                return previous.firstElementChild;
            }
            previous = previous.previousElementSibling;
        }
    }
    return undefined;
}
// Swap the positions of two video containers (actually their parent overlays) in the DOM.
function SwapVideoPositionsInDom(
    firstVideoContainer: Element,
    secondVideoContainer: Element
) {
    const firstOverlay = firstVideoContainer.closest(".bloom-textOverPicture");
    const secondOverlay = secondVideoContainer.closest(
        ".bloom-textOverPicture"
    );
    if (!firstOverlay || !secondOverlay) {
        return;
    }
    const overlayContainer = firstOverlay.parentElement;
    if (!overlayContainer || overlayContainer !== secondOverlay.parentElement) {
        return;
    }
    const thirdOverlay = secondOverlay.nextElementSibling; // may be null, but that's okay
    overlayContainer.insertBefore(secondOverlay, firstOverlay);
    if (firstOverlay.nextElementSibling !== thirdOverlay) {
        overlayContainer.insertBefore(firstOverlay, thirdOverlay);
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
    videoElement: HTMLVideoElement,
    icon: HTMLElement,
    iconClass: string
): HTMLElement {
    const wrapper = videoElement.ownerDocument.createElement("div");
    wrapper.classList.add("bloom-videoControlContainer");
    wrapper.classList.add("bloom-ui"); // don't save as part of document
    wrapper.appendChild(icon);
    wrapper.classList.add(iconClass);
    icon.classList.add("bloom-videoControl");
    videoElement.parentElement?.appendChild(wrapper);
    return icon;
}

// The one we most recently started playing or paused. If we hit play on this one,
// we don't start from the beginning.
let currentVideoElement: HTMLVideoElement | null = null;

// Handles a click on the play button. This is ignored here if the video is in an overlay
// and we're not in Play mode, so the bubbleManager can decide if it's a drag or a click.
// (This is also called by code in bubbleManager, when it determines that mouse activity
// on the button SHOULD be considered a click, not a drag of the overlay. The event is
// then actually from the mouseup, and forcePlay is true.)
export function handlePlayClick(ev: MouseEvent, forcePlay?: boolean) {
    const video = (ev.target as HTMLElement)
        ?.closest(".bloom-videoContainer")
        ?.getElementsByTagName("video")[0];
    if (!video) {
        return; // should not happen
    }
    // If we're in an overlay, we don't want this handler to play the video,
    // becuse the click might be a drag on the overlay. We'll let bubbleManager
    // decide and call playVideo if appropriate. That is, if we're not in Play mode,
    // where dragging is not applicable, or being called FROM the bubbleManager.
    if (
        !forcePlay &&
        video.closest(kTextOverPictureSelector) &&
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
    // At least I don't think so. Outside Play mode, clicking on overlays is mainly about moving
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
