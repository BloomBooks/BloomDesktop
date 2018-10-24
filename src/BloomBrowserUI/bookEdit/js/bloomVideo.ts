import "../../lib/jquery.resize"; // makes jquery resize work on all elements
import { GetButtonModifier } from "./bloomImages";
import { BloomApi } from "../../utils/bloomApi";

// The code in this file supports operations on video panels in custom pages (and potentially elsewhere).
// It sets things up for the button (plural eventually) to appear when hovering over the video.
// Currently the button actions are entirely in C#.

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { getToolboxFrameExports } from "./bloomFrames";
import { SignLanguageToolControls } from "../toolbox/signLanguage/signLanguageTool";

const mouseOverFunction = e => {
    var target = e.target as HTMLElement;
    if (!target) {
        return; // can this happen?
    }
    if (target.tagName.toLowerCase() === "video") {
        target.setAttribute("controls", "true");
    }
};

const mouseOutFunction = e => {
    var target = e.target as HTMLElement;
    if (!target) {
        return; // can this happen?
    }
    if (target.tagName.toLowerCase() === "video") {
        target.removeAttribute("controls");
    }
};

export function SetupVideoEditing(container) {
    BloomApi.get("featurecontrol/enterpriseEnabled", result => {
        const isEnterpriseEnabled = result.data;
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
    var videoElts = containerDiv.getElementsByTagName("video");
    for (var i = 0; i < videoElts.length; i++) {
        // Early sign language code included this; now we do it only on hover.
        videoElts[i].removeAttribute("controls");

        videoElts[i].addEventListener("ended", e => {
            var video = e.target as HTMLVideoElement;
            video.load(); // reset to the beginning
        });
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
            .done(function(changeVideoText) {
                $(containerDiv)
                    .mouseenter(function() {
                        var $this = $(this);

                        var buttonModifier = GetButtonModifier($this);

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
                        var $this = $(this);
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

function SetupClickToShowSignLanguageTool(containerDiv: Element) {
    // if the user clicks on the video placeholder (or the video for that matter--see BL-6149),
    // bring up the sign language tool
    $(containerDiv).click(function() {
        getToolboxFrameExports()
            .getTheOneToolbox()
            .activateToolFromId(SignLanguageToolControls.kToolID);
    });
}
