import { Bubble } from "comicaljs";
import { Point } from "../point";
import {
    getImageFromCanvasElement,
    isPlaceHolderImage,
    kImageContainerClass,
} from "../bloomImages";
import { renderCanvasElementContextControls } from "./CanvasElementContextControls";
import theOneLocalizationManager from "../../../lib/localizationManager/localizationManager";
import {
    kBackgroundImageClass,
    kBloomButtonClass,
    kBloomCanvasSelector,
} from "../../toolbox/canvas/canvasElementConstants";

export interface ICanvasElementSelectionUiHost {
    getActiveElement: () => HTMLElement | undefined;

    setActiveElement: (element: HTMLElement | undefined) => void;

    adjustContainerAspectRatio: (
        canvasElement: HTMLElement,
        useSizeOfNewImage?: boolean,
    ) => void;

    startResizeDrag: (
        event: MouseEvent,
        corner: "ne" | "nw" | "se" | "sw",
    ) => void;

    startSideControlDrag: (event: MouseEvent, side: string) => void;

    startMoveCrop: (event: MouseEvent) => void;

    adjustMoveCropHandleVisibility: (
        removeCropAttrsIfNotNeeded?: boolean,
    ) => void;
}

export class CanvasElementSelectionUi {
    private host: ICanvasElementSelectionUiHost;
    private thingToFocusAfterSettingColor: HTMLElement | undefined;

    public constructor(host: ICanvasElementSelectionUiHost) {
        this.host = host;
    }

    // Remove the canvas element control frame if it exists (when no canvas element is active)
    // Also remove the menu if it's still open.  See BL-13852.
    public removeControlFrame(): void {
        // this.activeElement is still set and works for hiding the menu.
        const activeElement = this.host.getActiveElement();
        const controlFrame = document.getElementById(
            "canvas-element-control-frame",
        );
        if (controlFrame) {
            if (activeElement) {
                // we're going to remove the container of the canvas element context controls,
                // but it seems best to let React clean up after itself.
                // For example, there may be a context menu popup to remove, too.
                renderCanvasElementContextControls(activeElement, false);
            }
            // Reschedule so that the rerender can finish before removing the control frame.
            setTimeout(() => {
                controlFrame.remove();
                document
                    .getElementById("canvas-element-context-controls")
                    ?.remove();
            }, 0);
        }
    }

    public checkActiveElementIsVisible(): void {
        const activeElement = this.host.getActiveElement();
        if (!activeElement) {
            return;
        }
        if (window.getComputedStyle(activeElement).display === "none") {
            this.host.setActiveElement(undefined);
        }
    }

    // Set up the control frame for the active canvas element. This includes creating it if it
    // doesn't exist, and positioning it correctly.
    public setupControlFrame(): void {
        // If the active element isn't visible, it isn't really active.  See BL-14439.
        this.checkActiveElementIsVisible();
        const eltToPutControlsOn = this.host.getActiveElement();
        let controlFrame = document.getElementById(
            "canvas-element-control-frame",
        );
        if (!eltToPutControlsOn) {
            this.removeControlFrame();
            return;
        }

        if (!controlFrame) {
            controlFrame =
                eltToPutControlsOn.ownerDocument.createElement("div");
            controlFrame.setAttribute("id", "canvas-element-control-frame");
            controlFrame.classList.add("bloom-ui"); // makes sure it gets cleaned up.
            eltToPutControlsOn.parentElement?.appendChild(controlFrame);
            const corners = ["ne", "nw", "se", "sw"];
            corners.forEach((corner) => {
                const control =
                    eltToPutControlsOn.ownerDocument.createElement("div");
                control.classList.add("bloom-ui-canvas-element-resize-handle");
                control.classList.add(
                    "bloom-ui-canvas-element-resize-handle-" + corner,
                );
                controlFrame?.appendChild(control);
                control.addEventListener("mousedown", (event) => {
                    this.host.startResizeDrag(
                        event,
                        corner as "ne" | "nw" | "se" | "sw",
                    );
                });
            });
            // "sides means not just left and right, but all four sides of the control frame"
            const sides = ["n", "s", "e", "w"];
            sides.forEach((side) => {
                const sideControl =
                    eltToPutControlsOn.ownerDocument.createElement("div");
                sideControl.classList.add(
                    "bloom-ui-canvas-element-side-handle",
                );
                sideControl.classList.add(
                    "bloom-ui-canvas-element-side-handle-" + side,
                );
                controlFrame?.appendChild(sideControl);
                sideControl.addEventListener("mousedown", (event) => {
                    if (event.buttons !== 1 || !this.host.getActiveElement()) {
                        return;
                    }
                    const target = event.currentTarget as HTMLElement;
                    if (target.closest(`.bloom-image-control-frame-no-image`)) {
                        return; // don't crop empty image container
                    }
                    this.host.startSideControlDrag(event, side);
                });
            });
            const sideHandle =
                eltToPutControlsOn.ownerDocument.createElement("div");
            sideHandle.classList.add(
                "bloom-ui-canvas-element-move-crop-handle",
            );
            controlFrame?.appendChild(sideHandle);
            sideHandle.addEventListener("mousedown", (event) => {
                if (event.buttons !== 1 || !this.host.getActiveElement()) {
                    return;
                }
                this.host.startMoveCrop(event);
            });
            const toolboxRoot =
                eltToPutControlsOn.ownerDocument.createElement("div");
            toolboxRoot.setAttribute("id", "canvas-element-context-controls");
            // We don't have to worry about removing this before saving because it is above the level
            // of the bloom-page.
            document.body.appendChild(toolboxRoot);
        }
        const imageContainer =
            eltToPutControlsOn?.getElementsByClassName(
                kImageContainerClass,
            )?.[0];
        const hasImage = !!imageContainer;
        if (hasImage) {
            controlFrame.classList.add("has-image");
        } else {
            controlFrame.classList.remove("has-image");
        }
        if (eltToPutControlsOn?.classList.contains(kBloomButtonClass)) {
            controlFrame.classList.add("is-button");
        } else {
            controlFrame.classList.remove("is-button");
        }

        const hasSvg =
            eltToPutControlsOn?.getElementsByClassName("bloom-svg")?.length > 0;
        if (hasSvg) {
            controlFrame.classList.add("has-svg");
        } else {
            controlFrame.classList.remove("has-svg");
        }
        const hasText =
            eltToPutControlsOn?.getElementsByClassName(
                "bloom-editable bloom-visibility-code-on",
            ).length > 0;
        if (hasText) {
            controlFrame.classList.add("has-text");
        } else {
            controlFrame.classList.remove("has-text");
        }
        // to reduce flicker we don't show this when switching to a different canvas element until we determine
        // that it is wanted.
        controlFrame.classList.remove(
            "bloom-ui-canvas-element-show-move-crop-handle",
        );
        // If the canvas element is not the right shape for a contained image, fix it now.
        // This also aligns the canvas element controls with the image (possibly after waiting
        // for the image dimensions)
        this.host.adjustContainerAspectRatio(eltToPutControlsOn);
        renderCanvasElementContextControls(eltToPutControlsOn, false);
    }

    public async getHandleTitlesAsync(
        controlFrame: HTMLElement,
        className: string,
        l10nId: string,
        force: boolean = false,
        attribute: string = "title",
    ): Promise<void> {
        const handles = Array.from(
            controlFrame.getElementsByClassName(className),
        ) as HTMLElement[];
        // We could cache these somewhere, especially the crop/change shape pair, but I think
        // it would be premature optimization. We only have four title, and
        // only the crop/change shape one has to be retrieved each time the frame moves.
        if (!handles[0]?.getAttribute(attribute) || force) {
            const title = await theOneLocalizationManager.asyncGetText(
                "EditTab.Toolbox.ComicTool.Handle." + l10nId,
                "",
                "",
            );
            handles.forEach((handle) => {
                handle.setAttribute(attribute, title);
            });
        }
    }

    // Align the control frame with the active canvas element.
    public alignControlFrameWithActiveElement(): void {
        const controlFrame = document.getElementById(
            "canvas-element-control-frame",
        );
        let controlsAbove = false;
        const activeElement = this.host.getActiveElement();
        if (!controlFrame || !activeElement) return;

        if (controlFrame.parentElement !== activeElement.parentElement) {
            activeElement.parentElement?.appendChild(controlFrame);
        }
        controlFrame.classList.toggle(
            "bloom-noAutoHeight",
            activeElement.classList.contains("bloom-noAutoHeight"),
        );
        // We want some special CSS rules for control frames on background images (e.g., no resize handles).
        // But we give the class a different name so the control frame won't accidentally be affected
        // by any CSS intended for the background image itself. That is, if the active element (the actual canvas
        // element) has kBackgroundImageClass, which triggers its own CSS rules, we want the control frame
        // to have this different class to trigger control frame background-specific CSS rules.
        controlFrame.classList.toggle(
            kBackgroundImageClass + "-control-frame",
            activeElement.classList.contains(kBackgroundImageClass),
        );

        // mark empty image control frames with a special class
        let imageIsPlaceHolder = false;
        const img = getImageFromCanvasElement(activeElement);
        if (img && isPlaceHolderImage(img.getAttribute("src"))) {
            imageIsPlaceHolder = true;
        }
        controlFrame.classList.toggle(
            "bloom-image-control-frame-no-image",
            imageIsPlaceHolder,
        );

        const hasText = controlFrame.classList.contains("has-text");
        // We don't need to await these, they are just async so the handle titles can be updated
        // once the localization manager retrieves them.
        void this.getHandleTitlesAsync(
            controlFrame,
            "bloom-ui-canvas-element-resize-handle",
            "Resize",
        );
        void this.getHandleTitlesAsync(
            controlFrame,
            "bloom-ui-canvas-element-side-handle",
            hasText ? "ChangeShape" : "Crop",
            // We don't need to change it while we're moving the frame, only if we're switching
            // between text and image. And there's another state we want
            // when cropping a background image and snapped.
            !controlFrame.classList.contains("moving"),
            "data-title",
        );
        void this.getHandleTitlesAsync(
            controlFrame,
            "bloom-ui-canvas-element-move-crop-handle",
            "Shift",
        );
        // Text boxes get a little extra padding, making the control frame bigger than
        // the canvas element itself. The extra needed corresponds roughly to the (.less) @sideHandleRadius,
        // but one pixel less seems to be enough to prevent the side handles actually overlapping text,
        // though maybe I've just been lucky and this should really be 4.
        // Seems like it should be easy to do this in the .less file, but the control frame is not
        // a child of the canvas element (for z-order reasons), so it's not easy for CSS to move it left
        // when the style is already absolutely controlling style.left. It's easier to just tweak
        // it here.
        const extraPadding = hasText ? 3 : 0;
        // using pxToNumber here because the position and size of the canvas element are often fractional.
        // OTOH, clientWidth etc are whole numbers. If we allow that rounding in to affect where to
        // place the control frame, we can end up with a 1 pixel gap between the canvas element and
        // the control frame, which looks bad. In case we want to use some other unit (e.g., %) in a template
        // we use the offsetWidth as a fallback.
        controlFrame.style.width =
            CanvasElementSelectionUi.pxToNumber(
                activeElement.style.width,
                activeElement.offsetWidth,
            ) +
            2 * extraPadding +
            "px";
        controlFrame.style.height = activeElement.style.height;
        controlFrame.style.left =
            CanvasElementSelectionUi.pxToNumber(activeElement.style.left) -
            extraPadding +
            "px";
        controlFrame.style.top = activeElement.style.top;
        const tails = Bubble.getBubbleSpec(activeElement).tails;
        if (tails.length > 0) {
            const tipY = tails[0].tipY;
            controlsAbove =
                tipY > activeElement.clientHeight + activeElement.offsetTop;
        }
        this.host.adjustMoveCropHandleVisibility();
        this.adjustContextControlPosition(controlFrame, controlsAbove);
    }

    public adjustContextControlPosition(
        controlFrame: HTMLElement | null,
        controlsAbove: boolean,
    ): void {
        const contextControl = document.getElementById(
            "canvas-element-context-controls",
        );
        if (!contextControl) return;
        if (!controlFrame) {
            contextControl.remove();
            return;
        }
        const scalingContainer = document.getElementById(
            "page-scaling-container",
        );
        // The context controls look as if they're on the page, so they should have the same scaling.
        // But they aren't actually in the scaling container, so we have to give them their
        // own scaling transform.
        contextControl.style.transform =
            scalingContainer?.style.transform ?? "";
        const controlFrameRect = controlFrame.getBoundingClientRect();
        const contextControlRect = contextControl.getBoundingClientRect();
        const scale = Point.getScalingFactor();

        // This just needs to be wider than the context controls ever are. They get centered in a box this wide.
        const contextControlsWidth = 300;
        // Subtracting half the width of the context control frame and adding half the width of the control Frame
        // centers it. The width of the context controls is scaled by its own transform (which we set
        // to match the one that applies to the control frame) so we need to scale the left offset the same.)
        // The width of the control frame rect is already scaled by the transform.
        const left =
            controlFrameRect.left +
            window.scrollX +
            controlFrameRect.width / 2 -
            (contextControlsWidth / 2) * scale;
        let top = controlFrameRect.top + window.scrollY;
        contextControl.style.visibility = "visible";
        if (controlsAbove) {
            // Bottom 11 px above the top of the control frame.
            if (contextControlRect.height > 0) {
                top -= contextControlRect.height + 11;
            } else {
                // We get a zero height when it is initially hidden. Place it in about the right
                // place so we can measure it and try again once it is (invisibly) rendered.
                top -= 30 + 11;
                contextControl.style.visibility = "hidden";
                setTimeout(() => {
                    this.adjustContextControlPosition(
                        controlFrame,
                        controlsAbove,
                    );
                }, 0);
            }
        } else {
            // Top 11 px below the bottom of the control frame
            top += controlFrameRect.height + 11;
            // exception: if the control frame extends beyond the bottom of the image-container,
            // we want to use the image-container's bottom as our reference point.
            // This can happen with a background image set to bloom-imageObjectFitCover.
            const activeElement = this.host.getActiveElement();
            const bloomCanvasRect = activeElement!
                .closest(kBloomCanvasSelector)!
                .getBoundingClientRect();
            if (controlFrameRect.bottom > bloomCanvasRect.bottom) {
                top = bloomCanvasRect.bottom + 11;
            }
        }
        if (
            controlFrameRect.top === 0 &&
            controlFrameRect.left === 0 &&
            controlFrameRect.width === 0 &&
            controlFrameRect.height === 0
        ) {
            // If the control frame is not visible, let CSS control the placement of the context control.
            contextControl.style.left = "";
            contextControl.style.top = "";
        } else {
            contextControl.style.left = left + "px";
            contextControl.style.top = top + "px";
        }
        // This is constant, so it could be in the CSS. But then it could not share a constant
        // with the computation of left above, so it would be harder to keep things consistent.
        contextControl.style.width = contextControlsWidth + "px";
    }

    public setThingToFocusAfterSettingColor(x: HTMLElement): void {
        this.thingToFocusAfterSettingColor = x;
    }

    public restoreFocus(): void {
        if (this.thingToFocusAfterSettingColor) {
            this.thingToFocusAfterSettingColor.focus();
            // I don't fully understand why we need this, but without it, the input
            // doesn't end up focused. Apparently we just need to overcome whatever
            // is stealing the focus before the next cycle.
            setTimeout(() => {
                this.thingToFocusAfterSettingColor?.focus();
            }, 0);
        }
    }

    private static pxToNumber(value: string, fallback: number = 0): number {
        if (!value) {
            return fallback;
        }
        if (value.endsWith("px")) {
            return parseFloat(value);
        }
        const num = parseFloat(value);
        return Number.isNaN(num) ? fallback : num;
    }
}
