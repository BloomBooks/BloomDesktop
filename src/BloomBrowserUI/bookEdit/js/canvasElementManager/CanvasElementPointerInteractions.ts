import { Bubble, Comical } from "comicaljs";
import { Point, PointScaling } from "../point";
import { renderCanvasElementContextControls } from "./CanvasElementContextControls";
import { handlePlayClick } from "../bloomVideo";
import {
    kBackgroundImageClass,
    kBloomCanvasSelector,
    kCanvasElementSelector,
} from "../../toolbox/canvas/canvasElementConstants";
import { CanvasGuideProvider } from "./CanvasGuideProvider";
import { CanvasSnapProvider } from "./CanvasSnapProvider";
import { convertPointFromViewportToElementFrame } from "./CanvasElementGeometry";
import { inPlayMode } from "./CanvasElementPositioning";

export interface ICanvasElementPointerInteractionsHost {
    getActiveElement: () => HTMLElement | undefined;
    setActiveElement: (element: HTMLElement | undefined) => void;

    getCanvasElementWeAreTextEditing: () => HTMLElement | undefined;
    setCanvasElementWeAreTextEditing: (
        element: HTMLElement | undefined,
    ) => void;

    isPictureCanvasElement: (canvasElement: HTMLElement) => boolean;
    duplicateCanvasElementBox: (
        canvasElement: HTMLElement,
        sameLocation?: boolean,
    ) => HTMLElement | undefined;

    adjustCanvasElementLocation: (
        canvasElement: HTMLElement,
        container: HTMLElement,
        newPosition: Point,
    ) => void;

    startMoving: () => void;
    stopMoving: () => void;

    setLastMoveContainer: (container: HTMLElement) => void;

    resetCropBasis: () => void;
}

export class CanvasElementPointerInteractions {
    private host: ICanvasElementPointerInteractionsHost;
    private guideProvider: CanvasGuideProvider;
    private snapProvider: CanvasSnapProvider;

    private bubbleToDrag: Bubble | undefined;
    private bubbleDragGrabOffset: { x: number; y: number } = { x: 0, y: 0 };

    private activeElementAtMouseDown: HTMLElement | undefined;
    private mouseIsDown = false;
    private clientXAtMouseDown: number;
    private clientYAtMouseDown: number;
    private mouseDownContainer: HTMLElement;
    private gotAMoveWhileMouseDown = false;

    private animationFrame: number;
    private lastMoveEvent: MouseEvent;

    public constructor(
        host: ICanvasElementPointerInteractionsHost,
        snapProvider: CanvasSnapProvider,
        guideProvider: CanvasGuideProvider,
    ) {
        this.host = host;
        this.snapProvider = snapProvider;
        this.guideProvider = guideProvider;
    }

    // Setup event handlers that allow the canvas element to be moved around.
    public setMouseDragHandlers(bloomCanvas: HTMLElement): void {
        // An earlier version of this code set onmousedown to this.onMouseDown, etc.
        // We need to use addEventListener so we can capture.
        // It's unlikely, but I can't rule it out, that a deliberate side effect
        // was to remove some other onmousedown handler. Just in case, clear the fields.
        // I don't think setting these has any effect on handlers done with addEventListener,
        // but just in case, I'm doing this first.
        bloomCanvas.onmousedown = null;
        bloomCanvas.onmousemove = null;
        bloomCanvas.onmouseup = null;

        // We use mousemove effects instead of drag due to concerns that drag effects would make the entire bloom-canvas appear to drag.
        // Instead, with mousemove, we can make only the specific canvas element move around
        // Grabbing these (particularly the move event) in the capture phase allows us to suppress
        // effects of ctrl and alt clicks on the text.
        bloomCanvas.addEventListener("mousedown", this.onMouseDown, {
            capture: true,
        });

        // Canvas elements have their own context menu. Prevent the browser's default
        // context menu from appearing over those elements in regular browsers.
        bloomCanvas.addEventListener("contextmenu", this.onContextMenu, {
            capture: true,
        });

        // I would prefer to add this to document in onMouseDown, but not yet satisfied that all
        // the things it does while hovering are no longer needed.
        bloomCanvas.addEventListener("mousemove", this.onMouseMove, {
            capture: true,
        });

        // mouse up handler is added to document in onMouseDown

        bloomCanvas.onkeypress = (event: Event) => {
            // If the user is typing in a canvas element, make sure automatic shrinking is off.
            // Automatic shrinking while typing might be useful when originally authoring a comic,
            // but it's a nuisance when translating one, as the canvas element is initially empty
            // and shrinks to one line, messing up the whole layout.
            if (!event.target || !(event.target as Element).closest) return;
            const topBox = (event.target as Element).closest(
                kCanvasElementSelector,
            ) as HTMLElement;
            if (!topBox) return;
            topBox.classList.remove("bloom-allowAutoShrink");
        };
    }

    private moveInsertionPointAndFocusTo = (x, y): Range | undefined => {
        type DocumentWithCaret = Document & {
            caretPositionFromPoint?: (
                x: number,
                y: number,
            ) => CaretPosition | null;
            caretRangeFromPoint?: (x: number, y: number) => Range | null;
        };
        const doc = document as DocumentWithCaret;
        const rangeOrCaret = doc.caretPositionFromPoint
            ? doc.caretPositionFromPoint(x, y)
            : doc.caretRangeFromPoint
              ? doc.caretRangeFromPoint(x, y)
              : null;

        if (!rangeOrCaret) {
            return undefined;
        }

        // We really seem to need to handle both possibilities. I had it working with just the
        // code for range, then restarted Bloom and started getting CaretPositions. Maybe a new
        // version of WebView2 got auto-installed? Anyway, now it should handle both.
        let range: Range;
        if ("endContainer" in rangeOrCaret) {
            range = rangeOrCaret;
        } else {
            // Probably a CaretPosition. We need a Range to use with addRange.
            range = document.createRange();
            range.setStart(rangeOrCaret.offsetNode, rangeOrCaret.offset);
            range.setEnd(rangeOrCaret.offsetNode, rangeOrCaret.offset);
        }

        if (range && range.collapse && range?.endContainer?.parentElement) {
            range.collapse(false); // probably not needed?
            range.endContainer.parentElement.focus();
            const setSelection = () => {
                const selection = window.getSelection();
                selection?.removeAllRanges();
                selection?.addRange(range);
            };
            // I have _no_ idea why it is necessary to do this twice, but if we don't, the selection
            // ends up at a more-or-less random position (often something that was recently selected).
            setSelection();
            setSelection();
        }
        return range as Range;
    };

    // MUST be defined this way, rather than as a member function, so that it can
    // be passed directly to addEventListener and still get the correct 'this'.
    public onContextMenu = (event: MouseEvent) => {
        const targetElement =
            event.target instanceof HTMLElement ? event.target : null;
        if (!targetElement || inPlayMode(targetElement)) {
            return;
        }
        if (!targetElement.closest(kCanvasElementSelector)) {
            return;
        }
        event.preventDefault();
        event.stopPropagation();
    };

    // MUST be defined this way, rather than as a member function, so that it can
    // be passed directly to addEventListener and still get the correct 'this'.
    public onMouseDown = (event: MouseEvent) => {
        this.activeElementAtMouseDown = this.host.getActiveElement();
        const bloomCanvas = event.currentTarget as HTMLElement;
        // Let standard clicks on the bloom editable or other UI elements only be processed by that element
        if (this.isMouseEventAlreadyHandled(event)) {
            return;
        }
        this.gotAMoveWhileMouseDown = false;
        this.mouseIsDown = true;
        this.clientXAtMouseDown = event.clientX;
        this.clientYAtMouseDown = event.clientY;
        this.mouseDownContainer = bloomCanvas;

        // Listen on document (capture phase) so we still detect mouseup if the drag
        // ends outside the bloom-canvas element.
        document.addEventListener("mouseup", this.onMouseUp, {
            capture: true,
        });

        const coordinates = this.getPointRelativeToCanvas(event, bloomCanvas);
        if (!coordinates) {
            return;
        }

        const bubble = Comical.getBubbleHit(
            bloomCanvas,
            coordinates.getUnscaledX(),
            coordinates.getUnscaledY(),
            true, // only consider canvas elements with pointer events allowed.
        );
        if (bubble && event.button === 2) {
            // Right mouse button
            if (bubble.content !== this.host.getActiveElement()) {
                this.host.setActiveElement(bubble.content);
            }
            event.preventDefault();
            event.stopPropagation();
            // re-render the toolbox with its menu open at the desired location
            renderCanvasElementContextControls(bubble.content, true, {
                left: event.clientX,
                top: event.clientY,
            });
            return;
        }

        if (
            Comical.isDraggableNear(
                bloomCanvas,
                coordinates.getUnscaledX(),
                coordinates.getUnscaledY(),
            )
        ) {
            // If we're starting to drag something, typically a tail handle, in Comical,
            // don't do any other mouse activity.
            return;
        }

        const startDraggingBubble = (bubbleToStart: Bubble) => {
            // Note: at this point we do NOT want to focus it. Only if we decide in mouse up that we want to text-edit it.
            this.host.setActiveElement(bubbleToStart.content);

            // Possible move action started
            this.bubbleToDrag = bubbleToStart;
            // in case this is somehow left from earlier, we want a fresh start for the new move.
            this.animationFrame = 0;

            this.guideProvider.startDrag(
                "move",
                Array.from(
                    document.querySelectorAll(kCanvasElementSelector),
                ) as HTMLElement[],
            );

            const pointRelativeToViewport = new Point(
                event.clientX,
                event.clientY,
                PointScaling.Scaled,
                "MouseEvent Client (Relative to viewport)",
            );
            const relativePoint = convertPointFromViewportToElementFrame(
                pointRelativeToViewport,
                bubbleToStart.content,
            );
            this.bubbleDragGrabOffset = {
                x: relativePoint.getUnscaledX(),
                y: relativePoint.getUnscaledY(),
            };
        };

        if (bubble) {
            if (
                window.getComputedStyle(bubble.content).pointerEvents === "none"
            ) {
                return;
            }
            if (event.altKey) {
                event.preventDefault();
                event.stopPropagation();
                if (Comical.findRelatives(bubble).length === 0) {
                    this.host.setActiveElement(bubble.content);
                    const newCanvasElement =
                        this.host.duplicateCanvasElementBox(
                            bubble.content,
                            true,
                        );
                    if (!newCanvasElement) return;
                    startDraggingBubble(new Bubble(newCanvasElement));
                    return;
                }
            }

            const canvasElementWeAreEditing =
                this.host.getCanvasElementWeAreTextEditing();
            const clickOnCanvasElementWeAreEditing =
                canvasElementWeAreEditing ===
                    (event.target as HTMLElement)?.closest(
                        kCanvasElementSelector,
                    ) && canvasElementWeAreEditing;
            if (
                event.altKey ||
                event.ctrlKey ||
                !clickOnCanvasElementWeAreEditing
            ) {
                event.preventDefault();
                event.stopPropagation();
            }
            if (bubble.content.classList.contains(kBackgroundImageClass)) {
                this.host.setActiveElement(bubble.content);
                return;
            }
            startDraggingBubble(bubble);
        }
    };

    // MUST be defined this way, rather than as a member function, so that it can
    // be passed directly to addEventListener and still get the correct 'this'.
    public onMouseMove = (event: MouseEvent) => {
        if (inPlayMode(event.currentTarget as HTMLElement)) {
            return;
        }
        if (event.buttons === 0 && this.mouseIsDown) {
            this.onMouseUp(event);
            return;
        }
        this.lastMoveEvent = event;
        const deltaX = event.clientX - this.clientXAtMouseDown;
        const deltaY = event.clientY - this.clientYAtMouseDown;
        if (
            event.buttons === 1 &&
            Math.sqrt(deltaX * deltaX + deltaY * deltaY) > 3
        ) {
            this.gotAMoveWhileMouseDown = true;
            this.host.startMoving();
        }
        if (!this.gotAMoveWhileMouseDown) {
            return;
        }

        const container = event.currentTarget as HTMLElement;

        if (!this.bubbleToDrag) {
            this.handleMouseMoveHover(event, container);
        } else {
            this.handleMouseMoveDragCanvasElement(event, container);
        }
    };

    private handleMouseMoveHover(event: MouseEvent, container: HTMLElement) {
        if (this.isMouseEventAlreadyHandled(event)) {
            return;
        }

        let hoveredBubble = this.getBubbleUnderMouse(event, container);
        const activeElement = this.host.getActiveElement();

        if (hoveredBubble && hoveredBubble.content !== activeElement) {
            if (this.host.isPictureCanvasElement(hoveredBubble.content)) {
                hoveredBubble = null;
            }
        }
    }

    private getBubbleUnderMouse(
        event: MouseEvent,
        container: HTMLElement,
    ): Bubble | null {
        const coordinates = this.getPointRelativeToCanvas(event, container);
        if (!coordinates) {
            return null;
        }

        return (
            Comical.getBubbleHit(
                container,
                coordinates.getUnscaledX(),
                coordinates.getUnscaledY(),
            ) ?? null
        );
    }

    private handleMouseMoveDragCanvasElement(
        event: MouseEvent,
        container: HTMLElement,
    ) {
        if (event.buttons === 0) {
            this.onMouseUp(event);
            return;
        }
        const activeElement = this.host.getActiveElement();
        if (activeElement) {
            const r = activeElement.getBoundingClientRect();
            const bloomCanvas =
                activeElement.parentElement?.closest(kBloomCanvasSelector);
            if (bloomCanvas) {
                const canvas = this.getFirstCanvasForContainer(bloomCanvas);
                if (canvas)
                    canvas.classList.toggle(
                        "moving",
                        event.clientX > r.left &&
                            event.clientX < r.right &&
                            event.clientY > r.top &&
                            event.clientY < r.bottom,
                    );
            }
        }
        this.host.setLastMoveContainer(container);
        container.style.cursor = "move";

        event.preventDefault();
        event.stopPropagation();
        if (this.animationFrame) {
            return;
        }
        this.animationFrame = requestAnimationFrame(() => {
            if (!this.bubbleToDrag) {
                this.animationFrame = 0;
                return;
            }

            const pointRelativeToViewport = new Point(
                event.clientX,
                event.clientY,
                PointScaling.Scaled,
                "MouseEvent Client (Relative to viewport)",
            );
            const bloomCanvas =
                this.bubbleToDrag.content.parentElement?.closest(
                    kBloomCanvasSelector,
                ) as HTMLElement;
            const relativePoint = convertPointFromViewportToElementFrame(
                pointRelativeToViewport,
                bloomCanvas,
            );

            let newPosition = new Point(
                relativePoint.getUnscaledX() - this.bubbleDragGrabOffset.x,
                relativePoint.getUnscaledY() - this.bubbleDragGrabOffset.y,
                PointScaling.Unscaled,
                "Created by handleMouseMoveDragCanvasElement()",
            );

            const p = this.snapProvider.getPosition(
                event,
                newPosition.getUnscaledX(),
                newPosition.getUnscaledY(),
            );
            newPosition = new Point(
                p.x,
                p.y,
                PointScaling.Unscaled,
                "Created by handleMouseMoveDragCanvasElement()",
            );

            this.host.adjustCanvasElementLocation(
                this.bubbleToDrag.content,
                container,
                newPosition,
            );

            this.guideProvider.duringDrag(this.bubbleToDrag.content);
            this.host.resetCropBasis();
            this.animationFrame = 0;
        });
    }

    private onMouseUp = (event: MouseEvent) => {
        this.mouseIsDown = false;
        this.snapProvider.endDrag();
        this.guideProvider.endDrag();
        document.removeEventListener("mouseup", this.onMouseUp, {
            capture: true,
        });
        if (this.mouseDownContainer && inPlayMode(this.mouseDownContainer)) {
            return;
        }
        this.host.stopMoving();
        if (
            !this.gotAMoveWhileMouseDown &&
            (event.target as HTMLElement).closest(".bloom-videoPlayIcon")
        ) {
            handlePlayClick(event, true);
            return;
        }

        if (this.bubbleToDrag) {
            event.preventDefault();
            event.stopPropagation();
        }

        this.bubbleToDrag = undefined;
        this.mouseDownContainer?.classList.remove("grabbing");
        const editable = (event.target as HTMLElement)?.closest(
            ".bloom-editable",
        );
        if (
            editable &&
            editable.closest(kCanvasElementSelector) ===
                this.host.getCanvasElementWeAreTextEditing()
        ) {
            return;
        }
        if (
            !this.gotAMoveWhileMouseDown &&
            editable &&
            this.activeElementAtMouseDown === this.host.getActiveElement()
        ) {
            const canvasElement = (event.target as HTMLElement)?.closest(
                kCanvasElementSelector,
            ) as HTMLElement;
            this.host.setCanvasElementWeAreTextEditing(canvasElement);
            canvasElement?.classList.add("bloom-focusedCanvasElement");
            this.moveInsertionPointAndFocusTo(event.clientX, event.clientY);
        } else {
            event.preventDefault();
            event.stopPropagation();
        }
    };

    private isMouseEventAlreadyHandled(ev: MouseEvent): boolean {
        if (ev.detail === 2) {
            return true;
        }
        const targetElement = ev.target instanceof Element ? ev.target : null;
        if (!targetElement) {
            return false;
        }
        if (inPlayMode(targetElement)) {
            return true;
        }
        if (targetElement.classList.contains("changeImageButton")) {
            return true;
        }
        if (targetElement.classList.contains("bloom-dragHandle")) {
            return true;
        }
        if (
            targetElement.closest("#animationEnd") ||
            targetElement.closest("#animationStart")
        ) {
            return true;
        }
        if (targetElement.classList.contains("ui-resizable-handle")) {
            return true;
        }
        if (targetElement.closest(".bloom-passive-element")) {
            return true;
        }
        if (targetElement.closest("#canvas-element-control-frame")) {
            return true;
        }
        if (targetElement.closest("[data-target-of")) {
            return true;
        }
        if (
            targetElement.closest(".bloom-videoReplayIcon") ||
            targetElement.closest(".bloom-videoPauseIcon")
        ) {
            return true;
        }
        if (ev.ctrlKey || ev.altKey) {
            return false;
        }
        const editable = targetElement.closest(".bloom-editable");
        const editingCanvasElement =
            this.host.getCanvasElementWeAreTextEditing();
        if (
            editable &&
            editingCanvasElement &&
            editingCanvasElement.contains(editable) &&
            ev.button !== 2
        ) {
            return true;
        }
        if (targetElement.closest(".MuiDialog-container")) {
            return true;
        }
        return false;
    }

    private getPointRelativeToCanvas(
        event: MouseEvent,
        container: Element,
    ): Point | undefined {
        const canvas = this.getFirstCanvasForContainer(container);
        if (!canvas) {
            return undefined;
        }

        const pointRelativeToViewport = new Point(
            event.clientX,
            event.clientY,
            PointScaling.Scaled,
            "MouseEvent Client (Relative to viewport)",
        );

        return convertPointFromViewportToElementFrame(
            pointRelativeToViewport,
            canvas,
        );
    }

    private getFirstCanvasForContainer(
        container: Element,
    ): HTMLCanvasElement | undefined {
        const collection = container.getElementsByTagName("canvas");
        if (!collection || collection.length <= 0) {
            return undefined;
        }

        return collection.item(0) as HTMLCanvasElement;
    }
}
