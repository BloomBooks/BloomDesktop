import * as React from "react";
import * as ReactDOM from "react-dom";
import { BloomApi } from "../../../utils/bloomApi";
import { ToolBox, ITool } from "../toolbox";
import { getPageFrameExports } from "../../js/bloomFrames";
import "./imageDescription.less";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import { Label } from "../../../react_components/l10n";
import { Checkbox } from "../../../react_components/checkbox";
import Link from "../../../react_components/link";
import HelpLink from "../../../react_components/helpLink";
import {
    RequiresBloomEnterpriseWrapper,
    checkIfEnterpriseAvailable
} from "../../../react_components/requiresBloomEnterprise";

interface IImageDescriptionState {
    enabled: boolean;
    checkBoxes: Array<boolean>;
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
        checkBoxes: []
    };

    private static i18ids = [
        "ContextKey",
        "ConsiderAudience",
        "BeConcise",
        "BeObjective",
        "GeneralSpecific"
    ];
    private static defaultText = [
        "Context is Key",
        "Consider your Audience",
        "Be Concise",
        "Be Objective",
        "General to Specific"
    ];

    private activeEditable: Element | null;

    private createCheckboxes() {
        const checkBoxes: JSX.Element[] = [];
        for (let i = 0; i < ImageDescriptionToolControls.i18ids.length; i++) {
            const index = i; // in case 'i' changing affects earlier checkboxes
            checkBoxes.push(
                <Checkbox
                    key={i}
                    l10nKey={
                        "EditTab.Toolbox.ImageDescriptionTool." +
                        ImageDescriptionToolControls.i18ids[i]
                    }
                    className="imageDescriptionCheck"
                    name=""
                    checked={this.state.checkBoxes[index]}
                    onCheckChanged={checked =>
                        this.onCheckChanged(checked, index)
                    }
                >
                    {ImageDescriptionToolControls.defaultText[index]}
                </Checkbox>
            );
        }
        return checkBoxes;
    }

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
                    <div className="imageDescriptionToolInternalWrapper">
                        <div className="imgDescLabelBlock">
                            <Label l10nKey="EditTab.Toolbox.ImageDescriptionTool.LearnToMake">
                                Learn to make effective image descriptions:
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
                        <div className="wrapPlayVideo disabled invisible">
                            <img id="playBloomTrainingVideo" src="play.svg" />
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
                        <div className="imgDescLabelBlock">
                            <Label l10nKey="EditTab.Toolbox.ImageDescriptionTool.CheckDescription">
                                Check your image description against each of
                                these reminders:
                            </Label>
                        </div>
                        {this.createCheckboxes()}
                    </div>
                    <div className="helpLinkWrapper imgDescLabelBlock">
                        <HelpLink
                            helpId="Tasks/Edit_tasks/Image_Description_Tool/Image_Description_Tool_overview.htm"
                            l10nKey="Common.Help"
                        >
                            Help
                        </HelpLink>
                    </div>
                </div>
            </RequiresBloomEnterpriseWrapper>
        );
    }

    private onCheckChanged(checked: boolean, index: number) {
        this.setState((prevState, props) => {
            const newCheckedBoxes = prevState.checkBoxes.slice(0); // shallow copy so we don't modify original
            newCheckedBoxes[index] = checked;
            return { checkBoxes: newCheckedBoxes };
        });
        if (this.activeEditable) {
            let checkListAttr = (
                this.activeEditable.getAttribute("data-descriptionCheckList") ||
                ""
            )
                .replace(ImageDescriptionToolControls.i18ids[index], "")
                .replace("  ", " ")
                .trim();

            if (checked) {
                checkListAttr = (
                    checkListAttr +
                    " " +
                    ImageDescriptionToolControls.i18ids[index]
                ).trim();
            }
            this.activeEditable.setAttribute(
                "data-descriptionCheckList",
                checkListAttr
            );
        }
    }

    public static setup(root): ImageDescriptionToolControls {
        return ReactDOM.render(
            <ImageDescriptionToolControls />,
            root
        ) as ImageDescriptionToolControls;
    }

    public selectImageDescription(description: Element): void {
        const activeEditableList = description.getElementsByClassName(
            "bloom-content1"
        );
        if (activeEditableList.length === 0) {
            // pathological
            this.activeEditable = null;
            this.setState({ enabled: false, checkBoxes: [] });
            return;
        }
        this.activeEditable = activeEditableList[0];
        const checkedList =
            this.activeEditable.getAttribute("data-descriptionCheckList") || "";
        const newCheckStates: boolean[] = [];
        for (let i = 0; i < ImageDescriptionToolControls.i18ids.length; i++) {
            newCheckStates.push(
                checkedList.indexOf(ImageDescriptionToolControls.i18ids[i]) >= 0
            );
        }
        this.setState({ enabled: true, checkBoxes: newCheckStates });
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
        // We switched to a page that has at least one image. Make the first one active
        // (as far as the check boxes are concerned).
        const imageDescriptions = imageContainers[0].getElementsByClassName(
            "bloom-imageDescription"
        );
        if (imageDescriptions.length === 0) {
            // other code will add an imageDescription, and we will be called again
            this.setDisabledState();
            return;
        }
        this.selectImageDescription(imageDescriptions[0]);
    }

    private setDisabledState() {
        this.activeEditable = null;
        this.setState({ enabled: false, checkBoxes: [] });
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
                e.currentTarget as Element
            );
        }
    };

    // Make sure the page has the elements used to store image descriptions,
    // not on every edit, but whenever a new page is displayed.
    public newPageReady() {
        checkIfEnterpriseAvailable().then(enabled => {
            if (enabled && this.reactControls) {
                this.reactControls.setStateForNewPage();
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
                let addedTranslationGroup = false;
                for (let i = 0; i < imageContainers.length; i++) {
                    const container = imageContainers[i];
                    const imageDescriptions = container.getElementsByClassName(
                        "bloom-imageDescription"
                    );

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
                    if (imageDescriptions.length === 0) {
                        // from somewhere else I copied this as a typical default set of classes for a translation group,
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
                        const newTg = getPageFrameExports()
                            .makeElement(
                                "<div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement" +
                                    " ImageDescriptionEdit-style'></div>"
                            )
                            .get(0);
                        container.appendChild(newTg);
                        addedTranslationGroup = true;
                    }
                }
                if (addedTranslationGroup) {
                    // This inserts all the right bloom-editable divs in whatever languages are needed.
                    BloomApi.postThatMightNavigate(
                        "common/saveChangesAndRethinkPageEvent"
                    );
                    // This return is currently redundant but it emphasizes that you can't count on anything
                    // more happening in this branch.The page will unload somewhere in the
                    // course of saveChangesAndRethinkPageEvent. Then a new page will load and updateMarkup()
                    // will be called again...this time every image container should have a translation group.
                    return;
                }
            }
        });
    }
}
