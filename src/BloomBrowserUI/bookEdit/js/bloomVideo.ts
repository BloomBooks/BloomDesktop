import "../../lib/jquery.resize"; // makes jquery resize work on all elements
import { get } from "../../utils/bloomApi";

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

const mouseOverFunction = e => {
    const target = e.target as HTMLElement;
    if (!target) {
        return; // can this happen?
    }
    if (target.tagName.toLowerCase() === "video") {
        if (
            (e.altKey || e.ctrlKey) &&
            target.closest(".bloom-textOverPicture")
        ) {
            target.removeAttribute("controls"); // trying to move/resize video container
        } else {
            target.setAttribute("controls", ""); // attribute just has to exist to work
        }
    }
};

const mouseOutFunction = e => {
    const target = e.target as HTMLElement;
    if (!target) {
        return; // can this happen?
    }
    if (target.tagName.toLowerCase() === "video") {
        target.removeAttribute("controls");
    }
};

export function SetupVideoEditing(container) {
    get("settings/enterpriseEnabled", result => {
        const isEnterpriseEnabled: boolean = result.data;
        $(container)
            .find(".bloom-videoContainer")
            .each((index, vc) => {
                SetupVideoContainer(vc, isEnterpriseEnabled);
            });
        // We use mouseover rather than mouseenter and mouseout rather than mouseleave
        // and attach to the body rather than individual videos so that we only have
        // to do it once, and don't have to worry about attaching them to newly
        // created videos. However, this function can be called again, and we only
        // want one, so we remove before adding.
        document.body.removeEventListener("mouseover", mouseOverFunction);
        document.body.addEventListener("mouseover", mouseOverFunction);
        document.body.removeEventListener("mouseout", mouseOutFunction);
        document.body.addEventListener("mouseout", mouseOutFunction);
    });
}

function SetupVideoContainer(
    containerDiv: Element,
    isEnterpriseEnabled: boolean
) {
    const videoElts = containerDiv.getElementsByTagName("video");
    for (let i = 0; i < videoElts.length; i++) {
        const video = videoElts[i] as HTMLVideoElement;
        // Early sign language code included this; now we do it only on hover.
        video.removeAttribute("controls");
        video.addEventListener("playing", e => videoPlayingEventHandler(e));
        video.addEventListener("ended", e => videoSetupEventHandler(e));
    }

    SetupClickToShowSignLanguageTool(containerDiv);

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
                $(containerDiv)
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

function videoSetupEventHandler(e: Event) {
    const video = e.target as HTMLVideoElement;
    const start: number = getVideoStartSeconds(video);
    SignLanguageTool.setCurrentVideoPoint(start, video);
}

function videoPlayingEventHandler(e: Event) {
    // The main purpose of this handler is to stop the playback when the video reaches
    // the endPoint set by the user, since the (e.g.) "#t=1.3,3.4" format seems to stop appropriately in Firefox,
    // but not in Geckofx. I've tested it in FF45 and FF63 and the video controls respect the segment timing.
    // It is conceivable that we won't need this code if we can figure out why Bloom's Geckofx isn't respecting
    // the timings. Currently running our code on Geckofx60 gets us a NotImplementedException inside of
    // the SignLanguageApi C# code (in Geckofx-Core).
    const video = e.target as HTMLVideoElement;
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
    $(containerDiv).click(() => {
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
