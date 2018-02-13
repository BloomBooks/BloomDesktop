import * as React from "react";
import * as ReactDOM from "react-dom";
import { H1, Div, IUILanguageAwareProps, Label } from "../../../react_components/l10n";
import { RadioGroup, Radio } from "../../../react_components/Radio";
import axios from "axios";
import { ToolBox, ITool } from "../toolbox";
import Slider from "rc-slider";
// These are for Pan and Zoom:
import { EditableDivUtils } from "../../js/editableDivUtils";
import { getPageFrameExports } from "../../js/bloomFrames";
import AudioRecording from "../talkingBook/audioRecording";
import { Checkbox } from "../../../react_components/Checkbox";

// The toolbox is included in the list of tools because of this line of code
// in tooboxBootstrap.ts:
// ToolBox.registerTool(new PanAndZoomTool());.
export class PanAndZoomTool implements ITool {
    rootControl: PanAndZoomControl;

    makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");
        root.setAttribute("class", "ui-panAndZoomBody");
        this.rootControl = ReactDOM.render(
            <PanAndZoomControl
                onPreviewClick={() => this.previewPanAndZoom()}
                onPanAndZoomChanged={(checked) => this.panAndZoomChanged(checked)}
            />,
            root
        );
        const initialState = this.getStateFromHtml();
        this.rootControl.setState(initialState);
        if (initialState.haveImageContainerButNoImage) {
            this.setupImageObserver();
        }
        return root as HTMLDivElement;
    }
    beginRestoreSettings(settings: string): JQueryPromise<void> {
        //Nothing to do, so return an already-resolved promise.
        var result = $.Deferred<void>();
        result.resolve();
        return result;
    }
    isAlwaysEnabled(): boolean {
        return false;
    }

    // required for ITool interface
    hasRestoredSettings: boolean;
    /* tslint:disable:no-empty */ // We need these to implement the interface, but don't need them to do anything.
    configureElements(container: HTMLElement) { }
    finishToolLocalization(pane: HTMLElement) { }
    /* tslint:enable:no-empty */

    updateMarkup() {
        // This isn't exactly updating the markup, but it needs to happen when we switch pages,
        // just like updating markup. Using this hook does mean it will (unnecessarily) happen
        // every time the user pauses typing while this tool is active. I don't much expect people
        // to be editing the book and configuring background music at the same time, so I'm not
        // too worried. If it becomes a performance problem, we could enhance ITool with a
        // function that is called just when the page switches.
        const newState = this.getStateFromHtml();
        if (newState.haveImageContainerButNoImage) {
            this.setupImageObserver();
        }
        this.rootControl.setState(newState);
        if (!newState.panAndZoomChecked || newState.haveImageContainerButNoImage) {
            return;
        }

        const page = this.getPage();
        // enhance: if more than one image...do what??
        const firstImage = this.getFirstImage();
        firstImage.classList.add("bloom-hideImageButtons");
        this.removeElt(page.getElementById("animationStart"));
        this.removeElt(page.getElementById("animationEnd"));
        const scale = EditableDivUtils.getPageScale();
        const imageHeight = this.getHeight(firstImage);
        const imageWidth = this.getWidth(firstImage);
        let usingDefaults = false;
        const makeResizeRect = (handleLabel: string, id: string, left: number, top: number, width: number,
            height: number, initAttr: string): JQuery => {
            const savedState = firstImage.getAttribute(initAttr);
            if (savedState) {
                try {
                    const parts = savedState.split(" ");
                    // NB This is safe because Javascript parseFloat is not culture-specific.
                    left = parseFloat(parts[0]) * imageWidth;
                    top = parseFloat(parts[1]) * imageHeight;
                    width = parseFloat(parts[2]) * imageWidth;
                    height = parseFloat(parts[3]) * imageHeight;
                } catch (e) {
                    // If there's a problem with saved state, just go back to defaults.
                    usingDefaults = true;
                }
            } else {
                usingDefaults = true;
            }
            // So far, I can't figure out what the 3000 puts us in front of, but we have to be in front of it for dragging to work.
            // ui-resizable styles are setting font-size to 0.1px. so we have to set it back.
            const htmlForHandle = "<div id='elementId' class='classes' style='width:30px;height:30px;background-color:black;color:white;"
                + "z-index:3000;cursor:default;'><p style='padding: 2px 0px 0px 9px;font-size:16px'>" + handleLabel + "</p></div>";
            const htmlForDragHandle = htmlForHandle.replace("elementId", "dragHandle").replace("classes", "bloom-dragHandleAnimation");
            const htmlForResizeHandles = htmlForHandle.replace("elementId", "resizeHandle")
                .replace("classes", "ui-resizable-handle ui-resizable-se") // the "2 box in the lower right"
                + "<div class='ui-resizable-handle ui-resizable-e' style='z-index: 90;'></div>" // handle on left edge (std appearance)
                + "<div class='ui-resizable-handle ui-resizable-s' style='z-index: 90;'></div>" // handle on bottom edge (std appearance)
                + "<div class='ui-resizable-handle ui-resizable-w' style='z-index: 90;'></div>" // handle on right edge (std appearance)
                + "<div class='ui-resizable-handle ui-resizable-n' style='z-index: 90;'></div>"; // handle on top edge (std appearance)
            // The order is important here. Something assumes the drag handle is the first child.
            // It does not work well to just put the border on the outer div.
            // - without a z-index it somehow disappears over jpegs.
            // - putting a z-index on the actual draggable makes a new stacking context and somehow messes up how draggable/resizable work.
            //      All kinds of weird things happen, like handles disappearing,
            //      or not being able to click on them when one box is inside the other.
            const htmlForDraggable = "<div id='" + id + "' style='height: "
                + height + "px; width:" + width + "px; position: absolute; top: " + top + "px; left:" + left + "px;'>"
                + htmlForDragHandle
                + "  <div style='height:100%;width:100%;position:absolute;top:0;left:0;"
                + "border: dashed black 2px;box-sizing:border-box;z-index:1'></div>"
                + htmlForResizeHandles
                + "</div>";
            // Do NOT use an opacity setting here. Besides the fact that there's no reason to dim the rectangle while
            // moving it, it makes a new stacking context, and somehow this causes the dragged rectangle to go
            // behind jpeg images.
            const argsForDraggable = {
                handle: ".bloom-dragHandleAnimation", containment: "parent",
                stop: (event, ui) => this.updateDataAttributes(),
                // This bizarre kludge works around a bug that jquery have decided not to fix in their jquery draggable
                // code (https://bugs.jqueryui.com/ticket/6844). Basically without this the dragging happens as if
                // the view were not scaled. In particular there's a sudden jump when dragging starts.
                drag: (event, ui) => {
                    const xpos = ui.position.left / scale;
                    const ypos = ui.position.top / scale;
                    ui.position.top = ypos;
                    ui.position.left = xpos;
                }
            };
            const argsForResizable = {
                handles: { e: ".ui-resizable-e", s: ".ui-resizable-s", n: ".ui-resizable-n", w: ".ui-resizable-w", se: "#resizeHandle" },
                containment: "parent", aspectRatio: true,
                stop: (event, ui) => this.updateDataAttributes()
            };
            // Unless the element is created and made draggable and resizable in the page iframe's execution context,
            // the dragging and resizing just don't work.
            return getPageFrameExports().makeElement(htmlForDraggable, $(firstImage), argsForResizable, argsForDraggable);
        };
        const rect1 = makeResizeRect("1", "animationStart", 2, 2, imageWidth * 3 / 4, imageHeight * 3 / 4, "data-initialrect");
        const rect2 = makeResizeRect("2", "animationEnd", imageWidth * 3 / 8,
            imageHeight / 8, imageWidth / 2, imageHeight / 2, "data-finalrect");
        if (usingDefaults) {
            // If we're using defaults, the current rectangle positions don't correspond to what's in the file
            // (typically because we're animating this picture for the first time).
            // In case the user is happy with the defaults and doesn't move anything, we should save them.
            // If not using defaults, we don't want to change the file, because some pixel approximation
            // is involved in reading the rectangle positions, and we don't want the rectangles to creep
            // a fraction of a pixel each time we open this page.
            this.updateDataAttributes();
        }
    }
    showTool() {
        this.updateMarkup();
    }
    hideTool() {
        const page = this.getPage();
        page.getElementById("animationStart").remove();
        page.getElementById("animationEnd").remove();
        // enhance: if more than one image...do what??
        const firstImage = this.getFirstImage();
        if (!firstImage) {
            return;
        }
        firstImage.classList.remove("bloom-hideImageButtons");
        this.removeCurrentAudioMarkup();
    }
    removeCurrentAudioMarkup(): void {
        const currentAudioElts = this.getPage().getElementsByClassName("ui-audioCurrent");
        if (currentAudioElts.length) {
            currentAudioElts[0].classList.remove("ui-audioCurrent");
        }
    }

    id(): string {
        return "panAndZoom";
    }

    getFirstImage(): HTMLImageElement {
        const imgElements = this.getPage().getElementsByClassName("bloom-imageContainer");
        if (!imgElements.length) {
            return null;
        }
        return imgElements[0] as HTMLImageElement;
    }


    panAndZoomChanged(checked: boolean) {
        const firstImage = this.getFirstImage();
        if (!firstImage) {
            return;
        }
        if (checked) {
            if (!firstImage.getAttribute("data-initialrect")) {
                // see if we can restore a backup state
                firstImage.setAttribute("data-initialrect", firstImage.getAttribute("data-disabled-initialrect"));
                firstImage.removeAttribute("data-disabled-initialrect");
                this.updateMarkup();
            }
        } else {
            if (firstImage.getAttribute("data-initialrect")) { // always?
                // save old state, thus recording that we're in the off state, not just uninitialized.
                firstImage.setAttribute("data-disabled-initialrect", firstImage.getAttribute("data-initialrect"));
                firstImage.removeAttribute("data-initialrect");
            }
            this.hideTool();
        }
    }

    observer: MutationObserver;

    updateChoosePictureState(): void {
        // If they once choose a picture, there's no going back to a placeholder (on this page).
        this.rootControl.setState({ haveImageContainerButNoImage: false });
        this.observer.disconnect();
        this.updateMarkup(); // one effect is to show the rectangles.
    }

    setupImageObserver(): void {
        // Arrange to update things when they DO choose an image.
        this.observer = new MutationObserver(() => this.updateChoosePictureState());
        // The specific thing we want to observe is the src attr of the img element embedded
        // in the first image container. We want to update our UI if this changes from
        // placeholder to a 'real' image. This will need to be enhanced if we support
        // images done with background-image.
        this.observer.observe(this.getFirstImage().getElementsByTagName("img")[0],
            { attributes: true, attributeFilter: ["src"] });
    }

    // https://github.com/nefe/You-Dont-Need-jQuery says this is eqivalent to $(el).height() which
    // we aren't allowed to use any more.
    getHeight(el) {
        const styles = window.getComputedStyle(el);
        const height = el.offsetHeight;
        const borderTopWidth = parseFloat(styles.borderTopWidth);
        const borderBottomWidth = parseFloat(styles.borderBottomWidth);
        const paddingTop = parseFloat(styles.paddingTop);
        const paddingBottom = parseFloat(styles.paddingBottom);
        return height - borderBottomWidth - borderTopWidth - paddingTop - paddingBottom;
    }

    // Hopefully I figured out the equivalent for width
    getWidth(el) {
        const styles = window.getComputedStyle(el);
        const width = el.offsetWidth;
        const borderLeftWidth = parseFloat(styles.borderLeftWidth);
        const borderRightWidth = parseFloat(styles.borderRightWidth);
        const paddingLeft = parseFloat(styles.paddingLeft);
        const paddingRight = parseFloat(styles.paddingRight);
        return width - borderLeftWidth - borderRightWidth - paddingLeft - paddingRight;
    }

    updateDataAttributes(): void {
        const page = this.getPage();
        const startRect = page.getElementById("animationStart");
        const endRect = page.getElementById("animationEnd");
        const image = startRect.parentElement;

        const fullHeight = this.getHeight(image);
        const fullWidth = this.getWidth(image);

        const scale = EditableDivUtils.getPageScale();

        image.setAttribute("data-initialrect", "" + startRect.offsetLeft / fullWidth / scale
            + " " + startRect.offsetTop / fullHeight / scale
            + " " + this.getWidth(startRect) / fullWidth + " " + this.getHeight(startRect) / fullHeight);
        image.setAttribute("data-finalrect", "" + endRect.offsetLeft / fullWidth / scale + " " + endRect.offsetTop / fullHeight / scale
            + " " + this.getWidth(endRect) / fullWidth + " " + this.getHeight(endRect) / fullHeight);
    }

    removeElt(x: HTMLElement): void {
        if (x) {
            x.remove();
        }
    }

    // This code shares various aspects with BloomPlayer. But I don't see a good way to share them, and many aspects are very different.
    // - This code is simpler because there is only ever one pan-and-zoom-animation-capable image in a document
    //   (for now, anyway, since bloom only displays one page at a time in edit mode and we only support pan and zoom on the first image)
    // - this code is also simpler because we don't have to worry about the image not yet being loaded by the time we
    // want to set up the animation
    // - this code is complicated by having to deal with problems caused by parent divs using scale for zoom.
    // somewhat more care is needed here to avoid adding the animation stuff permanently to the document
    previewPanAndZoom() {
        const page = this.getPage();
        const pageDoc = this.getPageFrame().contentWindow.document;
        const firstImage = this.getFirstImage();
        if (!firstImage || !(document.getElementById("panAndZoom") as HTMLInputElement).checked) {
            return;
        }
        var duration = 0;
        $(page).find(".bloom-editable.bloom-content1").find("span.audio-sentence").each((index, span) => {
            var spanDuration = parseFloat($(span).attr("data-duration"));
            if (spanDuration) { // might be NaN if missing or somehow messed up
                duration += spanDuration;
            }
        });
        if (duration < 0.5) {
            duration = 4;
        }

        const scale = EditableDivUtils.getPageScale();
        // Make a div that wraps a div that will move and be clipped which wraps a clone of firstImage
        // Enhance: when we change the signature of makeElement, we can get rid of the vestiges of JQuery here.
        const wrapDiv = getPageFrameExports().makeElement("<div class='" + this.wrapperClassName
            + " bloom-animationWrapper' style='visibility: hidden; background-color:white; "
            + "height:" + this.getHeight(firstImage) * scale + "px; width:" + this.getWidth(firstImage) * scale + "px; "
            + "position: absolute;"
            + "left:" + firstImage.getBoundingClientRect().left + "px; "
            + "top: " + firstImage.getBoundingClientRect().top + "px; "
            + "'><div id='bloom-movingDiv'></div></div>", $(pageDoc.body))[0] as HTMLElement;
        const movingDiv = wrapDiv.firstElementChild;
        var picToAnimate = firstImage.cloneNode(true) as HTMLElement;
        // don't use getElementById here; the elements we want to remove are NOT yet
        // in the document, but the ones they are clones of (which we want to keep) are.
        picToAnimate.querySelector("#animationStart").remove();
        picToAnimate.querySelector("#animationEnd").remove();
        movingDiv.appendChild(picToAnimate);
        page.documentElement.appendChild(wrapDiv);
        // Todo: it needs to be the page's document.
        const animationElement = pageDoc.createElement("style");
        animationElement.setAttribute("type", "text/css");
        animationElement.setAttribute("id", "animationSheet");
        animationElement.innerText = ".bloom-ui-animationWrapper {overflow: hidden; translateZ(0)} "
            + ".bloom-animate {height: 100%; width: 100%; "
            + "background-repeat: no-repeat; background-size: contain}";
        pageDoc.body.appendChild(animationElement);
        const stylesheet = animationElement.sheet;
        const initialRectStr = firstImage.getAttribute("data-initialrect");
        const initialRect = initialRectStr.split(" ");
        const initialScaleWidth = 1 / parseFloat(initialRect[2]) * scale;
        const initialScaleHeight = 1 / parseFloat(initialRect[3]) * scale;
        const finalRectStr = firstImage.getAttribute("data-finalrect");
        const finalRect = finalRectStr.split(" ");
        const finalScaleWidth = 1 / parseFloat(finalRect[2]) * scale;
        const finalScaleHeight = 1 / parseFloat(finalRect[3]) * scale;
        const wrapDivWidth = this.getWidth(wrapDiv);
        const wrapDivHeight = this.getHeight(wrapDiv);
        const initialX = parseFloat(initialRect[0]) * wrapDivWidth / scale;
        const initialY = parseFloat(initialRect[1]) * wrapDivHeight / scale;
        const finalX = parseFloat(finalRect[0]) * wrapDivWidth / scale;
        const finalY = parseFloat(finalRect[1]) * wrapDivHeight / scale;
        const animateStyleName = "bloom-animationPreview";
        const movePicName = "movepic";
        // Will take the form of "scale3d(W, H,1.0) translate3d(Xpx, Ypx, 0px)"
        // Using 3d scale and transform apparently causes GPU to be used and improves
        // performance over scale/transform. (https://www.kirupa.com/html5/ken_burns_effect_css.htm)
        // May also help with blurring of material originally hidden.
        const initialTransform = "scale3d(" + initialScaleWidth + ", " + initialScaleHeight
            + ", 1.0) translate3d(-" + initialX + "px, -" + initialY + "px, 0px)";
        const finalTransform = "scale3d(" + finalScaleWidth + ", " + finalScaleHeight
            + ", 1.0) translate3d(-" + finalX + "px, -" + finalY + "px, 0px)";
        //Insert the keyframe animation rule with the dynamic begin and end set
        (stylesheet as CSSStyleSheet).insertRule("@keyframes " + movePicName
            + " { from{ transform-origin: 0px 0px; transform: " + initialTransform
            + "; } to{ transform-origin: 0px 0px; transform: " + finalTransform + "; } }", 0);

        //Insert the css for the imageView div that utilizes the newly created animation
        // We make the animation longer than the narration by the transition time so
        // the old animation continues during the fade.
        (stylesheet as CSSStyleSheet).insertRule("." + animateStyleName
            + " { transform-origin: 0px 0px; transform: "
            + initialTransform
            + "; animation-name: " + movePicName + "; animation-duration: "
            + duration
            + "s; animation-fill-mode: forwards; "
            + "animation-timing-function: linear;}", 1);
        movingDiv.setAttribute("class", "bloom-animate bloom-pausable " + animateStyleName);
        // At this point the wrapDiv becomes visible and the animation starts.
        //wrapDiv.show(); mysteriously fails
        wrapDiv.setAttribute("style", wrapDiv.getAttribute("style").replace("visibility: hidden; ", ""));
        // Play the audio during animation
        const audio = new AudioRecording();
        audio.setupForListen();
        audio.listen();
        window.setTimeout(() => {
            this.removeElt(animationElement);
            this.removeElt(wrapDiv);
            this.removeCurrentAudioMarkup();
        }, (duration + 1) * 1000);
    }

    private wrapperClassName = "bloom-ui-animationWrapper";
    public getPageFrame(): HTMLIFrameElement {
        return parent.window.document.getElementById("page") as HTMLIFrameElement;
    }

    // The document object of the editable page, a root for searching for document content.
    public getPage(): HTMLDocument {
        var page = this.getPageFrame();
        if (!page) return null;
        return page.contentWindow.document;
    }

    getStateFromHtml(): IPanAndZoomState {
        const page = this.getPage();
        // enhance: if more than one image...do what??
        const firstImage = this.getFirstImage();
        let wantChoosePictureMessage = false;
        let panAndZoomChecked = true;
        if (!firstImage) {
            // no place to put an image, we can't be enabled.
            // leave choose picture hidden, there's no way to choose an image on this page.
            panAndZoomChecked = false;
        } else {
            if (firstImage.getAttribute("data-disabled-initialrect")) {
                // At some point on this page the check box has been explicitly turned off
                panAndZoomChecked = false;
            }
            // Enhance this if we need to support background-image approach.
            if (firstImage.getElementsByTagName("img")[0].getAttribute("src") === "placeHolder.png") {
                // it's a placeholder, show the message, we need to let them choose it before
                // we hide those controls to show ours.
                wantChoosePictureMessage = true;
            }
        }
        return { haveImageContainerButNoImage: wantChoosePictureMessage, panAndZoomChecked: panAndZoomChecked };
    }
}

interface IPanAndZoomState {
    haveImageContainerButNoImage: boolean;
    panAndZoomChecked: boolean;
}

interface IPanAndZoomProps {
    onPreviewClick: () => void;
    onPanAndZoomChanged: (boolean) => void;
}

// This react class implements the UI for the pan and zoom toolbox.
export class PanAndZoomControl extends React.Component<IPanAndZoomProps, IPanAndZoomState> {
    constructor(props) {
        super(props);
        // This state won't last long, client sets it immediately. But must have something.
        // To minimize flash we start with both off.
        this.state = { haveImageContainerButNoImage: false, panAndZoomChecked: false };
    }

    onPanAndZoomChanged(checked: boolean): void {
        this.setState({ panAndZoomChecked: checked });
        this.props.onPanAndZoomChanged(checked);
    }

    public render() {
        return (
            <div className="ui-panAndZoomBody">
                <Checkbox id="panAndZoom" name="panAndZoom" l10nKey="EditTab.Toolbox.PanAndZoom.Heading"
                    onCheckChanged={(checked) => this.onPanAndZoomChanged(checked)}
                    checked={this.state.panAndZoomChecked}>Pan and Zoom</Checkbox>
                <div className="button-label-wrapper" id="panAndZoom-play-wrapper">
                    <div className="button-wrapper" onClick={() => this.props.onPreviewClick()}>
                        <button id="panAndZoom-preview" className="ui-panAndZoom-button ui-button enabled" />
                        <Div className="panAndZoom-label"
                            l10nKey="EditTab.Toolbox.PanAndZoom.Preview">Preview</Div>
                    </div>
                    <Div className={"panAndZoom-message" + (this.state.haveImageContainerButNoImage ? "" : " hidden")}
                        l10nKey="EditTab.Toolbox.PanAndZoom.ChooseImage">Choose an image to activate the
                        Pan and Zoom controls.</Div>
                </div>
            </div>
        );
    }
}