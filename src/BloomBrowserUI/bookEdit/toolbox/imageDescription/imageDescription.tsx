import * as React from "react";
import * as ReactDOM from "react-dom";
import { BloomApi } from "../../../utils/bloomApi";
import { ToolBox, ITool } from "../toolbox";
import { getPageFrameExports } from "../../js/bloomFrames";
import "./imageDescription.less";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import { Label } from "../../../react_components/l10nComponents";
import { Checkbox } from "../../../react_components/checkbox";
import Link from "../../../react_components/link";
import { ToolBottomHelpLink } from "../../../react_components/helpLink";
import {
    RequiresBloomEnterpriseWrapper,
    checkIfEnterpriseAvailable
} from "../../../react_components/requiresBloomEnterprise";

interface IImageDescriptionState {
    enabled: boolean;
    descriptionNotNeeded: boolean;
}
interface IProps {}
// This react class implements the UI for image description toolbox.
// (The ImageDescriptionAdapter class below implements the interface required for interaction with
// the toolbox.)
// Note: this file is included in toolboxBundle.js because webpack.config says to include all
// tsx files in bookEdit/toolbox.
// The toolbox is included in the list of tools because of the one line of immediately-executed code
// which passes an instance of ImageDescriptionTool to ToolBox.registerTool().
export class ImageDescriptionToolControls extends React.Component<
    IProps,
    IImageDescriptionState
> {
    public readonly state: IImageDescriptionState = {
        enabled: true,
        descriptionNotNeeded: false
    };

    private activeEditable: Element | null;

    // Todo: when we have a training video for image description, set the relevant link href, remove
    // the 'disabled' class, and make the play button do something (just another way to go
    // to the link destination?)
    public render() {
        return (
            <RequiresBloomEnterpriseWrapper className="imageDescriptionToolOuterWrapper">
                <div
                    className={
                        "imageDescriptionTool" +
                        (this.state.enabled ? "" : " disabled")
                    }
                >
                    <div className="topGroup">
                        <div className="imgDescLabelBlock">
                            <Label l10nKey="EditTab.Toolbox.ImageDescriptionTool.KeepInMind">
                                Keep these things in mind:
                            </Label>
                            <ul>
                                <li>
                                    <Label l10nKey="EditTab.Toolbox.ImageDescriptionTool.ImportantToDescribe">
                                        Are there important{" "}
                                        <strong>actions</strong>,{" "}
                                        <strong>relationships</strong>,{" "}
                                        <strong>emotions</strong>, or things in
                                        the <strong>scene</strong> that add to
                                        the story but are not in the text?
                                    </Label>
                                    <ul>
                                        <li>
                                            <Label l10nKey="EditTab.Toolbox.ImageDescriptionTool.UseSimpleWords">
                                                Use words that are{" "}
                                                <strong>simple</strong> enough
                                                for the listener.
                                            </Label>
                                        </li>
                                        <li>
                                            <Label l10nKey="EditTab.Toolbox.ImageDescriptionTool.KeepItShort">
                                                Keep it <strong>short</strong>.
                                            </Label>
                                        </li>
                                    </ul>
                                </li>
                            </ul>
                        </div>
                        <div className="imgDescLabelBlock">
                            <Label l10nKey="EditTab.Toolbox.ImageDescriptionTool.CheckThisBox">
                                Otherwise, check this box:
                            </Label>
                            <Checkbox
                                key={0}
                                l10nKey={
                                    "EditTab.Toolbox.ImageDescriptionTool.ShouldNotDescribe"
                                }
                                className="imageDescriptionCheck"
                                name=""
                                checked={this.state.descriptionNotNeeded}
                                onCheckChanged={checked =>
                                    this.onCheckChanged(checked)
                                }
                            >
                                This image should not be described.
                            </Checkbox>
                        </div>
                        <div className="imgDescLabelBlock">
                            <Label l10nKey="EditTab.Toolbox.ImageDescriptionTool.MoreInformation">
                                For more information:
                            </Label>
                            <div className="indentPoet">
                                <Link
                                    id="poetDiagram"
                                    href="https://poet.diagramcenter.org"
                                    l10nKey="EditTab.Toolbox.ImageDescriptionTool.PoetDiagram"
                                    l10nComment="English text is the actual link. May not need translation?"
                                >
                                    poet.diagramcenter.org
                                </Link>
                            </div>
                            <div className="wrapPlayVideo disabled invisible">
                                <img
                                    id="playBloomTrainingVideo"
                                    src="play.svg"
                                />
                                <Link
                                    id="bloomImageDescriptionTraining"
                                    className="disabled"
                                    href=""
                                    l10nKey="EditTab.Toolbox.ImageDescriptionTool.BloomTrainingVideo"
                                    l10nComment="Link that launches the video"
                                >
                                    Bloom training video
                                </Link>
                            </div>
                        </div>
                    </div>
                    {/* the flex box will then push this to the bottom */}
                    <ToolBottomHelpLink helpId="Tasks/Edit_tasks/Image_Description_Tool/Image_Description_Tool_overview.htm" />
                </div>
            </RequiresBloomEnterpriseWrapper>
        );
    }

    private onCheckChanged(checked: boolean) {
        this.setState((prevState, props) => {
            return { descriptionNotNeeded: checked };
        });
        if (this.activeEditable) {
            if (checked) {
                this.activeEditable.setAttribute("aria-hidden", "true");
            } else {
                this.activeEditable.removeAttribute("aria-hidden");
            }
        }
    }

    public static setup(root): ImageDescriptionToolControls {
        return (ReactDOM.render(
            <ImageDescriptionToolControls />,
            root
        ) as unknown) as ImageDescriptionToolControls;
    }

    public selectImageDescription(imageContainer: Element | null): void {
        if (imageContainer == null) {
            // pathological
            this.activeEditable = null;
            this.setState({ enabled: false, descriptionNotNeeded: false });
            return;
        }
        this.activeEditable = imageContainer;
        const noDescriptionNeeded = this.activeEditable.getAttribute(
            "aria-hidden"
        );
        this.setState({
            enabled: true,
            descriptionNotNeeded: noDescriptionNeeded == "true"
        });
    }

    public setStateForNewPage(): void {
        const page = ToolboxToolReactAdaptor.getPage();
        if (!page) {
            this.setDisabledState();
            return;
        }
        // If we're still on the same page, it must be one without images.
        // We might also have switched TO one without images.
        const imageContainers = page.getElementsByClassName(
            "bloom-imageContainer"
        );
        if (imageContainers.length === 0) {
            // This is OK whether we switched from a page without images or just stayed on one.
            this.setDisabledState();
            return;
        }
        this.selectImageDescription(imageContainers[0]);
    }

    private setDisabledState() {
        this.activeEditable = null;
        this.setState({ enabled: false, descriptionNotNeeded: false });
    }
}

export class ImageDescriptionAdapter extends ToolboxToolReactAdaptor {
    private reactControls: ImageDescriptionToolControls | null;
    public static kToolID = "imageDescription";

    public makeRootElement(): HTMLDivElement {
        return super.adaptReactElement(
            <ImageDescriptionToolControls
                ref={renderedElement => (this.reactControls = renderedElement)}
            />
        );
    }

    public detachFromPage() {
        const page = ToolBox.getPage();
        if (page) {
            page.classList.remove("bloom-showImageDescriptions");
        }
    }

    public isExperimental(): boolean {
        return false;
    }
    public id(): string {
        return ImageDescriptionAdapter.kToolID;
    }

    // If we declare the function in this normal way and pass it to addEventListener,
    // we get the wrong 'this' and can't get at this.reactControls.
    // private descriptionGotFocus(e: Event) {
    //     this.reactControls.selectImageDescription(e.target as Element);
    // }
    // We use currentTarget (the thing the event was attached to) because we're looking for the
    // bloom-imageDescription (group), but the target (actually clicked) will be a bloom-editable or one of its children.
    private descriptionGotFocus = (e: Event) => {
        if (this.reactControls) {
            this.reactControls.selectImageDescription(
                (e.currentTarget as Element).parentElement
            );
        }
    };

    // Make sure the page has the elements used to store image descriptions,
    // not on every edit, but whenever a new page is displayed.
    public newPageReady() {
        checkIfEnterpriseAvailable().then(enabled => {
            const imageDescControls = this.reactControls;
            if (enabled && imageDescControls) {
                imageDescControls.setStateForNewPage();
                const page = ToolBox.getPage();
                if (!page) {
                    return;
                }
                // turn on special layout to make image descriptions visible (might already be on)
                page.classList.add("bloom-showImageDescriptions");
                // Make sure every image container has a child bloom-translationGroup to hold the image description.
                const imageContainers = page.getElementsByClassName(
                    "bloom-imageContainer"
                );

                for (let i = 0; i < imageContainers.length; i++) {
                    const container = imageContainers[i];
                    let imageDescriptions = container.getElementsByClassName(
                        "bloom-imageDescription"
                    );
                    if (imageDescriptions.length === 0) {
                        // Adds a new bloom-translationGroup
                        // Gets the information we need to fill out the interior bloom-editables of the newly added bloom-translation group.
                        // Preferable to only send a request for the info we need and not save and refresh the whole page.
                        //   (Allows us to avoid the synchronous reload of the page, makes the UI experience much snappier)
                        BloomApi.post(
                            "common/requestTranslationGroups",
                            result => {
                                // newPageReady() can be called twice, and both calls might occur before this async
                                // callback happens for either of them, so both may take this "no translation groups"
                                // branch and start to create them.  So check again before actually adding the new
                                // description elements.
                                // See https://issues.bloomlibrary.org/youtrack/issue/BL-6798 for some
                                // confusing behavior that can result without this check.
                                imageDescriptions = container.getElementsByClassName(
                                    "bloom-imageDescription"
                                );
                                if (result && imageDescriptions.length == 0) {
                                    this.appendTranslationGroup(
                                        result.data,
                                        container
                                    );
                                    // BL-6775 if we just added image description
                                    // translationGroups to a page that didn't have them before,
                                    // we need to reset our state.
                                    imageDescControls.setStateForNewPage();
                                    // BL-6798: we need to add focus listeners to these new description elements.
                                    imageDescriptions = container.getElementsByClassName(
                                        "bloom-imageDescription"
                                    );
                                    this.addFocusListeners(imageDescriptions);
                                }
                            }
                        );
                    } else {
                        this.addFocusListeners(imageDescriptions);
                    }
                }
            }
        });
    }

    private addFocusListeners(imageDescriptions: HTMLCollectionOf<Element>) {
        // Arrange to change which image the check boxes refer to when an image's description
        // gets focus. Note that we do not change this or disable them if something other
        // than an image description gets focus; this potentially allows the user to do things like
        // moving the focus and selecting a check box using the keyboard.
        for (let i = 0; i < imageDescriptions.length; i++) {
            imageDescriptions[i].removeEventListener(
                "focus",
                this.descriptionGotFocus
            ); // prevent duplicates
            // look for it in capture phase so we see child elements getting focus
            imageDescriptions[i].addEventListener(
                "focus",
                this.descriptionGotFocus,
                true
            );
        }
    }

    // Adds a new bloom-translationGroup
    // This function is meant to get called after we send a request to C# land to figure out what kind of bloom-editables/languages we need inside this translation group
    // The container must be inside the (editing) page iFrame (because this relies on getPageFromExports()
    private appendTranslationGroup(innerHtml, container: Element) {
        // Fill the interior of the new element with the HTML we get back from the API call.

        // from somewhere else I (John Thomson) copied this as a typical default set of classes for a translation group,
        // except for the extra bloom-imageDescription. This distinguishes it from other TGs (such as in
        // textOverPicture) which might be nested in image containers.
        // Note that, like normal-style, the class imageDescriptionEdit-style class is not defined
        // anywhere. Image descriptions will get the book's default font and Bloom's default text size
        // from other style sheets, unless the user edits the imageDescriptionEdit-style directly.
        // Using a unique name serves to prevent image description from using the possibly very large
        // text set for the main content (normal-style); this style will inherit the defaults independently.
        // Including 'edit' in the name of the style is intended to convey that this style is only
        // intended for use in editing; we will style it otherwise if we actually make it visible
        // in an epub.
        const newElementHtmlPrefix =
            "<div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement ImageDescriptionEdit-style'>";
        const newElementHtmlSuffix = "</div>";

        let newElementHtmlInterior: string = "";
        if (innerHtml) {
            newElementHtmlInterior = innerHtml;
        }
        const newElementHtml =
            newElementHtmlPrefix +
            newElementHtmlInterior +
            newElementHtmlSuffix;

        const newTg = getPageFrameExports()
            .makeElement(newElementHtml)
            .get(0);

        container.appendChild(newTg);

        // This is necessary for the data-language tooltip to appear, probably among other things.
        getPageFrameExports().SetupElements(container);

        $(newTg)
            .find(".bloom-editable")
            .each((index, newEditable) => {
                // Attaching CKEditor is necessary for range select formatting to work.
                getPageFrameExports().attachToCkEditor(newEditable);
            });
    }
}
