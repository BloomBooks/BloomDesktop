import "../../lib/jquery.resize"; // makes jquery resize work on all elements
import axios from "axios";
import { GetButtonModifier } from "./bloomImages";

// The code in this file supports operations on video panels in custom pages (and potentially elsewhere).
// It sets things up for the button (plural eventually) to appear when hovering over the video.
// Currently the button actions are entirely in C#.

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";

export function SetupVideoEditing(container) {
    $(container).find(".bloom-videoContainer").each((index, vc) => {
        SetupVideoContainer(vc);
    });
}

function SetupVideoContainer(containerDiv) {
    $(containerDiv).mouseenter(function () {
        var $this = $(this);

        var buttonModifier = GetButtonModifier($this);

        // The code that executes when this button is clicked is currently C#.
        // See EditingView._browser1_OnBrowserClick for the start of the chain.
        $this.prepend("<button class='changeVideoButton imageButton " + buttonModifier +
            "' title='" + theOneLocalizationManager.getText("EditTab.Video.ChangeVideo") + "'></button>");

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
        .mouseleave(function () {
            var $this = $(this);
            $this.removeClass("hoverUp");
            $this.find(".changeVideoButton").each(function () {
                $(this).remove();
            });

            // $this.find('.editMetadataButton').each(function () {
            //     if (!$(this).hasClass('imgMetadataProblem')) {
            //         $(this).remove();
            //     }
            // });
        });
}