import * as React from "react";
import * as ReactDOM from "react-dom";
//import { H1, Div, IUILanguageAwareProps, Label } from "../../../react_components/l10n";
//import { RadioGroup, Radio } from "../../../react_components/radio";
//import axios from "axios";
import { ToolBox, ITool } from "../toolbox";
import { getPageFrameExports } from "../../js/bloomFrames";
import "./imageDescription.less";
import { fireCSharpEditEvent } from "../../js/bloomEditing";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";

// This react class implements the UI for image description toolbox.
// (The ImageDescriptionAdapter class below implements the interface required for interaction with
// the toolbox.)
// Note: this file is included in toolboxBundle.js because webpack.config says to include all
// tsx files in bookEdit/toolbox.
// The toolbox is included in the list of tools because of the one line of immediately-executed code
// which passes an instance of ImageDescriptionTool to ToolBox.registerTool().
export class ImageDescriptionToolControls extends React.Component {

    // There is deliberately no content yet. That will follow.
    public render() {
        return (
            <div className={"imageDescriptionTool"}>
            </div>
        );
    }

    public static setup(root): ImageDescriptionToolControls {
        return ReactDOM.render(
            <ImageDescriptionToolControls />,
            root
        );
    }
}

export class ImageDescriptionAdapter extends ToolboxToolReactAdaptor {
    reactControls: ImageDescriptionToolControls;

    public makeRootElement(): HTMLDivElement {
        return super.adaptReactElement(
            <ImageDescriptionToolControls
                ref={renderedElement =>
                    (this.reactControls = renderedElement)
                }
            />
        );
    }
    showTool() {
        this.updateMarkup();
    }
    hideTool() {
        ToolBox.getPage().classList.remove("bloom-showImageDescriptions");
    }

    id(): string {
        return "imageDescription";
    }

    // Most if not all of this doesn't need doing every time text is edited on the page.
    // But it's the only way currently to get it called at some critical moments like
    // when we switch pages or add a new picture with origami.
    updateMarkup() {
        var page = ToolBox.getPage();
        // turn on special layout to make image descriptions visible (might already be on)
        page.classList.add("bloom-showImageDescriptions");
        // Make sure every image container has a child bloom-translationGroup to hold the image description.
        var imageContainers = page.getElementsByClassName("bloom-imageContainer");
        var addedTranslationGroup = false;
        for (var i = 0; i < imageContainers.length; i++) {
            const container = imageContainers[i];
            var imageDescriptions = container.getElementsByClassName("bloom-imageDescription");
            if (imageDescriptions.length === 0) {
                // from somewhere else I copied this as a typical default set of classes for a translation group,
                // except for the extra bloom-imageDescription. This distinguishes it from other TGs (such as in
                // textOverPicture) which might be nested in image containers.
                const newTg = getPageFrameExports()
                    .makeElement("<div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement normal-style'></div>").get(0);
                container.appendChild(newTg);
                addedTranslationGroup = true;
            }
        }
        if (addedTranslationGroup) {
            // This inserts all the right bloom-editable divs in whatever languages are needed.
            fireCSharpEditEvent("saveChangesAndRethinkPageEvent", "");
            // This return is currently redundant but it emphasizes that you can't count on anything
            // more happening in this branch.The page will unload somewhere in the
            // course of saveChangesAndRethinkPageEvent. Then a new page will load and updateMarkup()
            // will be called again...this time every image container should have a translation group.
            return;
        }
    }
}
