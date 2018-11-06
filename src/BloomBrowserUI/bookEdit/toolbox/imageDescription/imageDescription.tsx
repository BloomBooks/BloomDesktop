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
    enterpriseFeaturesEnabled
} from "../../../react_components/requiresBloomEnterprise";
import { number } from "prop-types";
import { containerCSS } from "react-select/lib/components/containers";

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
            <RequiresBloomEnterpriseWrapper>
                <div
                    className={
                        "imageDescriptionTool" +
                        (this.state.enabled ? "" : " disabled")
                    }
                >
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
                            <img id="playBloomTrainingVideo" src="play.svg" />
                            <Link
                                id="bloomImageDescritionTraining"
                                className="disabled"
                                href=""
                                l10nKey="EditTab.Toolbox.ImageDescriptionTool.BloomTrainingVideo"
                                l10nComment="Link that launces the video"
                            >
                                Bloom training video
                            </Link>
                        </div>
                    </div>
                    <div className="imgDescLabelBlock">
                        <Label l10nKey="EditTab.Toolbox.ImageDescriptionTool.WriteYours">
                            Write your image description on the left, in the box
                            next to the picture.
                        </Label>
                    </div>
                    <div className="imgDescLabelBlock">
                        <Label l10nKey="EditTab.Toolbox.ImageDescriptionTool.CheckDescription">
                            Check your image description against each of these
                            reminders:
                        </Label>
                    </div>
                    {this.createCheckboxes()}
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

export class DraggablePositioningInfo {
    // The original position info before shrinking to create space for Image Description
    public left: string;
    public top: string;
    public width: string;
    public height: string;
    public fontSize: string;
    public transform: string;

    // The position info after applying shrinking. We save it to determine if the user has moved the elements or not during the shrunken stage.
    // If they didn't move anything, I prefer to re-use the pre-existing values as it is safer and more exact than attempting to re-calculate the original
    public lastLeft: string;
    public lastTop: string;
    public lastWidth: string;
    public lastHeight: string;
    public lastFontSize: string;
    public lastTransform: string;

    // If the user did move something, then we need this info to calculate the reverse transformation
    public lastImageClientWidth: number;
    public lastImageClientHeight: number;
}

export class ImageDescriptionAdapter extends ToolboxToolReactAdaptor {
    private reactControls: ImageDescriptionToolControls | null;
    public static kToolID = "imageDescription";
    private isActive: boolean = false;
    private originalDraggablePositions: DraggablePositioningInfo[] = [];
    private leftOffsetOfImageFromContainer: number = 0;
    private topOffsetOfImageFromContainer: number = 20; // There is a 20 pixel bar that appears at the top

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

            if (this.isActive) {
                this.unshrinkDraggablesOnPageForImageDescription();
            }
        }

        this.isActive = false;
    }

    public isExperimental(): boolean {
        return true;
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

    public getDraggablesOnPage(): HTMLCollectionOf<Element> {
        const page = ToolBox.getPage();
        if (!page) {
            return null;
        }

        // Re-adjust any user-draggable items to their new position given that the margins
        // Note: it might've been nice to set up the HTML structure such that the img and bloom-textOverPicture had an ancestor that was exactly the size of the img
        // but then it's a huge mess for how to make it work on existing books that already have this HTML structure set.
        const draggablesThatNeedToMove = page.getElementsByClassName(
            "ui-draggable"
        );
        // Alternatively, use class "bloom-textOverPicture" if there are any .ui-draggables that aren't supposed to stay at the same relative position over the image

        return draggablesThatNeedToMove;
    }

    private unshrinkDraggablesOnPageForImageDescription() {
        const draggableList = this.getDraggablesOnPage();

        if (!draggableList) {
            return;
        }

        for (let i = 0; i < draggableList.length; ++i) {
            const draggableElement: HTMLElement = draggableList[
                i
            ] as HTMLElement;

            let originalPos: DraggablePositioningInfo;
            if (i < this.originalDraggablePositions.length) {
                originalPos = this.originalDraggablePositions[i];
            }

            this.unshrinkDraggableForImageDescription(
                draggableElement,
                originalPos
            );
        }
    }

    // Precondition: the image element should already be re-sized by the time this function is called.
    public unshrinkDraggableForImageDescription(
        draggableElement: HTMLElement,
        originalPos: DraggablePositioningInfo
    ) {
        draggableElement.classList.remove("imageDescriptionShrink");
        const shrunkDescendants = draggableElement.getElementsByClassName(
            "imageDescriptionShrink"
        );

        // Note: shrunkDescendants will shrink as you remove the class names, so can't use a for loop assuming the length is static
        let indexOfFirstUnprocessedElement = 0;
        while (shrunkDescendants.length > indexOfFirstUnprocessedElement) {
            const oldLength = shrunkDescendants.length;
            shrunkDescendants[indexOfFirstUnprocessedElement].classList.remove(
                "imageDescriptionShrink"
            );

            if (oldLength == shrunkDescendants.length) {
                // This would be unexpected, but deal with it so that we don't risk an infinite loop
                ++indexOfFirstUnprocessedElement;
            }
        }

        if (originalPos) {
            // Determine the true width and height of the image.
            // (because clientWidth tells you the width and height allocated to the element, but the image probably will not consume all of it)
            const imageElement = this.GetMainImageFromImageContainer(
                draggableElement.parentElement
            );

            const oldTrueDimensions = this.getTrueImageDimensionsInBoundingBox(
                imageElement.naturalWidth,
                imageElement.naturalHeight,
                originalPos.lastImageClientWidth,
                originalPos.lastImageClientHeight
            );
            const newTrueDimensions = this.getTrueImageDimensionsInBoundingBox(
                imageElement.naturalWidth,
                imageElement.naturalHeight,
                imageElement.clientWidth,
                imageElement.clientHeight
            );
            const scalingFactor: number =
                newTrueDimensions.trueWidth / oldTrueDimensions.trueWidth;

            // For each attribute, check if it is safe to restore from saved settings, or if we need re-calculate the position
            if (draggableElement.style.left == originalPos.lastLeft) {
                draggableElement.style.left = originalPos.left;
            } else {
                draggableElement.style.left = this.scalePosition(
                    draggableElement.style.left,
                    draggableElement.clientWidth,
                    oldTrueDimensions.trueWidth,
                    newTrueDimensions.trueWidth,
                    draggableElement.parentElement.clientWidth,
                    originalPos.lastImageClientWidth,
                    draggableElement.parentElement.clientWidth,
                    this.leftOffsetOfImageFromContainer,
                    0,
                    scalingFactor
                );
            }

            if (draggableElement.style.top == originalPos.lastTop) {
                draggableElement.style.top = originalPos.top;
            } else {
                draggableElement.style.top = this.scalePosition(
                    draggableElement.style.top,
                    draggableElement.clientHeight,
                    oldTrueDimensions.trueHeight,
                    newTrueDimensions.trueHeight,
                    draggableElement.parentElement.clientHeight,
                    originalPos.lastImageClientHeight,
                    draggableElement.parentElement.clientHeight,
                    this.topOffsetOfImageFromContainer,
                    0,
                    scalingFactor
                );
            }

            if (draggableElement.style.width == originalPos.lastWidth) {
                draggableElement.style.width = originalPos.width;
            } else {
                this.scaleWidth(draggableElement, scalingFactor);
            }

            if (draggableElement.style.height == originalPos.lastHeight) {
                draggableElement.style.height = originalPos.height;
            } else {
                this.scaleHeight(draggableElement, scalingFactor);
            }

            if (draggableElement.style.fontSize == originalPos.lastFontSize) {
                draggableElement.style.fontSize = originalPos.fontSize;
            } else {
                this.scaleFont(draggableElement, scalingFactor);
            }

            this.upscaleChildren(draggableElement, 1.0);
        } else {
            // Well, this is an unexpected state... we are really expecting to restore it from the orignalPos
            // Just leave it where it is now for simplicity, but utry to restore it ot the default size

            draggableElement.style.fontSize = "1em";
        }
    }

    private scaleFont(element: HTMLElement, scalingFactor: number) {
        let oldFontSizeStr: string;
        if (element.style.fontSize) {
            oldFontSizeStr = element.style.fontSize;
        } else {
            const computedStyle = window.getComputedStyle(element);
            oldFontSizeStr = computedStyle.fontSize;
        }

        const oldParsedFontSize = ImageDescriptionAdapter.parseNumberAndUnit(
            oldFontSizeStr
        );
        const newFontSizeValue = oldParsedFontSize.num * scalingFactor;
        const newFontSizeStr =
            newFontSizeValue.toString() + oldParsedFontSize.unit;
        element.style.fontSize = newFontSizeStr;
    }

    private scaleWidth(element: HTMLElement, scalingFactor: number) {
        let oldWidthStr: string;
        if (element.style.width) {
            oldWidthStr = element.style.width;
        } else {
            oldWidthStr = window.getComputedStyle(element).width;
        }
        const oldParsedWidthSize = ImageDescriptionAdapter.parseNumberAndUnit(
            oldWidthStr
        );
        const newWidthValue = oldParsedWidthSize.num * scalingFactor;
        const newWidthStr = newWidthValue.toString() + oldParsedWidthSize.unit;
        element.style.width = newWidthStr;
    }

    private scaleHeight(element: HTMLElement, scalingFactor: number) {
        let oldHeightStr: string;
        if (element.style.height) {
            oldHeightStr = element.style.height;
        } else {
            oldHeightStr = window.getComputedStyle(element).height;
        }
        const oldParsedHeightSize = ImageDescriptionAdapter.parseNumberAndUnit(
            oldHeightStr
        );
        const newHeightValue = oldParsedHeightSize.num * scalingFactor;
        const newHeightStr =
            newHeightValue.toString() + oldParsedHeightSize.unit;
        element.style.height = newHeightStr;
    }

    // Make sure the page has the elements used to store image descriptions,
    // not on every edit, but whenever a new page is displayed.
    public newPageReady() {
        enterpriseFeaturesEnabled().then(enabled => {
            if (enabled && this.reactControls) {
                this.reactControls.setStateForNewPage();
                const page = ToolBox.getPage();
                if (!page) {
                    return;
                }
                const imageContainers = page.getElementsByClassName(
                    "bloom-imageContainer"
                );

                // turn on special layout to make image descriptions visible (might already be on)
                if (!page.classList.contains("bloom-showImageDescriptions")) {
                    page.classList.add("bloom-showImageDescriptions");

                    this.shrinkDraggablesOnPageForImageDescription();
                    this.isActive = true;
                }

                // Make sure every image container has a child bloom-translationGroup to hold the image description.
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

    protected shrinkDraggablesOnPageForImageDescription() {
        // Enhance: The ::after text (which display the language of the textbox needs to be scaled down.
        const draggablesThatNeedToMove = this.getDraggablesOnPage();

        if (draggablesThatNeedToMove == null) {
            return;
        }

        this.originalDraggablePositions = [];
        for (let i = 0; i < draggablesThatNeedToMove.length; ++i) {
            const draggableElement = draggablesThatNeedToMove[i] as HTMLElement;

            if (draggableElement) {
                // Save the information that we need from before we mutate the element
                const originalPositionInfo: DraggablePositioningInfo = new DraggablePositioningInfo();
                this.originalDraggablePositions.push(originalPositionInfo);
                if (draggableElement.style) {
                    originalPositionInfo.left = draggableElement.style.left;
                    originalPositionInfo.top = draggableElement.style.top;
                    originalPositionInfo.width = draggableElement.style.width;
                    originalPositionInfo.height = draggableElement.style.height;
                    originalPositionInfo.fontSize =
                        draggableElement.style.fontSize;
                    originalPositionInfo.transform =
                        draggableElement.style.transform;
                }

                // Do the real work of shrinking the element
                const imageElement = this.GetMainImageFromImageContainer(
                    draggableElement.parentElement
                );
                this.shrinkDraggableForImageDescription(
                    draggableElement,
                    imageElement
                );

                // Save the information that we need from after mutating the element
                if (draggableElement.style) {
                    originalPositionInfo.lastLeft = draggableElement.style.left;
                    originalPositionInfo.lastTop = draggableElement.style.top;
                    originalPositionInfo.lastWidth =
                        draggableElement.style.width;
                    originalPositionInfo.lastHeight =
                        draggableElement.style.height;
                    originalPositionInfo.lastFontSize =
                        draggableElement.style.fontSize;
                    originalPositionInfo.lastTransform =
                        draggableElement.style.transform;
                }

                originalPositionInfo.lastImageClientWidth =
                    imageElement.clientWidth;
                originalPositionInfo.lastImageClientHeight =
                    imageElement.clientHeight;
            }
        }
    }

    private GetMainImageFromImageContainer(
        parentElement: Element
    ): HTMLImageElement {
        if (!parentElement) {
            return null;
        }

        let imageElement: HTMLImageElement;

        const imageTags = parentElement.getElementsByTagName("img");

        if (imageTags && imageTags.length > 0) {
            imageElement = imageTags[0];
            let maxArea: number =
                imageElement.clientWidth * imageElement.clientHeight;
            for (let i = 1; i < imageTags.length; ++i) {
                const area: number =
                    imageTags[i].clientWidth * imageTags[i].clientHeight;
                if (area > maxArea) {
                    imageElement = imageTags[i];
                    maxArea = area;
                }
            }
        }

        return imageElement;
    }

    public shrinkDraggableForImageDescription(
        element: HTMLElement,
        imageElement: HTMLImageElement
    ): void {
        if (!element || !element.parentElement || !imageElement) {
            return;
        }

        const parentElement = element.parentElement;

        // Determine the true width and height of the image.
        // (because clientWidth tells you the width and height allocated to the element, but the image probably will not consume all of it)
        const oldTrueDimensions = this.getTrueImageDimensionsInBoundingBox(
            imageElement.naturalWidth,
            imageElement.naturalHeight,
            parentElement.clientWidth,
            parentElement.clientHeight
        );
        const newTrueDimensions = this.getTrueImageDimensionsInBoundingBox(
            imageElement.naturalWidth,
            imageElement.naturalHeight,
            imageElement.clientWidth,
            imageElement.clientHeight
        );

        const scalingFactor: number =
            newTrueDimensions.trueWidth / oldTrueDimensions.trueWidth; // scalingX and scalingY are equivalent.

        // Calculate x position
        if (element.style.left) {
            const newPosition: string = this.scalePosition(
                element.style.left,
                element.clientWidth,
                oldTrueDimensions.trueWidth,
                newTrueDimensions.trueWidth,
                parentElement.clientWidth,
                parentElement.clientWidth,
                imageElement.clientWidth,
                0,
                this.leftOffsetOfImageFromContainer,
                scalingFactor
            );
            if (newPosition) {
                element.style.left = newPosition;
            }
        }

        // Calculate y position
        if (element.style.top) {
            const newPosition: string = this.scalePosition(
                element.style.top,
                element.clientHeight,
                oldTrueDimensions.trueHeight,
                newTrueDimensions.trueHeight,
                parentElement.clientHeight,
                parentElement.clientHeight,
                imageElement.clientHeight,
                0,
                this.topOffsetOfImageFromContainer,
                scalingFactor
            );

            if (newPosition) {
                element.style.top = newPosition;
            }
        }

        this.applyScaling(element, scalingFactor);
    }

    // Given the old and new image dimension information, calculates where the text box element should be moved to (for a single dimension)
    protected scalePosition(
        positionString: string, // The CSS string specifying the position of the near edge in the current dimension
        elementLength: number, // The length of the text box in the current dimension
        oldImageLength: number, // The true length of the image in the current dimension when sized to fit into the old bounding box
        newImageLength: number, // The true length of the image in the current dimension  when sized to fit into the new bounding box
        physicalContainerLength: number, // The physical container is what actually contains the element.
        oldVisualContainerLength: number, // The virtual container is what visually looks like should contain the element.
        newVisualContainerLength: number, // The virtual container is what visually looks like should contain the element.
        oldOffsetBetweenPhysicalAndVisual: number,
        newOffsetBetweenPhysicalAndVisual: number,
        scalingFactor: number
    ): string {
        if (positionString) {
            const { num, unit } = ImageDescriptionAdapter.parseNumberAndUnit(
                positionString
            );

            let oldTextBoxNearEdgePosition: number; // i.e, explicitly in pixels
            if (unit == "%") {
                oldTextBoxNearEdgePosition =
                    (physicalContainerLength * num) / 100;
            } else if (unit == "px" || unit == "") {
                oldTextBoxNearEdgePosition = num;
            }

            if (oldTextBoxNearEdgePosition != undefined) {
                oldTextBoxNearEdgePosition -= oldOffsetBetweenPhysicalAndVisual;

                const oldCenter = oldVisualContainerLength / 2;

                // Calculate the left (or top) boundary of the image. It is not necessarily at the physical boundary (e.g. 0 or 20), if the image doesn't have the perfect aspect ratio
                const oldImageNearEdgePosition = oldCenter - oldImageLength / 2;

                // Calculate the proportion at which the text box started in the old frame of reference.
                const textBoxRelativePositionProportion =
                    (oldTextBoxNearEdgePosition - oldImageNearEdgePosition) /
                    oldImageLength;

                const newCenter: number =
                    newVisualContainerLength / 2 +
                    newOffsetBetweenPhysicalAndVisual;

                const newImageNearEdgePosition: number =
                    newCenter - newImageLength / 2;
                const newTextBoxNearEdgePosition: number =
                    newImageNearEdgePosition +
                    textBoxRelativePositionProportion * newImageLength;

                // This code is needed if you use transform: scale(). Scaling down will cause the top-left corner to move so we need to adjust pre-maturely for that.
                // newTextBoxNearEdgePosition -= (1 - scalingFactor) * (elementLength / 2);

                let newPosition: number;
                if (unit == "%") {
                    newPosition =
                        (newTextBoxNearEdgePosition / physicalContainerLength) *
                        100;
                } else if (unit == "px" || unit == "") {
                    newPosition = newTextBoxNearEdgePosition;
                }

                if (newPosition != undefined) {
                    return newPosition.toString() + unit;
                }
            }
        }

        return null;
    }

    private applyScaling(element: HTMLElement, scalingFactor: number) {
        // Setting the scaling factor is good because you can modify a lot of attributes all in one go as well as those in the descendants
        //   The downside is it makes positioning the top-left corner much less intuitive (because the scaling is applied from the center of the box)
        //   But the bigger probably is that having scaling applied causes disastrously confusing results for the user when trying to move the elemnt.
        //
        // So, instead, we need to specify the width, height, font-size, etc. as well as descendants.
        //   The benefit of not scaling is the calculation for the top-left corner position is simpler and more intuitive.
        element.classList.add("imageDescriptionShrink");

        this.scaleWidth(element, scalingFactor);
        this.scaleHeight(element, scalingFactor);
        this.scaleFont(element, scalingFactor);

        this.downscaleChildren(element, scalingFactor);
    }

    // that is, to un-shrink them
    private upscaleChildren(element: HTMLElement, scalingFactor: number) {
        this.scaleChildren(element, scalingFactor, false);
    }
    // that is, to shrink them
    private downscaleChildren(element: HTMLElement, scalingFactor: number) {
        this.scaleChildren(element, scalingFactor, true);
    }
    private scaleChildren(
        element: HTMLElement,
        scalingFactor: number,
        shouldApplyShrink: boolean
    ) {
        // Note: in the future would also need to update anything else that can possibly show up here, which is admittedly a pain.
        const divDescendants = element.getElementsByTagName("div");
        for (let i = 0; i < divDescendants.length; ++i) {
            if (!divDescendants[i]) {
                continue;
            }

            if (divDescendants[i].id == "formatButton") {
                divDescendants[i].style.transform =
                    "scale(" + scalingFactor + ", " + scalingFactor + ")";

                if (shouldApplyShrink) {
                    divDescendants[i].classList.add("imageDescriptionShrink");
                }
            }

            if (shouldApplyShrink) {
                if (
                    divDescendants[i].classList.contains(
                        "bloom-translationGroup"
                    )
                ) {
                    divDescendants[i].classList.add("imageDescriptionShrink");
                } else if (
                    divDescendants[i].classList.contains("bloom-editable")
                ) {
                    divDescendants[i].classList.add("imageDescriptionShrink");

                    for (
                        let j = 0;
                        j < divDescendants[i].children.length;
                        ++j
                    ) {
                        const editableDescendant =
                            divDescendants[i].children[j];
                        editableDescendant.classList.add(
                            "imageDescriptionShrink"
                        );
                    }
                }
            }
        }
    }

    public static parseNumberAndUnit(text: string) {
        // Assumption: Hope we don't get any scientific notation...
        let index = 0;
        while (index < text.length) {
            const c: string = text.charAt(index);

            // Check if each character is valid or invalid as a number
            const isSign = (c == "+" || c == "-") && index == 0;
            const isNumeric: boolean = "0" <= c && c <= "9";
            const isDecimalPoint: boolean = c == ".";

            // Enhance: maybe you should get mad about multiple decimal points.
            if (!isSign && !isNumeric && !isDecimalPoint) {
                break;
            } else {
                ++index;
            }
        }

        const indexOfFirstNonNumericCharacter = index;
        const num: number = Number(
            text.substring(0, indexOfFirstNonNumericCharacter)
        );

        const unit = text.substring(indexOfFirstNonNumericCharacter).trim();

        return { num: num, unit: unit };
    }

    // Note: Will up-scale the image to fit if the image is smaller than the bounding box
    public getTrueImageDimensionsInBoundingBox(
        naturalWidth: number,
        naturalHeight: number,
        boundingWidth: number,
        boundingHeight: number
    ) {
        const resizeScalingX = boundingWidth / naturalWidth;
        const resizeScalingY = boundingHeight / naturalHeight;

        const resizeScaling =
            resizeScalingX < resizeScalingY ? resizeScalingX : resizeScalingY;

        const newTrueWidth = naturalWidth * resizeScaling;
        const newTrueHeight = naturalHeight * resizeScaling;

        return { trueWidth: newTrueWidth, trueHeight: newTrueHeight };
    }
}
