import { GetButtonModifier } from "./bloomImages";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { BloomApi } from "../../utils/bloomApi";

// Code related to the fourth option for types of origami panel content, HTML Widget
// An HTML widget must be a zip file (with extension wdgt) containing a self-contained web page rooted at
// index.htm{l}, which must occur in the root directory of the zip file.
// We might enhance it one day to support more of the Apple HTML5 widget file format,
// (https://support.apple.com/en-us/HT204433)
// such as using information in Info.plist to locate the root file.

// BL-9319 Custom widgets should only be Enterprise-enabled.

// Initialization function, sets up all the editing functions we support for these elements.
export function SetupWidgetEditing(container: HTMLElement): void {
    BloomApi.get("settings/enterpriseEnabled", result => {
        const isEnterpriseEnabled: boolean = result.data;
        if (isEnterpriseEnabled) {
            const widgets = Array.from(
                container.getElementsByClassName("bloom-widgetContainer")
            );
            widgets.forEach(w => SetupWidget(w));
        }
    });
}

function SetupWidget(w: Element): void {
    if (w.matches(":hover")) {
        w.classList.add("hoverUp");
    } else {
        w.classList.remove("hoverUp");
    }
    const buttonModifier = GetButtonModifier(w);

    w.addEventListener("mouseenter", () => {
        // I'm not enthusiastic about this approach to making the "choose" button
        // appear on hover, but various classes are shared with other such buttons
        // so it seemed better not to take a different approach here.
        w.classList.add("hoverUp");
        const wrapper = document.createElement("div");
        wrapper.innerHTML =
            '<button class="chooseWidgetButton imageButton imageOverlayButton ' +
            buttonModifier +
            '" title="' +
            theOneLocalizationManager.getText(
                "EditTab.Widget.ChooseWidget",
                "Choose Widget"
            ) +
            '"></button>';
        const chooseButton = wrapper.firstChild as HTMLElement;
        w.appendChild(chooseButton);
        chooseButton.onclick = () => {
            // The C# code displays the choose file dialog, unzips the widget into
            // an appropriate place, and returns us a relative path to it suitable
            // for the iframe src. Or an empty string, if the user canceled.
            BloomApi.getString("editView/chooseWidget", widgetSrc => {
                if (!widgetSrc) {
                    return; // user canceled.
                }
                let iframe = w.getElementsByTagName("iframe")[0];
                if (!iframe) {
                    iframe = document.createElement("iframe");
                    w.appendChild(iframe);
                }
                iframe.setAttribute("src", widgetSrc);
                w.classList.remove("bloom-noWidgetSelected");
                const page = w.closest(".bloom-page");
                page!.classList.add("bloom-interactive-page");
                page!.setAttribute("data-activity", "iframe");
            });
        };
    });
    w.addEventListener("mouseleave", () => {
        w.classList.remove("hoverUp");
        const buttons = Array.from(
            w.getElementsByClassName("imageOverlayButton")
        );
        buttons.forEach((btn: HTMLElement) =>
            btn.parentElement!.removeChild(btn)
        );
    });
}
