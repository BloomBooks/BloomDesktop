import "../../lib/jquery.resize"; // makes jquery resize work on all elements
import { GetButtonModifier } from "./bloomImages";
import { BloomApi } from "../../utils/bloomApi";

// The code in this file supports operations on video panels in custom pages (and potentially elsewhere).
// It sets things up for the button (plural eventually) to appear when hovering over the video.
// Currently the button actions are entirely in C#.

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { getToolboxFrameExports } from "./bloomFrames";
import {
    SignLanguageToolControls,
    SignLanguageTool
} from "../toolbox/signLanguage/signLanguageTool";

const mouseOverFunction = e => {
    const target = e.target as HTMLElement;
    if (!target) {
        return; // can this happen?
    }
    if (target.tagName.toLowerCase() === "video") {
        target.setAttribute("controls", "true");
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
    BloomApi.get("featurecontrol/enterpriseEnabled", result => {
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

                        const buttonModifier = GetButtonModifier($this);

                        // The code that executes when this button is clicked is currently C#.
                        // See EditingView._browser1_OnBrowserClick for the start of the chain.
                        $this.prepend(
                            "<button class='importVideoButtonOverlay imageButton " +
                                buttonModifier +
                                "' title='" +
                                changeVideoText +
                                "'></button>"
                        );

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
                        $this
                            .find(".importVideoButtonOverlay")
                            .each(function() {
                                $(this).remove();
                            });
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
    if (end === -1.0) {
        end = video.duration;
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
            SignLanguageTool.setCurrentVideoPoint(
                getVideoStartSeconds(video),
                video
            );
        } else {
            resetToStartAfterPlayingToEndPoint(video, endPoint);
        }
    }, 200);
}

function getVideoStartSeconds(videoElt: HTMLVideoElement): number {
    const source = videoElt.getElementsByTagName(
        "source"
    )[0] as HTMLSourceElement;
    const src = source.getAttribute("src");
    const urlTimingObj = SignLanguageTool.parseVideoSrcAttribute(src);
    return parseFloat(urlTimingObj.start);
}

function getVideoEndSeconds(videoElt: HTMLVideoElement): number {
    const source = videoElt.getElementsByTagName(
        "source"
    )[0] as HTMLSourceElement;
    const src = source.getAttribute("src");
    const urlTimingObj = SignLanguageTool.parseVideoSrcAttribute(src);
    return parseFloat(urlTimingObj.end);
}

function SetupClickToShowSignLanguageTool(containerDiv: Element) {
    // if the user clicks on the video placeholder (or the video for that matter--see BL-6149),
    // bring up the sign language tool
    $(containerDiv).click(() => {
        getToolboxFrameExports()
            .getTheOneToolbox()
            .activateToolFromId(SignLanguageToolControls.kToolID);
    });
}
