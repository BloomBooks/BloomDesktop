import * as React from "react";
import * as ReactDOM from "react-dom";
import { BloomMessageBox, showBloomMessageBox } from "./BloomMessageBox";
import { getEditTabBundleExports } from "../bookEdit/js/bloomFrames";
import theOneLocalizationManager from "../lib/localizationManager/localizationManager";

// This class contains static methods that simplify using the BloomMessageBox component, especially from
// a non-React environment.
export default class BloomMessageBoxSupport {
    // This method assumes we just have a message (that needs localizing), an "OK" button,
    // and an optional Help link.
    // If defined, helpButtonFileId, creates a single "Learn More" button on the left side.
    // This is intended to look for a localizable file whose English source resides at
    // "BloomBrowserUI/help/{helpButtonFileId}-en.md".
    public static CreateAndShowSimpleMessageBox(
        l10nKey: string,
        englishText: string,
        l10nComment: string,
        helpButtonFileId?: string,
    ) {
        theOneLocalizationManager
            .asyncGetText(l10nKey, englishText, l10nComment)
            .done((localizedMessage) => {
                const container =
                    getEditTabBundleExports().getModalDialogContainer();
                if (!container) {
                    // Fallback to alert; unlikely to happen.
                    alert(localizedMessage);
                    return;
                }
                theOneLocalizationManager
                    .asyncGetText("Common.OK", "OK", "")
                    .done((okText) => {
                        ReactDOM.render(
                            React.createElement(BloomMessageBox, {
                                messageHtml: localizedMessage,
                                icon: "warning",
                                rightButtonDefinitions: [
                                    {
                                        text: okText,
                                        id: "OKButton",
                                        default: true,
                                    },
                                ],
                                helpButtonFileId,
                                dialogEnvironment: {
                                    dialogFrameProvidedExternally: false,
                                    initiallyOpen: false,
                                },
                            }),
                            container,
                        );
                        showBloomMessageBox();
                    });
            });
    }
}
