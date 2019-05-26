import * as React from "react";
import * as ReactDOM from "react-dom";
import { Div } from "../../../react_components/l10nComponents";
import { ToolBottomHelpLink } from "../../../react_components/helpLink";
// These are for Motion:
import { EditableDivUtils } from "../../js/editableDivUtils";
import { getPageFrameExports } from "../../js/bloomFrames";
import AudioRecording from "../talkingBook/audioRecording";
import { Checkbox } from "../../../react_components/checkbox";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import { MusicToolControls } from "../music/musicToolControls";
import "./motion.less";

// The toolbox is included in the list of tools because of this line of code
// in tooboxBootstrap.ts:
// ToolBox.registerTool(new MotionTool());

/// The motion tool lets you define two rectangles; Bloom Reader will pan & zoom from one to the other
export class MotionTool extends ToolboxToolReactAdaptor {
    private rootControl: MotionControl;
    private animationStyleElement: HTMLStyleElement | null;
    private animationWrapDiv: HTMLElement | null;
    private animationRootDiv: HTMLElement | null;
    private narrationPlayer: AudioRecording;
    private stopPreviewTimeout: number;
    private animationPreviewAspectRatio = 16 / 9; // width divided by height of desired simulated device screen

    public makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");
        root.setAttribute("class", "ui-motionBody");
        this.rootControl = (ReactDOM.render(
            <MotionControl
                onPreviewClick={() => this.toggleMotionPreviewPlaying()}
                onMotionChanged={checked => this.motionChanged(checked)}
            />,
            root
        ) as unknown) as MotionControl;
        const initialState = this.getStateFromHtml();
        this.rootControl.setState(initialState);
        if (initialState.haveImageContainerButNoImage) {
            this.setupImageObserver();
        }
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

    private makeRectsVisible() {
        // First, abort any preview that's in progress.
        if (this.rootControl.state.playing) {
            this.toggleMotionPreviewPlaying();
        }
        const newState = this.getStateFromHtml();
        if (newState.haveImageContainerButNoImage) {
            this.setupImageObserver();
        }
        this.rootControl.setState(newState);
        if (!newState.motionChecked || newState.haveImageContainerButNoImage) {
            return;
        }

        const page = this.getPage();
        if (!page) return; // paranoid
        // enhance: if more than one image...do what??
        const firstImage = this.getFirstImage();
        if (!firstImage) return; // paranoid
        firstImage.classList.add("bloom-hideImageButtons");
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
            color: string
        ): JQuery => {
            let needToSaveThisRectangle: boolean;
            let left: number, top: number, width: number, height: number;
            [
                left,
                top,
                width,
                height,
                needToSaveThisRectangle
            ] = this.getActualRectFromAttrValue(
                firstImage,
                defLeft,
                defTop,
                defWidth,
                defHeight,
                initAttr
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
                "' style='height: " +
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
                `border: dashed ${color} 2px;box-sizing:border-box;z-index:1'></div>` +
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
                        firstImage.clientWidth - ui.helper.width()
                    );
                    const ypos = Math.min(
                        Math.max(0, ui.position.top / scale),
                        firstImage.clientHeight - ui.helper.height()
                    );
                    ui.position.top = ypos;
                    ui.position.left = xpos;
                }
            };
            const argsForResizable = {
                handles: {
                    e: ".ui-resizable-e",
                    s: ".ui-resizable-s",
                    n: ".ui-resizable-n",
                    w: ".ui-resizable-w",
                    se: "#resizeHandle"
                },
                containment: "parent",
                aspectRatio: true,
                stop: (event, ui) => this.updateDataAttributes()
            };
            // Unless the element is created and made draggable and resizable in the page iframe's execution context,
            // the dragging and resizing just don't work.
            return getPageFrameExports().makeElement(
                htmlForDraggable,
                $(firstImage),
                argsForResizable,
                argsForDraggable
            );
        };
        const bloomBlue = "#1D94A4";
        const bloomPurple = "#96668F";
        const rect1 = makeResizeRect(
            "1",
            "animationStart",
            0,
            0,
            3 / 4,
            3 / 4,
            "data-initialrect",
            bloomBlue
        );
        const rect2 = makeResizeRect(
            "2",
            "animationEnd",
            3 / 8,
            1 / 8,
            1 / 2,
            1 / 2,
            "data-finalrect",
            bloomPurple
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
        const page = this.getPage();
        if (page) {
            this.removeElt(page.getElementById("animationStart"));
            this.removeElt(page.getElementById("animationEnd"));
        }
        // enhance: if more than one image...do what??
        const firstImage = this.getFirstImage();
        if (!firstImage) {
            return;
        }
        firstImage.classList.remove("bloom-hideImageButtons");
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
        return "motion";
    }

    private getFirstImage(): HTMLElement | null {
        const page = this.getPage();
        if (!page) return null;
        const imgElements = page.getElementsByClassName("bloom-imageContainer");
        if (!imgElements.length) {
            return null;
        }
        return imgElements[0] as HTMLElement;
    }

    // Given one of the start/end rectangle objects, produce the string we want to save in
    // data-initialRect or data-finalRect.
    // This string is a representation of a rectangle as left top width height, where each is
    // a fraction of the actual image size. (Note: the image, NOT the image container, even
    // if the image is just a background image...though the current code does not support that.)
    private getTransformRectAttrValue(htmlRect: HTMLElement): string {
        const rectTop = htmlRect.offsetTop;
        const rectLeft = htmlRect.offsetLeft;
        const rectWidth = this.getWidth(htmlRect);
        const rectHeight = this.getHeight(htmlRect);
        const image = this.getFirstImage();
        if (!image) return ""; // paranoid

        const actualImage = image.getElementsByTagName("img")[0];
        const imageTop = actualImage.offsetTop;
        const imageLeft = actualImage.offsetLeft;
        const imageHeight = this.getHeight(actualImage);
        const imageWidth = this.getWidth(actualImage);

        const top = (rectTop - imageTop) / imageHeight;
        const left = (rectLeft - imageLeft) / imageWidth;
        const width = rectWidth / imageWidth;
        const height = rectHeight / imageHeight;
        const result = "" + left + " " + top + " " + width + " " + height;
        //alert("saved " + actualLeft + " " + actualTop + " " + actualWidth + " " + actualHeight + " got " + result);
        return result;
    }

    // Performs the reverse of the above transformation.
    // Todo zoom: this needs to get the right actual widths.
    private getActualRectFromAttrValue(
        firstImage: HTMLElement,
        defLeft: number,
        defTop: number,
        defWidth: number,
        defHeight: number,
        initAttr: string
    ): [number, number, number, number, boolean] {
        let left = defLeft,
            top = defTop,
            width = defWidth,
            height = defHeight;
        let needToSaveRectangle = true;
        const savedState = firstImage.getAttribute(initAttr);
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
        const actualImage = firstImage.getElementsByTagName("img")[0];
        const imageHeight = this.getHeight(actualImage);
        const imageWidth = this.getWidth(actualImage);
        let actualTop = top * imageHeight + actualImage.offsetTop;
        let actualLeft = left * imageWidth + actualImage.offsetLeft;
        let actualWidth = width * imageWidth;
        let actualHeight = height * imageHeight;
        // We want things to fit in the image container. This can be broken in various ways:
        // - we may have changed to an image that is a different shape since last displaying
        // - we may have changed the shape of the image container by origami
        // - we may have changed the shape of the image container by choosing a different page layout
        // (We may decide to go stronger than this and make sure it's within the actual image.)
        const containerWidth = this.getWidth(firstImage);
        const containerHeight = this.getHeight(firstImage);
        if (actualWidth > containerWidth) {
            actualWidth = containerWidth;
        }
        if (actualHeight > containerHeight) {
            actualHeight = containerHeight;
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
        if (actualLeft + actualWidth > containerWidth) {
            actualLeft = containerWidth - actualWidth;
        }
        if (actualTop < 0) {
            actualTop = 0;
        }
        if (actualTop + actualHeight > containerHeight) {
            actualTop = containerHeight - actualHeight;
        }
        return [
            actualLeft,
            actualTop,
            actualWidth,
            actualHeight,
            needToSaveRectangle
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
        const firstImage = this.getFirstImage();
        if (!firstImage) {
            return;
        }
        if (checked) {
            if (!firstImage.getAttribute("data-initialrect")) {
                // see if we can restore a backup state
                firstImage.setAttribute(
                    "data-initialrect",
                    firstImage.getAttribute(
                        "data-disabled-initialrect"
                    ) as string
                );
                firstImage.removeAttribute("data-disabled-initialrect");
                this.makeRectsVisible(); // ensures start/stop rectangles visible
            }
        } else {
            if (firstImage.getAttribute("data-initialrect")) {
                // always?
                // save old state, thus recording that we're in the off state, not just uninitialized.
                firstImage.setAttribute(
                    "data-disabled-initialrect",
                    firstImage.getAttribute("data-initialrect") as string
                );
                firstImage.removeAttribute("data-initialrect");
            }
            this.detachFromPage();
        }
    }

    private observer: MutationObserver;
    private sizeObserver: MutationObserver;

    private updateChoosePictureState(): void {
        // If they once choose a picture, there's no going back to a placeholder (on this page).
        this.rootControl.setState({ haveImageContainerButNoImage: false });
        this.observer.disconnect();
    }

    private resizeRectanglesDelay: number = 200;
    private resizeInProgress: boolean = false;
    private resizeOldStyle: string | null;

    // This is called when the size of the picture changes. We also get LOTS
    // of spurious calls, for example, while resizing the rectangles. And even
    // when really resizing the image container, we get too many calls, and
    // the handler gets behind the events and things get sluggish if not worse.
    // Worse, when resizing the rectangles, somehow the scaleImage code is triggered
    // on the main image, and the size and position attributes may briefly
    // be cleared, so for a short time they may really be changed, though no
    // permanent change is occurring.
    // Waiting briefly and then seeing whether the style really changed
    // prevents the spurious events from triggering regeneration of the rectangles
    // during resizing, which can prevent the resizing altogether.
    // Simply waiting briefly reduces the frequency of regenerating
    // the rectangles to something manageable and makes it feel much more responsive.
    private pictureSizeChanged(): void {
        if (this.resizeInProgress) {
            return;
        }
        setTimeout(() => {
            // allow any future notifications to be processed.
            this.resizeInProgress = false;
            const images = this.getImages();
            if (images[0].getAttribute("style") === this.resizeOldStyle) {
                return; // spurious notification
            }
        }, this.resizeRectanglesDelay);
        this.resizeInProgress = true; // ignore notifications until timeout
    }

    private getImages(): Array<HTMLImageElement> {
        const firstImage = this.getFirstImage();
        if (!firstImage) return []; // paranoid
        // not interested in images inside the resize rectangles.
        return Array.prototype.slice
            .call(firstImage.getElementsByTagName("img"))
            .filter(v => v.parentElement === firstImage);
    }

    private setupResizeObserver(): void {
        if (this.sizeObserver) {
            this.sizeObserver.disconnect();
        }
        this.sizeObserver = new MutationObserver(() =>
            this.pictureSizeChanged()
        );
        const images = this.getImages();

        // I'm not sure how images can be an empty list...possibly while the page is shutting down??
        // But I've seen the JS error, so being defensive...we can't observe an image that doesn't exist.
        if (images.length > 0) {
            const style = images[0].getAttribute("style");
            this.resizeOldStyle = style;
            // jquery's scaleImage function adjusts the position and size of the element to
            // keep it centered when the size of the image container changes.
            // margin-top and margin-left are only set using style; height and width
            // are also set in their own attributes. But if any of them changes, the style does.
            // We would prefer to use a ResizeObserver, but Gecko doesn't implement it yet.
            this.sizeObserver.observe(images[0], {
                attributes: true,
                attributeFilter: ["style"]
            });
        }
    }

    private setupImageObserver(): void {
        // Arrange to update things when they DO choose an image.
        this.observer = new MutationObserver(() =>
            this.updateChoosePictureState()
        );
        // The specific thing we want to observe is the src attr of the img element embedded
        // in the first image container. We want to update our UI if this changes from
        // placeholder to a 'real' image. This will need to be enhanced if we support
        // images done with background-image.
        const firstImage = this.getFirstImage();
        if (firstImage) {
            const images = firstImage.getElementsByTagName("img");
            // I'm not sure how images can be an empty list...possibly while the page is shutting down??
            // But I've seen the JS error, so being defensive...we can't observe an image that doesn't exist.
            if (images.length > 0) {
                this.observer.observe(images[0], {
                    attributes: true,
                    attributeFilter: ["src"]
                });
            }
        }
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
                this.getTransformRectAttrValue(startRect)
            );
            image.setAttribute(
                "data-finalrect",
                this.getTransformRectAttrValue(endRect)
            );
        }
    }

    private removeElt(x: HTMLElement | null): void {
        if (x) {
            x.remove();
        }
    }

    private animateStyleName: string = "bloom-animationPreview";

    // This code shares various aspects with BloomPlayer. But I don't see a good way to share them, and many aspects are very different.
    // - This code is simpler because there is only ever one motion-capable image in a document
    //   (for now, anyway, since bloom only displays one page at a time in edit mode and we only support motion on the first image)
    // - this code is also simpler because we don't have to worry about the image not yet being loaded by the time we
    // want to set up the animation
    // - this code is complicated by having to deal with problems caused by parent divs using scale for zoom.
    // somewhat more care is needed here to avoid adding the animation stuff permanently to the document
    // Review: Man this is a long method! It used to be almost 300 lines. I've refactored to bring it down
    // some to just over 200 lines.
    private toggleMotionPreviewPlaying() {
        const firstImage = this.getFirstImage();
        if (
            !firstImage ||
            !(document.getElementById("motion") as HTMLInputElement).checked ||
            this.rootControl.state.haveImageContainerButNoImage
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
        const page = this.getPage();
        if (!page || !page.documentElement) return; // paranoid
        const contentWindow = this.getPageFrame().contentWindow;
        if (!contentWindow) return; // paranoid

        const scale = EditableDivUtils.getPageScale();
        const bloomPage = page.getElementsByClassName(
            "bloom-page"
        )[0] as HTMLElement;
        const pageWidth = this.getWidth(bloomPage);

        const pageDoc = contentWindow.document;
        // Make a div with the shape of a typical phone screen in landscape mode which
        // will be the root for displaying the animation.
        const animationPageHeight =
            (pageWidth / this.animationPreviewAspectRatio) * scale;
        const animationPageWidth = pageWidth * scale;
        this.animationRootDiv = getPageFrameExports().makeElement(
            "<div " +
                "style='background-color:black; " +
                "height:" +
                animationPageHeight +
                "px; width:" +
                animationPageWidth +
                "px; " +
                "position: absolute;" +
                "left: 0;" +
                "top: 0;" +
                "'></div>",
            $(pageDoc.body)
        )[0] as HTMLElement;

        // Make a div that determines the shape and position of the animation.
        // It wraps a div that will move (by being scaled larger) and be clipped (to animationWrapDiv)
        // which in turn wraps a modified clone of firstImage, the content that gets panned and zoomed.
        // Enhance: when we change the signature of makeElement, we can get rid of the vestiges of JQuery here and above.
        this.animationWrapDiv = getPageFrameExports().makeElement(
            "<div class='" +
                this.wrapperClassName +
                " bloom-animationWrapper' " +
                "'><div id='bloom-movingDiv'></div></div>"
        )[0] as HTMLElement;

        const baseStyle = "visibility: hidden; background-color:white;";

        // Figure out the size and position we need for animationRootDiv and animationWrapDiv.
        // We use the original image to get the aspect ratio here because the clone
        // may not have finished loading yet.
        const originalImage = firstImage.getElementsByTagName("img")[0];
        // Enhance: if we allow the zoom rectangles to be a different shape from the image,
        // this should change to get the aspect ratio from the initialrect.
        const panZoomAspectRatio =
            this.getWidth(originalImage) / this.getHeight(originalImage);
        if (panZoomAspectRatio < this.animationPreviewAspectRatio) {
            // black bars on side
            const imageWidth = animationPageHeight * panZoomAspectRatio;
            this.animationWrapDiv.setAttribute(
                "style",
                baseStyle +
                    " height: 100%; width: " +
                    imageWidth +
                    "px; left: " +
                    (animationPageWidth - imageWidth) / 2 +
                    "px; top:0"
            );
        } else {
            // black bars top and bottom
            const imageHeight = animationPageWidth / panZoomAspectRatio;
            this.animationWrapDiv.setAttribute(
                "style",
                baseStyle +
                    " width: 100%; height: " +
                    imageHeight +
                    "px; top: " +
                    (animationPageHeight - imageHeight) / 2 +
                    "px; left: 0"
            );
        }
        const picToAnimate = firstImage.cloneNode(true) as HTMLElement;
        // don't use getElementById here; the elements we want to remove are NOT yet
        // in the document, but the ones they are clones of (which we want to keep) are.
        const start = picToAnimate.querySelector("#animationStart");
        if (start) {
            start.remove();
        }
        const end = picToAnimate.querySelector("#animationEnd");
        if (end) {
            end.remove();
        }
        picToAnimate.setAttribute(
            "style",
            "height:" +
                animationPageHeight +
                "px;width: " +
                animationPageWidth +
                "px;"
        );
        const duration = this.calculateDuration(page);
        const movingDiv = this.animationWrapDiv.firstElementChild;
        const initialRectStr = firstImage.getAttribute("data-initialrect");
        const finalRectStr = firstImage.getAttribute("data-finalrect");
        if (initialRectStr && finalRectStr && movingDiv) {
            // paranoia
            movingDiv.appendChild(picToAnimate);
            this.animationRootDiv.appendChild(this.animationWrapDiv);
            page.documentElement.appendChild(this.animationRootDiv);
            const actualImage = picToAnimate.getElementsByTagName("img")[0];

            // unfortunately the cloned image brings over attributes that position it in the current container.
            // The new parent is not the same size and we need different values to center it and make
            // it fill the container as much as possible.
            // We'd like to position it using SetupImage(actualImage); (from bloomImages) but for some reason,
            // probably because several parents are newly created, that's proving flaky.
            // The code here is a simplification of that method.
            const imgAspectRatio = panZoomAspectRatio; // should stay actual image AR, even if the other changes.
            const containerWidth = this.getWidth(picToAnimate);
            const containerHeight = this.getHeight(picToAnimate);
            let newWidth: number, newHeight: number;
            if (imgAspectRatio > containerWidth / containerHeight) {
                // full width, center vertically.
                newWidth = containerWidth;
                newHeight = containerWidth / imgAspectRatio;
            } else {
                newHeight = containerHeight;
                newWidth = containerHeight * imgAspectRatio;
            }
            // not sure why we set width and height both ways...using the same approach as SetupImage.
            actualImage.setAttribute("width", "" + newWidth);
            actualImage.setAttribute("height", "" + newHeight);
            actualImage.style.width = "" + newWidth + "px";
            actualImage.style.height = "" + newHeight + "px";
            actualImage.style.marginLeft = "0px";
            actualImage.style.marginTop = "0px";
            this.animationStyleElement = pageDoc.createElement("style");
            this.animationStyleElement.setAttribute("type", "text/css");
            this.animationStyleElement.setAttribute("id", "animationSheet");
            this.animationStyleElement.innerText =
                ".bloom-ui-animationWrapper {overflow: hidden; translateZ(0)} " +
                ".bloom-animate {height: 100%; width: 100%; " +
                "background-repeat: no-repeat; background-size: contain}";
            pageDoc.body.appendChild(this.animationStyleElement);
            const stylesheet = this.animationStyleElement.sheet;
            if (stylesheet) {
                this.addCssRules(
                    stylesheet,
                    duration,
                    initialRectStr,
                    finalRectStr
                );
            }
            movingDiv.setAttribute(
                "class",
                "bloom-animate bloom-pausable " + this.animateStyleName
            );
        } // end paranoia 'if'

        // At this point the wrapDiv becomes visible and the animation starts.
        //wrapDiv.show(); mysteriously fails
        const currentStyle = this.animationWrapDiv.getAttribute("style");
        if (currentStyle) {
            this.animationWrapDiv.setAttribute(
                "style",
                currentStyle.replace("visibility: hidden; ", "")
            );
        }
        bloomPage.style.visibility = "hidden";
        if (this.rootControl.state.previewVoice) {
            // Play the audio during animation
            this.narrationPlayer = new AudioRecording();
            this.narrationPlayer.setupForListen();
            this.narrationPlayer.listen();
        }
        if (this.rootControl.state.previewMusic) {
            MusicToolControls.previewBackgroundMusic(
                this.getPlayer(),
                // Enhance: implement pause, by adding playing to state.
                () => false,
                playing => undefined
            );
        }
        this.stopPreviewTimeout = window.setTimeout(() => {
            this.cleanupAnimation();
            this.rootControl.setState({ playing: false });
        }, (duration + 1) * 1000);
    }

    // Add CSS rules for keyframes and preview animation
    private addCssRules(
        stylesheet: StyleSheet,
        duration: number,
        initialRectStr: string,
        finalRectStr: string
    ) {
        const wrapDivWidth = this.getWidth(this.animationWrapDiv);
        const wrapDivHeight = this.getHeight(this.animationWrapDiv);
        const initialRect = initialRectStr.split(" ");
        const finalRect = finalRectStr.split(" ");
        // Will take the form of "scale3d(W, H,1.0) translate3d(Xpx, Ypx, 0px)"
        // Using 3d scale and transform apparently causes GPU to be used and improves
        // performance over scale/transform. (https://www.kirupa.com/html5/ken_burns_effect_css.htm)
        // May also help with blurring of material originally hidden.
        const initialTransform = this.calculateTransform(
            initialRect,
            wrapDivWidth,
            wrapDivHeight
        );
        const finalTransform = this.calculateTransform(
            finalRect,
            wrapDivWidth,
            wrapDivHeight
        );

        //Insert the keyframe animation rule with the dynamic begin and end set
        const movePicName = "movepic";
        (stylesheet as CSSStyleSheet).insertRule(
            "@keyframes " +
                movePicName +
                " { from{ transform-origin: 0px 0px; transform: " +
                initialTransform +
                "; } to{ transform-origin: 0px 0px; transform: " +
                finalTransform +
                "; } }",
            0
        );

        //Insert the css for the imageView div that utilizes the newly created animation
        // We make the animation longer than the narration by the transition time so
        // the old animation continues during the fade.
        (stylesheet as CSSStyleSheet).insertRule(
            "." +
                this.animateStyleName +
                " { transform-origin: 0px 0px; transform: " +
                initialTransform +
                "; animation-name: " +
                movePicName +
                "; animation-duration: " +
                duration +
                "s; animation-fill-mode: forwards; " +
                "animation-timing-function: linear;}",
            1
        );
    }

    private calculateTransform(
        rect: string[],
        wrapDivWidth: number,
        wrapDivHeight: number
    ): string {
        const scaleWidth = 1 / parseFloat(rect[2]);
        const scaleHeight = 1 / parseFloat(rect[3]);
        const x = parseFloat(rect[0]) * wrapDivWidth;
        const y = parseFloat(rect[1]) * wrapDivHeight;
        return this.generateTransformString(scaleWidth, scaleHeight, x, y);
    }

    private generateTransformString(
        scaleWidth: number,
        scaleHeight: number,
        x: number,
        y: number
    ): string {
        return (
            "scale3d(" +
            scaleWidth +
            ", " +
            scaleHeight +
            ", 1.0) translate3d(" +
            -x +
            "px, " +
            -y +
            "px, 0px)"
        );
    }

    private calculateDuration(page: HTMLDocument): number {
        let duration = 0;
        $(page)
            .find(".bloom-editable.bloom-content1")
            .find(".audio-sentence")
            .each((index, span) => {
                const spanDuration = parseFloat($(span).attr("data-duration"));
                if (spanDuration) {
                    // might be NaN if missing or somehow messed up
                    duration += spanDuration;
                }
            });
        if (duration < 0.5) {
            duration = 4;
        }
        return duration;
    }

    private cleanupAnimation() {
        const page = this.getPage();
        if (!page) return;
        (page.getElementsByClassName(
            "bloom-page"
        )[0] as HTMLElement).style.visibility = "";
        // stop the animation itself by removing the root elements it adds.
        this.removeElt(this.animationStyleElement);
        this.animationStyleElement = null;
        this.removeElt(this.animationRootDiv);
        this.animationWrapDiv = null;
        this.animationRootDiv = null;
        // stop narration if any.
        if (this.narrationPlayer) {
            this.narrationPlayer.stopListen();
        }
        this.removeCurrentAudioMarkup();
        // stop background music
        this.getPlayer().pause();
    }

    private getPlayer(): HTMLMediaElement {
        return document.getElementById("pzMusicPlayer") as HTMLMediaElement;
    }

    private wrapperClassName = "bloom-ui-animationWrapper";
    private getPageFrame(): HTMLIFrameElement {
        return parent.window.document.getElementById(
            "page"
        ) as HTMLIFrameElement;
    }

    // The document object of the editable page, a root for searching for document content.
    private getPage(): HTMLDocument | null {
        const page = this.getPageFrame();
        if (!page || !page.contentWindow) return null;
        return page.contentWindow.document;
    }

    private getStateFromHtml(): IMotionHtmlState {
        const pageClass = ToolboxToolReactAdaptor.getBloomPageAttr("class");
        const xmatter = !pageClass
            ? false // paranoia
            : pageClass.indexOf("bloom-frontMatter") >= 0 ||
              pageClass.indexOf("bloom-backMatter") >= 0;
        // enhance: if more than one image...do what??
        const firstImage = this.getFirstImage();

        // Enhance this if we need to support background-image approach.
        const images = firstImage
            ? firstImage.getElementsByTagName("img")
            : null;
        // I'm not quite sure how we can have no images in an image container
        // in a non-xmatter page, but I've seen JS errors caused by it, so
        // programming defensively.
        let srcAttr: string | null = "";
        if (images !== null && images.length > 0) {
            srcAttr = images[0].getAttribute("src");
        }

        const doNotHaveAPicture =
            images === null ||
            images === undefined ||
            images.length === 0 ||
            !srcAttr ||
            srcAttr.indexOf("placeHolder") > -1;

        let motionChecked = true;
        let motionPossible = !doNotHaveAPicture;
        if (!firstImage || xmatter) {
            // if there's no place to put an image, we can't be enabled.
            // And we don't support Motion in xmatter (BL-5427),
            // in part because we use background-image there and haven't fully supported
            // panning and zooming that; but mainly just don't think it makes
            // sense. In either case, leave choose picture hidden, there's no way
            // to choose an image on this page, or (in xmatter) it wouldn't help.
            motionChecked = false;
            motionPossible = false;
        } else {
            if (firstImage.getAttribute("data-disabled-initialrect")) {
                // At some point on this page the check box has been explicitly turned off
                motionChecked = false;
            }
        }
        return {
            haveImageContainerButNoImage: doNotHaveAPicture,
            motionChecked: motionChecked,
            motionPossible: motionPossible
        };
    }
}

interface IMotionHtmlState {
    haveImageContainerButNoImage: boolean;
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
        haveImageContainerButNoImage: false,
        motionChecked: false,
        motionPossible: true,
        previewVoice: true,
        previewMusic: true,
        playing: false
    };

    private onMotionChanged(checked: boolean): void {
        this.setState({ motionChecked: checked });
        this.props.onMotionChanged(checked);
    }

    public render() {
        return (
            <div
                className={
                    "ui-motionBody" +
                    (this.state.motionPossible ? "" : " disabled")
                }
            >
                <Div
                    l10nKey="EditTab.Toolbox.Motion.Intro"
                    l10nComment="Shown at the top of the 'Motion Tool' in the Edit tab"
                    className="intro"
                >
                    Motion Books are Bloom Reader books with two modes.
                    Normally, they are Talking Books. When you turn the phone
                    sideways, the picture fills the screen. It pans and zooms
                    from rectangle "1" to rectangle "2".
                </Div>
                <Checkbox
                    id="motion"
                    name="motion"
                    wrapClassName="enable-checkbox"
                    l10nKey="EditTab.Toolbox.Motion.ThisPage"
                    // tslint:disable-next-line:max-line-length
                    l10nComment="Motion here refers to panning and zooms image when it is viewed in Bloom Reader. Google 'Ken Burns effect' to see exactly what we mean."
                    onCheckChanged={checked => this.onMotionChanged(checked)}
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
                                onCheckChanged={checked =>
                                    this.setState({ previewVoice: checked })
                                }
                                checked={this.state.previewVoice}
                            >
                                Voice
                            </Checkbox>
                            <Checkbox
                                name="previewMusic"
                                l10nKey="EditTab.Toolbox.Motion.Preview.Music"
                                onCheckChanged={checked =>
                                    this.setState({ previewMusic: checked })
                                }
                                checked={this.state.previewMusic}
                            >
                                Music
                            </Checkbox>
                        </div>
                    </div>
                </div>
                <div className="helpLinkWrapper">
                    <ToolBottomHelpLink helpId="Tasks/Edit_tasks/Motion_Tool/Motion_Tool_overview.htm" />
                </div>
                <audio id="pzMusicPlayer" preload="none" />
            </div>
        );
    }
}
