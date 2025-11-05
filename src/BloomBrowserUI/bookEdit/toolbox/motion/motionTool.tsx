import * as React from "react";
import * as ReactDOM from "react-dom";
import $ from "jquery";
import { Div } from "../../../react_components/l10nComponents";
import { ToolBottomHelpLink } from "../../../react_components/helpLink";
// These are for Motion:
import { EditableDivUtils } from "../../js/editableDivUtils";
import { getEditablePageBundleExports } from "../../js/bloomFrames";
import AudioRecording from "../talkingBook/audioRecording";
import { Checkbox } from "../../../react_components/checkbox";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import { MusicToolControls } from "../music/musicToolControls";
import "./motion.less";
import {
    DisableImageEditing,
    EnableImageEditing,
    getBackgroundImageFromBloomCanvas,
} from "../../js/bloomImages";
import { kMotionToolId } from "../toolIds";
import { RequiresSubscriptionOverlayWrapper } from "../../../react_components/requiresSubscription";
import { getFeatureStatusAsync } from "../../../react_components/featureStatus";
import { TransformBasedAnimator } from "bloom-player";
import {
    kBloomCanvasClass,
    getCanvasElementManager,
} from "../canvas/canvasElementUtils";
import { animateStyleName } from "../../../utils/shared";

// The toolbox is included in the list of tools because of this line of code
// in tooboxBootstrap.ts:
// ToolBox.registerTool(new MotionTool());

/// The motion tool lets you define two rectangles; Bloom Reader will pan & zoom from one to the other
export class MotionTool extends ToolboxToolReactAdaptor {
    private rootControl: MotionControl;
    private narrationPlayer: AudioRecording;
    private stopPreviewTimeout: number;
    private animationPreviewAspectRatio = 16 / 9; // width divided by height of desired simulated device screen

    public makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");
        this.rootControl = ReactDOM.render(
            <MotionControl
                onPreviewClick={() => this.toggleMotionPreviewPlaying()}
                onMotionChanged={(checked) => this.motionChanged(checked)}
            />,
            root,
        ) as unknown as MotionControl;
        const initialState = this.getStateFromHtml();
        this.rootControl.setState(initialState);
        this.setupImageObserver();
        return root as HTMLDivElement;
    }
    public beginRestoreSettings(settings: string): JQueryPromise<void> {
        //Nothing to do, so return an already-resolved promise.
        const result = $.Deferred<void>();
        result.resolve();
        return result;
    }
    public isAlwaysEnabled(): boolean {
        return false;
    }
    public isExperimental(): boolean {
        return false;
    }

    public newPageReady() {
        this.makeRectsVisible();
    }
    private subscriptionAllowsMotion: boolean | undefined = undefined;

    private makeRectsVisible() {
        if (this.subscriptionAllowsMotion === undefined) {
            // Find out if we're really allowed to show them, then do it or not.
            getFeatureStatusAsync("motion").then((featureStatus) => {
                this.subscriptionAllowsMotion = !!(
                    featureStatus && featureStatus.enabled
                );
                this.makeRectsVisible();
            });
            return;
        }
        if (!this.subscriptionAllowsMotion) {
            return;
        } // First, abort any preview that's in progress.
        if (this.rootControl.state.playing) {
            this.toggleMotionPreviewPlaying();
        }
        const newState = this.getStateFromHtml();
        this.setupImageObserver();
        this.rootControl.setState(newState);
        if (!newState.motionChecked || newState.haveBloomCanvasButNoBgImage) {
            return;
        }

        const page = this.getPage();
        if (!page) return; // paranoid
        // enhance: if more than one image...do what??
        const bloomCanvasToAnimate = this.getBloomCanvasToAnimate();
        if (!bloomCanvasToAnimate) return; // paranoid
        DisableImageEditing(bloomCanvasToAnimate);
        this.removeElt(page.getElementById("animationStart"));
        this.removeElt(page.getElementById("animationEnd"));
        const scale = EditableDivUtils.getPageScale();
        let needToSaveRectangles = false;
        const makeResizeRect = (
            handleLabel: string,
            id: string,
            defLeft: number,
            defTop: number,
            defWidth: number,
            defHeight: number,
            initAttr: string,
            color: string,
        ): JQuery => {
            const [left, top, width, height, needToSaveThisRectangle] =
                this.getActualRectFromAttrValue(
                    bloomCanvasToAnimate,
                    defLeft,
                    defTop,
                    defWidth,
                    defHeight,
                    initAttr,
                );
            if (needToSaveThisRectangle) needToSaveRectangles = true;

            // So far, I can't figure out what the 3000 puts us in front of, but we have to be in front of it for dragging to work.
            // ui-resizable styles are setting font-size to 0.1px. so we have to set it back.
            const htmlForHandle =
                `<div id='elementId' class='classes' style='width:30px;height:30px;background-color:${color};color:white;` +
                "z-index:3000;cursor:default;'><p style='padding: 2px 0px 0px 9px;font-size:16px'>" +
                handleLabel +
                "</p></div>";
            const htmlForDragHandle = htmlForHandle
                .replace("elementId", "dragHandle")
                .replace("classes", "bloom-dragHandleAnimation");
            // We're going to quite a bit of trouble here to get a bigger, darker drag handle in the bottom right
            // corner than resizable provides by default. But the default one with our theme is almost invisible.
            // Curiously, the 10% contrast filter makes the light grey in the icon DARKER thus INCREASING contrast.

            const htmlForResizeHandles =
                "" +
                "<div id='resizeHandle' class='ui-resizable-handle ui-resizable-se' " +
                "style='width:16px;height:16px;z-index: 3000;filter:contrast(10%)'>" +
                "<span class='ui-icon ui-icon-grip-diagonal-se'></span></div>" + // handle on bottom right
                "<div class='ui-resizable-handle ui-resizable-e' style='z-index: 90;'></div>" + // handle on left edge (std appearance)
                "<div class='ui-resizable-handle ui-resizable-s' style='z-index: 90;'></div>" + // handle on bottom edge (std appearance)
                "<div class='ui-resizable-handle ui-resizable-w' style='z-index: 90;'></div>" + // handle on right edge (std appearance)
                "<div class='ui-resizable-handle ui-resizable-n' style='z-index: 90;'></div>"; // handle on top edge (std appearance)
            // The order is important here. Something assumes the drag handle is the first child.
            // It does not work well to just put the border on the outer div.
            // - without a z-index it somehow disappears over jpegs.
            // - putting a z-index on the actual draggable makes a new stacking context and somehow messes up how draggable/resizable work.
            //      All kinds of weird things happen, like handles disappearing,
            //      or not being able to click on them when one box is inside the other.
            // The "+1" on top and left seems to be necessary to account for the border that draggable/resizable puts around
            // the box; at least, the value we get back for the box's offset is one less than the value we pass here,
            // which can throw things off, especially in repeated saves with no actual movement.
            // Todo zoom: check that 1 is still the right amount to adjust.
            const htmlForDraggable =
                "<div id='" +
                id +
                "'class='bloom-animationRect' " +
                "style='height: " +
                height +
                "px; width:" +
                width +
                "px; position: absolute; top: " +
                (top + 1) +
                "px; left:" +
                (left + 1) +
                "px;'>" +
                htmlForDragHandle +
                "  <div style='height:100%;width:100%;position:absolute;top:0;left:0;" +
                `border: dashed ${color} 2px;box-sizing:border-box;z-index:2999;pointer-events:none';></div>` + //set the border div's z index to 2999 so it's in front of any overlays, but behind the other rectangle's draggable components (which are set at 3000)
                htmlForResizeHandles +
                "</div>";
            // Do NOT use an opacity setting here. Besides the fact that there's no reason to dim the rectangle while
            // moving it, it makes a new stacking context, and somehow this causes the dragged rectangle to go
            // behind jpeg images.
            const argsForDraggable = {
                handle: ".bloom-dragHandleAnimation",
                stop: (event, ui) => this.updateDataAttributes(),
                // This bizarre kludge works around a bug that jquery have decided not to fix in their jquery draggable
                // code (https://bugs.jqueryui.com/ticket/6844). Basically without this the dragging happens as if
                // the view were not scaled. In particular there's a sudden jump when dragging starts.
                // Setting containment to "parent" uses the unscaled size of the image as the bounds, resulting in
                // areas you can't move the pan&zoom rectangles when the zoom scale > 1.  When the zoom scale < 1, you
                // could drag the rectangles off the picture.  This event handler programmatically enforces the desired
                // containment.  (Setting the containment to a boundary array just didn't work for some reason.)
                drag: (event, ui) => {
                    const xpos = Math.min(
                        Math.max(0, ui.position.left / scale),
                        bloomCanvasToAnimate.clientWidth - ui.helper.width(),
                    );
                    const ypos = Math.min(
                        Math.max(0, ui.position.top / scale),
                        bloomCanvasToAnimate.clientHeight - ui.helper.height(),
                    );
                    ui.position.top = ypos;
                    ui.position.left = xpos;
                },
            };
            const argsForResizable = {
                handles: {
                    e: ".ui-resizable-e",
                    s: ".ui-resizable-s",
                    n: ".ui-resizable-n",
                    w: ".ui-resizable-w",
                    se: "#resizeHandle",
                },
                containment: "parent",
                aspectRatio: true,
                stop: (event, ui) => this.updateDataAttributes(),
            };
            // Unless the element is created and made draggable and resizable in the page iframe's execution context,
            // the dragging and resizing just don't work.
            const result = getEditablePageBundleExports()!.makeElement(
                htmlForDraggable,
                $(bloomCanvasToAnimate),
                argsForResizable,
                argsForDraggable,
            );
            // This is an extra guarantee that it doesn't end up persisted; also, it prevents
            // CanvasElementManager.AdjustChildrenIfSizeChanged from taking it into account.
            result.get(0).classList.add("bloom-ui");
            return result;
        };
        const bloomBlue = "#1D94A4";
        const bloomPurple = "#96668F";
        makeResizeRect(
            "1",
            "animationStart",
            0,
            0,
            3 / 4,
            3 / 4,
            "data-initialrect",
            bloomBlue,
        );
        makeResizeRect(
            "2",
            "animationEnd",
            3 / 8,
            1 / 8,
            1 / 2,
            1 / 2,
            "data-finalrect",
            bloomPurple,
        );
        if (needToSaveRectangles) {
            // If we're using defaults or had to adjust the aspect ratio,
            // the current rectangle positions don't correspond to what's in the file
            // (typically because we're animating this picture for the first time, or perhaps the
            // rectangles came from some import process, or maybe the user changed the image
            // since the last time he set the rectangles and the new one has a different aspect ratio).
            // In case the user is happy with the defaults and doesn't move anything, or there is
            // a significant change due to aspect ratio, we should save them.
            // If we don't need to save, we don't want to change the file, because some pixel approximation
            // is involved in reading the rectangle positions, and we don't want the rectangles to creep
            // a fraction of a pixel each time we open this page. Also we don't want to keep
            // making the document look changed to export processes.
            this.updateDataAttributes();
        }
        this.setupResizeObserver();
    }

    public detachFromPage() {
        if (this.rootControl.state.playing) {
            this.rootControl.setState({ playing: false });
            window.clearTimeout(this.stopPreviewTimeout);
            this.cleanupAnimation();
        }

        const page = this.getPage();
        if (page) {
            this.removeElt(page.getElementById("animationStart"));
            this.removeElt(page.getElementById("animationEnd"));
        }
        // enhance: if more than one image...do what??
        const bloomCanvasToAnimate = this.getBloomCanvasToAnimate();
        if (!bloomCanvasToAnimate) {
            return;
        }
        EnableImageEditing(bloomCanvasToAnimate);
        this.removeCurrentAudioMarkup();
        if (this.observer) {
            this.observer.disconnect();
        }
        if (this.sizeObserver) {
            this.sizeObserver.disconnect();
        }
    }

    private removeCurrentAudioMarkup(): void {
        const page = this.getPage();
        if (!page) return;
        const currentAudioElts = page.getElementsByClassName("ui-audioCurrent");
        if (currentAudioElts.length) {
            currentAudioElts[0].classList.remove("ui-audioCurrent");
        }
    }

    public id(): string {
        return kMotionToolId;
    }

    public featureName? = kMotionToolId;

    private getBloomCanvasToAnimate(): HTMLElement | null {
        const page = this.getPage();
        return page?.getElementsByClassName(
            kBloomCanvasClass,
        )[0] as HTMLElement;
    }

    // Given one of the start/end rectangle objects, produce the string we want to save in
    // data-initialRect or data-finalRect.
    // This string is a representation of a rectangle as left top width height, where each is
    // a fraction of the actual bloom-canvas size.
    private getTransformRectAttrValue(htmlRect: HTMLElement): string {
        const rectTop = htmlRect.offsetTop;
        const rectLeft = htmlRect.offsetLeft;
        const rectWidth = this.getWidth(htmlRect);
        const rectHeight = this.getHeight(htmlRect);
        const bloomCanvasToAnimate = this.getBloomCanvasToAnimate();
        if (!bloomCanvasToAnimate) return ""; // paranoid

        const imageHeight = this.getHeight(bloomCanvasToAnimate);
        const imageWidth = this.getWidth(bloomCanvasToAnimate);

        const top = rectTop / imageHeight;
        const left = rectLeft / imageWidth;
        const width = rectWidth / imageWidth;
        const height = rectHeight / imageHeight;
        const result = "" + left + " " + top + " " + width + " " + height;
        //alert("saved " + actualLeft + " " + actualTop + " " + actualWidth + " " + actualHeight + " got " + result);
        return result;
    }

    // Performs the reverse of the above transformation.
    // Todo zoom: this needs to get the right actual widths.
    private getActualRectFromAttrValue(
        bloomCanvasToAnimate: HTMLElement,
        defLeft: number,
        defTop: number,
        defWidth: number,
        defHeight: number,
        initAttr: string,
    ): [number, number, number, number, boolean] {
        let left = defLeft,
            top = defTop,
            width = defWidth,
            height = defHeight;
        let needToSaveRectangle = true;
        const savedState = bloomCanvasToAnimate.getAttribute(initAttr);
        if (savedState) {
            try {
                const parts = savedState.split(" ");
                // NB parseFloat is safe because Javascript parseFloat is not culture-specific.
                // Get the size relative to the image itself
                left = this.tryParseFloat(parts[0], defLeft);
                top = this.tryParseFloat(parts[1], defTop);
                width = this.tryParseFloat(parts[2], defWidth);
                height = this.tryParseFloat(parts[3], defHeight);
                // if we got a full set of saved values, we don't need to save...and would
                // prefer not to, so rounding errors can't accumulate.
                needToSaveRectangle = false;
            } catch (e) {
                // If there's a problem with saved state, just use defaults.
                // (If we got some values, still don't use them...a mixture might
                // produce something weird.)
                left = defLeft;
                top = defTop;
                width = defWidth;
                height = defHeight;
            }
        }
        const imageHeight = this.getHeight(bloomCanvasToAnimate);
        const imageWidth = this.getWidth(bloomCanvasToAnimate);
        let actualTop = top * imageHeight;
        let actualLeft = left * imageWidth;
        let actualWidth = width * imageWidth;
        let actualHeight = height * imageHeight;
        // We want things to fit in the bloom-canvas. This can be broken in various ways:
        // - we may have changed to an image that is a different shape since last displaying
        // - we may have changed the shape of the bloom-canvas by origami
        // - we may have changed the shape of the bloom-canvas by choosing a different page layout
        // (We may decide to go stronger than this and make sure it's within the actual image.)
        const bloomCanvasWidth = this.getWidth(bloomCanvasToAnimate);
        const bloomCanvasHeight = this.getHeight(bloomCanvasToAnimate);
        if (actualWidth > bloomCanvasWidth) {
            actualWidth = bloomCanvasWidth;
        }
        if (actualHeight > bloomCanvasHeight) {
            actualHeight = bloomCanvasHeight;
        }
        // For proper animation rectangles must be the same shape as the picture.
        // If we relax this constraint, we need to fix both our own preview code
        // and the main bloom player code so that the playback rectangle shape
        // is determined by the initialRect. Also, to avoid distortion,
        // we need to make sure they are at least the SAME shape (as each other).
        // But, we don't want to save changes that are just caused by rounding errors
        // converting % sizes to pixels.
        // To ensure this, if the calculated adjustment is less than 2 pixels,
        // we just don't make it.
        if (actualWidth / actualHeight > imageWidth / imageHeight) {
            // proposed rectangle is too wide for height. Reduce width.
            const possibleWidth = (actualHeight * imageWidth) / imageHeight;
            if (possibleWidth < actualWidth - 1) {
                actualWidth = possibleWidth;
                needToSaveRectangle = true;
            }
        } else if (actualWidth / actualHeight < imageWidth / imageHeight) {
            // too high for width. Reduce height.
            const possibleHeight = (actualWidth * imageHeight) / imageWidth;
            if (possibleHeight < actualHeight - 1) {
                actualHeight = possibleHeight;
                needToSaveRectangle = true;
            }
        }
        // Now make sure it's not positioned outside the container.
        // This should be done after we settled on a size that will fit
        // and is the right shape.
        if (actualLeft < 0) {
            actualLeft = 0;
        }
        if (actualLeft + actualWidth > bloomCanvasWidth) {
            actualLeft = bloomCanvasWidth - actualWidth;
        }
        if (actualTop < 0) {
            actualTop = 0;
        }
        if (actualTop + actualHeight > bloomCanvasHeight) {
            actualTop = bloomCanvasHeight - actualHeight;
        }
        return [
            actualLeft,
            actualTop,
            actualWidth,
            actualHeight,
            needToSaveRectangle,
        ];
    }

    private tryParseFloat(input: string, def: number): number {
        try {
            return parseFloat(input);
        } catch (e) {
            return def;
        }
    }

    private motionChanged(checked: boolean) {
        const bloomCanvasToAnimate = this.getBloomCanvasToAnimate();
        if (!bloomCanvasToAnimate) {
            return;
        }
        if (checked) {
            if (!bloomCanvasToAnimate.getAttribute("data-initialrect")) {
                // see if we can restore a backup state
                bloomCanvasToAnimate.setAttribute(
                    "data-initialrect",
                    bloomCanvasToAnimate.getAttribute(
                        "data-disabled-initialrect",
                    ) as string,
                );
                bloomCanvasToAnimate.removeAttribute(
                    "data-disabled-initialrect",
                );
                this.makeRectsVisible(); // ensures start/stop rectangles visible
            }
        } else {
            if (bloomCanvasToAnimate.getAttribute("data-initialrect")) {
                // always?
                // save old state, thus recording that we're in the off state, not just uninitialized.
                bloomCanvasToAnimate.setAttribute(
                    "data-disabled-initialrect",
                    bloomCanvasToAnimate.getAttribute(
                        "data-initialrect",
                    ) as string,
                );
                bloomCanvasToAnimate.removeAttribute("data-initialrect");
            }
            this.detachFromPage();
        }
    }

    private observer: MutationObserver;
    private sizeObserver: ResizeObserver;

    private updateMotionRectanglesState(): void {
        let haveBgImage = false;
        const bgImage = this.getBackgroundImage();
        if (bgImage) {
            const src = bgImage.getAttribute("src");
            haveBgImage = !!src && src.toLowerCase() !== "placeholder.png";
        }
        const newState = this.getStateFromHtml();
        if (haveBgImage) {
            newState.haveBloomCanvasButNoBgImage = false;
            newState.motionPossible = true;
            this.rootControl.setState(newState);
            this.makeRectsVisible();
        } else {
            newState.haveBloomCanvasButNoBgImage = true;
            newState.motionPossible = false;
            this.rootControl.setState(newState);
            this.hideRectangles();
        }
    }

    private hideRectangles(): void {
        const bloomCanvasToAnimate = this.getBloomCanvasToAnimate();
        bloomCanvasToAnimate?.removeAttribute("data-initialrect");
        bloomCanvasToAnimate?.removeAttribute("data-finalrect");
        const page = this.getPage();
        if (page) {
            this.removeElt(page.getElementById("animationStart"));
            this.removeElt(page.getElementById("animationEnd"));
        }
    }

    private resizeOldWidth = 0;
    private resizeOldHeight = 0;

    // This is called when the size of the bloom canvas changes.
    // It seems to get called constantly even when nothing has actually changed,
    // perhaps because makeRectsVisible actually re-creates the obsever, so we
    // debounce it by checking for an actual size change.
    // (Earlier versions using MutationObserver also debounced it to happen at most every 200ms;
    // that no longer seems to be necessary for smooth resizing.)
    private pictureSizeChanged(): void {
        const bloomCanvasToAnimate = this.getBloomCanvasToAnimate();
        if (
            !bloomCanvasToAnimate ||
            (bloomCanvasToAnimate.offsetHeight === this.resizeOldHeight &&
                bloomCanvasToAnimate.offsetWidth === this.resizeOldWidth)
        )
            return;
        // We don't actually need to redo everything this does, but it's the easiest way
        // to ensure everything is correct.
        this.makeRectsVisible();
    }

    private setupResizeObserver(): void {
        if (this.sizeObserver) {
            this.sizeObserver.disconnect();
        }
        const bloomCanvasToAnimate = this.getBloomCanvasToAnimate();
        if (!bloomCanvasToAnimate) return;
        this.sizeObserver = new ResizeObserver(() => this.pictureSizeChanged());
        this.sizeObserver.observe(bloomCanvasToAnimate);
        this.resizeOldHeight = bloomCanvasToAnimate.offsetHeight;
        this.resizeOldWidth = bloomCanvasToAnimate.offsetWidth;
    }

    private setupImageObserver(): void {
        // Arrange to update things when the user chooses or deletes an image.
        this.observer = new MutationObserver(() =>
            this.updateMotionRectanglesState(),
        );
        // The specific thing we want to observe is the src attr of the img element embedded
        // in the background image of the first bloom-canvas. We want to update our UI if this changes from
        // placeholder to a 'real' image.
        const bgImage = this.getBackgroundImage();
        if (bgImage) {
            const src = bgImage.getAttribute("src");
            if (!src || src.toLowerCase() === "placeholder.png") {
                this.hideRectangles();
            }
            this.observer.observe(bgImage, {
                attributes: true,
                attributeFilter: ["src"],
            });
        }
    }

    private getBackgroundImage(): HTMLElement | null {
        const bloomCanvasToAnimate = this.getBloomCanvasToAnimate();
        if (bloomCanvasToAnimate)
            return getBackgroundImageFromBloomCanvas(bloomCanvasToAnimate);
        return null;
    }

    // https://github.com/nefe/You-Dont-Need-jQuery says this is equivalent to $(el).height() which
    // we aren't allowed to use any more.
    // I believe this is a height that does NOT need to be scaled by our zoom factor
    // (e.g., it can be used unchanged to set a css width in px that will work zoomed)
    private getHeight(el) {
        const styles = window.getComputedStyle(el);
        const height = el.offsetHeight;
        const borderTopWidth = this.safeParseFloat(styles.borderTopWidth);
        const borderBottomWidth = this.safeParseFloat(styles.borderBottomWidth);
        const paddingTop = this.safeParseFloat(styles.paddingTop);
        const paddingBottom = this.safeParseFloat(styles.paddingBottom);
        return (
            height -
            borderBottomWidth -
            borderTopWidth -
            paddingTop -
            paddingBottom
        );
    }

    private safeParseFloat(stringMeasure: string | null): number {
        let measure: string = "";
        if (stringMeasure) {
            measure = stringMeasure;
        }
        return parseFloat(measure);
    }

    // Hopefully I figured out the equivalent for width
    private getWidth(el) {
        const styles = window.getComputedStyle(el);
        const width = el.offsetWidth;
        const borderLeftWidth = this.safeParseFloat(styles.borderLeftWidth);
        const borderRightWidth = this.safeParseFloat(styles.borderRightWidth);
        const paddingLeft = this.safeParseFloat(styles.paddingLeft);
        const paddingRight = this.safeParseFloat(styles.paddingRight);
        return (
            width -
            borderLeftWidth -
            borderRightWidth -
            paddingLeft -
            paddingRight
        );
    }

    private updateDataAttributes(): void {
        //alert("updating data attributes " + new Error().stack);
        const page = this.getPage();
        if (!page) return; // paranoid
        const startRect = page.getElementById("animationStart");
        const endRect = page.getElementById("animationEnd");
        if (!startRect || !endRect) return;
        const image = startRect.parentElement;
        if (image) {
            image.setAttribute(
                "data-initialrect",
                this.getTransformRectAttrValue(startRect),
            );
            image.setAttribute(
                "data-finalrect",
                this.getTransformRectAttrValue(endRect),
            );
        }
    }

    private removeElt(x: HTMLElement | null): void {
        if (x) {
            x.remove();
        }
    }

    //if this is ever changed, be sure to also change it in bloomUI.less
    readonly hiddenStyleName: string = "bloom-hidden-for-animation";

    //hide everything on the page, make a copy of the canvas, and move it using the TransformBasedAnimator class from bloom-player
    //Enhance: refactor this method and bloom-player's Animation.setupAnimation() to share more of the code that sets up the HTML structure around the canvas
    private toggleMotionPreviewPlaying() {
        const bloomCanvasToAnimate = this.getBloomCanvasToAnimate();
        if (
            !bloomCanvasToAnimate ||
            !(document.getElementById("motion") as HTMLInputElement).checked ||
            this.rootControl.state.haveBloomCanvasButNoBgImage
        ) {
            return;
        }
        const wasPlaying: boolean = this.rootControl.state.playing;
        // A 'functional' mode of using setState is recommended when the new state is a function
        // of the old state, like this:
        // this.rootControl.setState((oldState) => {
        //     return ({ playing: !oldState.playing });
        // });
        // But then, what do we do to determine whether to turn on or off? If we can't trust the
        // current state, we'd have to wait until the function we pass to setState is called.
        // But in theory, that could be just one of a series of calls before things stabilize.
        // There's probably a React function that notifies us of state change that we could
        // use. But it seems unnecessarily complicated. I don't think the user can click fast
        // enough for state not to have updated before the next click.
        this.rootControl.setState({ playing: !wasPlaying });
        if (wasPlaying) {
            // In case we start it again before the old timeout expires, we don't
            // want it to stop in the middle.
            window.clearTimeout(this.stopPreviewTimeout);
            this.cleanupAnimation();
            return;
        }
        getCanvasElementManager()?.setActiveElement(undefined);

        const page = this.getPage();
        if (!page || !page.documentElement) return; // paranoid
        const contentWindow = this.getPageFrame().contentWindow;
        if (!contentWindow) return; // paranoid
        const bloomPage = page.getElementsByClassName(
            "bloom-page",
        )[0] as HTMLElement;

        //create the animation divs
        const animationBackground = document.createElement("div");
        animationBackground.classList.add(animateStyleName);
        const animationWrapper = document.createElement("div");
        const animationCanvas = bloomCanvasToAnimate.cloneNode(
            true,
        ) as HTMLElement;

        //if the page has canvas overlay elements with elements such as speech bubbles, those
        //are drawn in a <canvas/> element created by the ComicalJS library.
        //when a <canvas/> is copied, its contents are not copied with it,
        //so we need to specifically copy the drawings from the original canvas to the clone.
        const comicalCanvas = bloomCanvasToAnimate.querySelector(
            ".comical-generated",
        ) as HTMLCanvasElement;
        if (comicalCanvas) {
            const animatedComicalCanvas = animationCanvas.querySelector(
                ".comical-generated",
            ) as HTMLCanvasElement;
            // this timeout seems to be necessary in cases where comical controls were
            // visible before we cleared the active element, to prevent the handles showing
            // in the preview.
            setTimeout(() => {
                const drawingContext = animatedComicalCanvas.getContext("2d");
                drawingContext?.drawImage(comicalCanvas, 0, 0);
            }, 0);
        }
        // before we add the animation elements, lest we count any recorded overlays twice.
        const duration = this.calculateDuration(page);

        //Prepare the animation background
        const editorBody = bloomPage.closest("body")!;
        editorBody.insertBefore(animationBackground, editorBody.firstChild!);
        // turn it into a 16:9 black rectangle to show aspect ratio
        animationBackground.style.backgroundColor = "black";
        animationBackground.style.width = `${editorBody.clientWidth}px`;
        animationBackground.style.height = `${
            editorBody.clientWidth * (9 / 16)
        }px`;
        animationBackground.classList.add("bloom-ui"); //this class tells the C# code responsible for saving the page to disregard this element

        //Prepare the animation wrapper
        animationBackground.appendChild(animationWrapper);
        //position the animation view inside the black rectangle
        const canvasDimensions = animationCanvas
            .getAttribute("data-imgsizebasedon")
            ?.split(",")
            .map(parseFloat) ?? [16, 9];
        const animationAspectRatio = canvasDimensions[0] / canvasDimensions[1];
        if (animationAspectRatio > this.animationPreviewAspectRatio) {
            animationWrapper.style.width = "100%";
            animationWrapper.style.height = `${
                animationWrapper.clientWidth / animationAspectRatio
            }px`;
            animationWrapper.style.left = "0px";
            animationWrapper.style.top = `${
                0.5 *
                (animationBackground.clientHeight -
                    animationWrapper.clientHeight)
            }px`;
        } else {
            animationWrapper.style.height = "100%";
            animationWrapper.style.width = `${
                animationWrapper.clientHeight * animationAspectRatio
            }px`;
            animationWrapper.style.top = "0px";
            animationWrapper.style.left = `${
                0.5 *
                (animationBackground.clientWidth - animationWrapper.clientWidth)
            }px`;
        }
        animationWrapper.style.overflow = "hidden";
        animationWrapper.style.transform = "translateZ(0)"; //needed for overflow:hidden to have an effect on a transformed child

        //Prepare the animation canvas
        animationWrapper.appendChild(animationCanvas);
        const wrapperDimensions = [
            animationWrapper.clientWidth,
            animationWrapper.clientHeight,
        ];

        //Canvas overlay elementss have their width, height, and bubble positions set by absolute pixel values
        //Because of that, we need to make sure the animationCanvas has the default pixel height and width that the overlays expect
        //Then we can safely rescale the animationCanvas to the size we want, and the overlays will rescale with it.
        animationCanvas.style.width = `${canvasDimensions[0]}px`;
        animationCanvas.style.height = `${canvasDimensions[1]}px`;
        animationCanvas.style.scale = `${
            wrapperDimensions[0] / canvasDimensions[0]
        }`;
        animationCanvas.style.top = `${
            (wrapperDimensions[1] - canvasDimensions[1]) / 2
        }px`;
        animationCanvas.style.left = `${
            (wrapperDimensions[0] - canvasDimensions[0]) / 2
        }px`;

        //hide the animation rectangles
        animationCanvas.removeChild(
            animationCanvas.querySelector("#animationStart")!,
        );
        animationCanvas.removeChild(
            animationCanvas.querySelector("#animationEnd")!,
        );

        //hide the whole page (styles in bloomUI.less keep the animation from being hidden)
        editorBody.classList.add(this.hiddenStyleName);

        //start the animation
        const initialRect = animationCanvas.getAttribute("data-initialrect")!;
        const finalRect = animationCanvas.getAttribute("data-finalrect")!;
        const animationEngine = new TransformBasedAnimator(
            initialRect,
            finalRect,
            duration,
            animationCanvas,
        );
        animationEngine.startAnimation();

        if (this.rootControl.state.previewVoice) {
            // Play the audio during animation. Don't mess with highlight while constructing
            // the recorder.
            this.narrationPlayer = new AudioRecording(false);
            this.narrationPlayer.setupForListen();
            this.narrationPlayer.listenAsync(bloomCanvasToAnimate);
        }
        if (this.rootControl.state.previewMusic) {
            MusicToolControls.previewBackgroundMusic(
                this.getPlayer(),
                // Enhance: implement pause, by adding playing to state.
                () => false,
                (playing) => undefined,
            );
        }
        this.stopPreviewTimeout = window.setTimeout(
            () => {
                this.cleanupAnimation();
                this.rootControl.setState({ playing: false });
            },
            (duration + 1) * 1000,
        );
    }

    private cleanupAnimation() {
        const page = this.getPage();
        if (!page) return;

        // stop the animation by removing any elements it added to the page.
        const animationElements = Array.from(
            page.getElementsByClassName(animateStyleName),
        );
        animationElements.forEach((child) =>
            child.parentElement!.removeChild(child),
        );

        const bloomPage = page.getElementsByClassName(
            "bloom-page",
        )[0] as HTMLElement;
        bloomPage.classList.remove("Landscape");

        const editorBody = bloomPage.closest("body")!;
        editorBody.classList.remove(this.hiddenStyleName);

        //show the "change layout" toggle again
        const layoutToggle = page.querySelector(
            ".above-page-control-container",
        ) as HTMLElement;
        if (
            layoutToggle &&
            layoutToggle.classList.contains(this.hiddenStyleName)
        ) {
            layoutToggle.classList.remove(this.hiddenStyleName);
        }

        // stop narration if any.
        if (this.narrationPlayer) {
            this.narrationPlayer.stopListen();
        }
        this.removeCurrentAudioMarkup();
        // stop background music
        this.getPlayer().pause();
    }

    private calculateDuration(page: HTMLDocument): number {
        let duration = 0;

        // Array.from required in Geckofx45
        Array.from(page.querySelectorAll(".audio-sentence")).forEach(
            (audioPortion) => {
                duration += this.getDurationOrZero(audioPortion);
            },
        );

        if (duration < 0.5) {
            duration = 4;
        }
        return duration;
    }

    private getDurationOrZero(element: Element): number {
        const durationString = element.attributes["data-duration"];
        if (durationString) {
            const duration = parseFloat(durationString.value);

            if (duration) {
                return duration;
            }
        }
        return 0;
    }

    private getPlayer(): HTMLMediaElement {
        return document.getElementById("pzMusicPlayer") as HTMLMediaElement;
    }

    private wrapperClassName = "bloom-ui-animationWrapper";
    private getPageFrame(): HTMLIFrameElement {
        return parent.window.document.getElementById(
            "page",
        ) as HTMLIFrameElement;
    }

    // The document object of the editable page, a root for searching for document content.
    private getPage(): HTMLDocument | null {
        const page = this.getPageFrame();
        if (!page || !page.contentWindow) return null;
        return page.contentWindow.document;
    }

    private getStateFromHtml(): IMotionHtmlState {
        // enhance: if more than one image...do what??
        const bloomCanvasToAnimate = this.getBloomCanvasToAnimate();
        let src = "";
        if (bloomCanvasToAnimate) {
            const bgImage =
                getBackgroundImageFromBloomCanvas(bloomCanvasToAnimate);
            src = bgImage?.getAttribute("src") || "";
        }

        const doNotHaveAPicture = !src || src.startsWith("placeHolder.png");

        let motionChecked = true;
        let motionPossible = !doNotHaveAPicture;
        if (!bloomCanvasToAnimate || ToolboxToolReactAdaptor.isXmatter()) {
            // if there's no place to put an image, we can't be enabled.
            // And we don't support Motion in xmatter (BL-5427),
            // in part because we use background-image there and haven't fully supported
            // panning and zooming that; but mainly just don't think it makes
            // sense. In either case, leave choose picture hidden, there's no way
            // to choose an image on this page, or (in xmatter) it wouldn't help.
            motionChecked = false;
            motionPossible = false;
        } else {
            if (
                bloomCanvasToAnimate.getAttribute("data-disabled-initialrect")
            ) {
                // At some point on this page the check box has been explicitly turned off
                motionChecked = false;
            }
        }
        return {
            haveBloomCanvasButNoBgImage: doNotHaveAPicture,
            motionChecked: motionChecked,
            motionPossible: motionPossible,
        };
    }
}

interface IMotionHtmlState {
    haveBloomCanvasButNoBgImage: boolean;
    motionChecked: boolean;
    motionPossible: boolean;
}

interface IMotionState extends IMotionHtmlState {
    previewVoice: boolean;
    previewMusic: boolean;
    playing: boolean;
}

interface IMotionProps {
    onPreviewClick: () => void;
    onMotionChanged: (boolean) => void;
}

// This react class implements the UI for the motion tool.
export class MotionControl extends React.Component<IMotionProps, IMotionState> {
    // This state won't last long, client sets the first two immediately.
    // But must have something. To minimize flash we start with both off.
    public readonly state: IMotionState = {
        haveBloomCanvasButNoBgImage: false,
        motionChecked: false,
        motionPossible: true,
        previewVoice: true,
        previewMusic: true,
        playing: false,
    };

    private onMotionChanged(checked: boolean): void {
        this.setState({ motionChecked: checked });
        this.props.onMotionChanged(checked);
    }

    public render() {
        return (
            <RequiresSubscriptionOverlayWrapper featureName={kMotionToolId}>
                <div
                    className={
                        "ui-motionBody" +
                        (this.state.motionPossible ? "" : " disabled")
                    }
                >
                    <div>
                        <Div
                            l10nKey="EditTab.Toolbox.Motion.Intro"
                            l10nComment="Shown at the top of the 'Motion Tool' in the Edit tab"
                            className="intro"
                        >
                            Motion Books are Bloom Reader books with two modes.
                            Normally, they are Talking Books. When you turn the
                            phone sideways, the picture fills the screen. It
                            pans and zooms from rectangle "1" to rectangle "2".
                        </Div>
                        <Checkbox
                            id="motion"
                            name="motion"
                            className="enable-checkbox"
                            l10nKey="EditTab.Toolbox.Motion.ThisPage"
                            // tslint:disable-next-line:max-line-length
                            l10nComment="Motion here refers to panning and zooms image when it is viewed in Bloom Reader. Google 'Ken Burns effect' to see exactly what we mean."
                            onCheckChanged={(checked) =>
                                this.onMotionChanged(checked)
                            }
                            checked={this.state.motionChecked}
                        >
                            Enable motion on this page
                        </Checkbox>
                        <div
                            className={
                                "button-label-wrapper" +
                                (this.state.motionChecked ? "" : " disabled")
                            }
                            id="motion-play-wrapper"
                        >
                            <div className="button-wrapper">
                                <button
                                    id="motion-preview"
                                    className={
                                        "ui-motion-button ui-button enabled" +
                                        (this.state.playing ? " playing" : "")
                                    }
                                    onClick={() => this.props.onPreviewClick()}
                                />
                                <div className="previewSettingsWrapper">
                                    <Div
                                        className="motion-label"
                                        l10nKey="EditTab.Toolbox.Motion.Preview"
                                    >
                                        Preview
                                    </Div>
                                    <Checkbox
                                        name="previewMotion"
                                        l10nKey="EditTab.Toolbox.Motion.Preview.Motion"
                                        checked={true}
                                        disabled={true}
                                    >
                                        Motion
                                    </Checkbox>
                                    <Checkbox
                                        name="previewVoice"
                                        l10nKey="EditTab.Toolbox.Motion.Preview.Voice"
                                        onCheckChanged={(checked) =>
                                            this.setState({
                                                previewVoice: checked,
                                            })
                                        }
                                        checked={this.state.previewVoice}
                                    >
                                        Voice
                                    </Checkbox>
                                    <Checkbox
                                        name="previewMusic"
                                        l10nKey="EditTab.Toolbox.Motion.Preview.Music"
                                        onCheckChanged={(checked) =>
                                            this.setState({
                                                previewMusic: checked,
                                            })
                                        }
                                        checked={this.state.previewMusic}
                                    >
                                        Music
                                    </Checkbox>
                                </div>
                            </div>
                        </div>
                    </div>
                    <ToolBottomHelpLink helpId="Tasks/Edit_tasks/Motion_Tool/Motion_Tool_overview.htm" />
                    <audio id="pzMusicPlayer" preload="none" />
                </div>
            </RequiresSubscriptionOverlayWrapper>
        );
    }
}
