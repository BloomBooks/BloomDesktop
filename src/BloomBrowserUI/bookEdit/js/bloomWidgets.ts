import { GetButtonModifier } from "./bloomImages";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";

// Code related to the fourth option for types of origami panel content, HTML Widget
// An HTML widget must be a zip file (with extension wdgt) containing a self-contained web page rooted at
// index.htm{l}, which must occur in the root directory of the zip file.
// We might enhance it one day to support more of the Apple HTML5 widget file format,
// (https://support.apple.com/en-us/HT204433)
// such as using information in Info.plist to locate the root file.

// Initialization function, sets up all the editing functions we support for these elements.
export function SetupWidgetEditing(container: HTMLElement): void {
    const widgets = Array.from(
        container.getElementsByClassName("bloom-widgetContainer")
    );
    widgets.forEach(w => SetupWidget(w));
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
        // At this point, also following the approach of the other four data types,
        // the code that handles clicking on the change widget button is in C#,
        // which makes at least some sense since it is heavily involved with file
        // manipulations that javascript can't do. See EditingView.OnChangeWidget().
        w.classList.add("hoverUp");
        const wrapper = document.createElement("div");
        wrapper.innerHTML =
            '<button class="changeWidgetButton imageButton imageOverlayButton ' +
            buttonModifier +
            '" title="' +
            theOneLocalizationManager.getText(
                "EditTab.Widget.ChangeWidget",
                "Choose Widget"
            ) +
            '"></button>';
        const chooseButton = wrapper.firstChild as HTMLElement;
        w.appendChild(chooseButton);
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
