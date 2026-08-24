import { Bubble, Comical } from "comicaljs";
import { Point, PointScaling } from "../point";
import { renderCanvasElementContextControls } from "./CanvasElementContextControls";
import {
    handlePauseClick,
    handlePlayClick,
    handleReplayClick,
} from "../bloomVideo";
import {
    kBackgroundImageClass,
    kBloomCanvasSelector,
    kCanvasElementClass,
    kCanvasElementSelector,
} from "../../toolbox/canvas/canvasElementConstants";
import { CanvasGuideProvider } from "./CanvasGuideProvider";
import { CanvasSnapProvider } from "./CanvasSnapProvider";
import {
    convertPointFromViewportToElementFrame,
    getLeftAndTopBorderWidths,
    getLeftAndTopPaddings,
} from "./CanvasElementGeometry";
import {
    getCanvasElementRotation,
    isPointInsideRotatedCanvasElement,
    kPointerInsideClass,
    kRotatedClass,
} from "./canvasElementRotation";
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

        // While the pointer is inside the bloom-canvas, onMouseMove keeps the mark on the turned
        // element the pointer is inside. No move event tells us it has left, so clear the mark here.
        // This must be the same function object every time, because this method runs again on every
        // page load and addEventListener would otherwise stack up another listener each time.
        bloomCanvas.addEventListener("mouseleave", this.onMouseLeave);

        // We use mousemove effects instead of drag due to concerns that drag effects would make the entire bloom-canvas appear to drag.
        // Instead, with mousemove, we can make only the specific canvas element move around
        // Grabbing these (particularly the move event) in the capture phase allows us to suppress
        // effects of ctrl and alt clicks on the text.
        bloomCanvas.addEventListener("mousedown", this.onMouseDown, {
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

    // Which canvas element the user clicked on, given a point in the coordinates of the
    // bloom-canvas.
    //
    // comicaljs tests each element against its un-rotated offset box, so it reports a miss
    // for a point that is really inside an element the user has turned, and a hit for a point
    // in the part of the box the element has turned away from. We therefore test the turned
    // elements here and leave the rest to comicaljs, then take whichever hit is on top.
    private getCanvasElementHit(
        bloomCanvas: HTMLElement,
        x: number,
        y: number,
    ): Bubble | undefined {
        const unrotatedHit = Comical.getBubbleHit(
            bloomCanvas,
            x,
            y,
            true, // only consider canvas elements with pointer events allowed.
            `.${kRotatedClass}`,
        );
        const rotatedHit = this.getRotatedCanvasElementHit(bloomCanvas, x, y);
        if (!rotatedHit) {
            return unrotatedHit;
        }
        if (!unrotatedHit) {
            return rotatedHit;
        }
        // Level is comicaljs's stacking order; the higher one is in front.
        const rotatedLevel =
            Bubble.getBubbleSpec(rotatedHit.content).level ?? 0;
        const unrotatedLevel =
            Bubble.getBubbleSpec(unrotatedHit.content).level ?? 0;
        return rotatedLevel >= unrotatedLevel ? rotatedHit : unrotatedHit;
    }

    private getRotatedCanvasElementHit(
        bloomCanvas: HTMLElement,
        x: number,
        y: number,
    ): Bubble | undefined {
        let hit: HTMLElement | undefined;
        let hitLevel = Number.NEGATIVE_INFINITY;
        Array.from(bloomCanvas.getElementsByClassName(kRotatedClass)).forEach(
            (element) => {
                if (
                    !(element instanceof HTMLElement) ||
                    !element.classList.contains(kCanvasElementClass)
                ) {
                    return;
                }
                const styles = window.getComputedStyle(element);
                // Match the two things comicaljs filters on: an invisible element is not there,
                // and one that takes no pointer events cannot be clicked.
                if (
                    styles.display === "none" ||
                    styles.pointerEvents === "none" ||
                    !isPointInsideRotatedCanvasElement(element, x, y)
                ) {
                    return;
                }
                const level = Bubble.getBubbleSpec(element).level ?? 0;
                if (level >= hitLevel) {
                    hitLevel = level;
                    hit = element;
                }
            },
        );
        return hit ? new Bubble(hit) : undefined;
    }

    // Where inside the canvas element the user took hold of it. The move code subtracts this
    // from the pointer position to get the element's new position, so it must be measured
    // against the element's own box.
    //
    // For an element the user has turned, getBoundingClientRect reports the box around the
    // turned element instead, which would make the element jump as soon as the drag began. In
    // that case we rebuild the same measurement from offsetLeft/offsetTop, which describe the
    // element's own box whatever its angle. For an element that is not turned the two agree,
    // so nothing changes for the ordinary case.
    private getGrabOffsetPoint(
        pointRelativeToViewport: Point,
        canvasElement: HTMLElement,
    ): Point {
        if (getCanvasElementRotation(canvasElement) === 0) {
            return convertPointFromViewportToElementFrame(
                pointRelativeToViewport,
                canvasElement,
            );
        }
        const bloomCanvas = canvasElement.parentElement?.closest(
            kBloomCanvasSelector,
        ) as HTMLElement;
        const pointInCanvas = convertPointFromViewportToElementFrame(
            pointRelativeToViewport,
            bloomCanvas,
        );
        const borderAndPadding = getLeftAndTopBorderWidths(canvasElement).add(
            getLeftAndTopPaddings(canvasElement),
        );
        return new Point(
            pointInCanvas.getUnscaledX() -
                canvasElement.offsetLeft -
                borderAndPadding.getUnscaledX(),
            pointInCanvas.getUnscaledY() -
                canvasElement.offsetTop -
                borderAndPadding.getUnscaledY(),
            PointScaling.Unscaled,
            "Grab offset within a rotated canvas element",
        );
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

        // We really do need to handle both possibilities, and here is why: a new version of
        // WebView2 had indeed been auto-installed. Chromium added the standard
        // caretPositionFromPoint (which answers a CaretPosition) in version 128; before that only
        // its own caretRangeFromPoint (which answers a Range) existed. Since we prefer the
        // standard one above, WebView2 128 and later take that branch and give us CaretPositions,
        // while everything back to our minimum of 112 (WebView2Browser.kMinimumWebView2Version)
        // falls through to the older call and gives us Ranges. So both branches below are live
        // across the versions we support, and neither can be deleted while 112 is the minimum.
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
    public onMouseDown = (event: MouseEvent) => {
        this.activeElementAtMouseDown = this.host.getActiveElement();
        const bloomCanvas = event.currentTarget as HTMLElement;
        // Let standard clicks on the bloom editable or other UI elements only be processed by that element
        if (this.isMouseEventAlreadyHandled(event)) {
            return;
        }
        this.gotAMoveWhileMouseDown = false;

        const coordinates = this.getPointRelativeToCanvas(event, bloomCanvas);
        if (!coordinates) {
            return;
        }

        const bubble = this.getCanvasElementHit(
            bloomCanvas,
            coordinates.getUnscaledX(),
            coordinates.getUnscaledY(),
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

            this.mouseIsDown = true;
            this.clientXAtMouseDown = event.clientX;
            this.clientYAtMouseDown = event.clientY;
            this.mouseDownContainer = bloomCanvas;
            // Listen on document (capture phase) so we still detect mouseup if the drag
            // ends outside the bloom-canvas element.
            document.addEventListener("mouseup", this.onBubbleDragMouseUp, {
                capture: true,
            });

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
            const relativePoint = this.getGrabOffsetPoint(
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
    public onMouseLeave = (event: MouseEvent) => {
        this.markTurnedElementUnderPointer(
            event.currentTarget as HTMLElement,
            undefined,
        );
    };

    public onMouseMove = (event: MouseEvent) => {
        if (inPlayMode(event.currentTarget as HTMLElement)) {
            return;
        }
        if (event.buttons === 0 && this.mouseIsDown) {
            this.onBubbleDragMouseUp(event);
            return;
        }
        if (event.buttons === 0) {
            // Not a drag, so keep the mark on the turned element the pointer is inside. This has
            // to come before the bubbleToDrag test, because a mouse-up on a video's play button
            // returns while that field is still set, which would leave the mark behind.
            this.updateTurnedElementUnderPointer(event);
        }
        if (!this.bubbleToDrag) {
            return;
        }
        const deltaX = event.clientX - this.clientXAtMouseDown;
        const deltaY = event.clientY - this.clientYAtMouseDown;
        if (
            event.buttons === 1 &&
            !this.gotAMoveWhileMouseDown &&
            Math.sqrt(deltaX * deltaX + deltaY * deltaY) > 3
        ) {
            this.gotAMoveWhileMouseDown = true;
            this.host.startMoving();
        }
        if (!this.gotAMoveWhileMouseDown) {
            return;
        }

        const container = event.currentTarget as HTMLElement;
        this.handleMouseMoveDragCanvasElement(event, container);
    };

    // Which turned canvas element the pointer is inside, so that the CSS can do what :hover
    // cannot for a turned element; see kPointerInsideClass. An element the user has not turned
    // gets a real :hover from the browser, so we only have to do this for the turned ones, which
    // also keeps the work small: we walk the turned elements, not every element, and we do no
    // comicaljs hit test.
    private updateTurnedElementUnderPointer(event: MouseEvent) {
        const bloomCanvas = event.currentTarget as HTMLElement;
        if (bloomCanvas.getElementsByClassName(kRotatedClass).length === 0) {
            return;
        }
        const coordinates = this.getPointRelativeToCanvas(event, bloomCanvas);
        if (!coordinates) {
            return;
        }
        const hit = this.getRotatedCanvasElementHit(
            bloomCanvas,
            coordinates.getUnscaledX(),
            coordinates.getUnscaledY(),
        );
        this.markTurnedElementUnderPointer(bloomCanvas, hit?.content);
    }

    // Put the mark on one turned element and take it off the others. Pass undefined to clear it
    // from all of them.
    private markTurnedElementUnderPointer(
        bloomCanvas: HTMLElement,
        element: HTMLElement | undefined,
    ) {
        Array.from(bloomCanvas.getElementsByClassName(kRotatedClass)).forEach(
            (turned) => {
                turned.classList.toggle(
                    kPointerInsideClass,
                    turned === element,
                );
            },
        );
    }

    private handleMouseMoveDragCanvasElement(
        event: MouseEvent,
        container: HTMLElement,
    ) {
        if (event.buttons === 0) {
            this.onBubbleDragMouseUp(event);
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

    // One of a video's three buttons, when the user has clicked it inside a turned canvas
    // element. The obvious test, event.target, works only while the button is what the browser
    // hit; inside a turned element the comicaljs canvas is on top and is the target instead, for
    // the reason given at kPointerInsideClass. So we look through everything under the pointer
    // rather than at the topmost thing alone.
    //
    // Two limits keep that from clicking a button the user cannot see. The button must belong to
    // the element the user pressed on, which the ordinary hit test chose, so a button that
    // another element covers is not clicked. And a button that is not displayed is not in the
    // list at all, so a hidden video cannot be started.
    private getVideoButtonInsideTurnedElement(
        event: MouseEvent,
    ): HTMLElement | undefined {
        const pressed = this.bubbleToDrag?.content;
        if (!pressed?.classList.contains(kRotatedClass)) {
            return undefined;
        }
        const doc = pressed.ownerDocument;
        for (const element of doc.elementsFromPoint(
            event.clientX,
            event.clientY,
        )) {
            const button = element.closest(
                ".bloom-videoPlayIcon, .bloom-videoPauseIcon, .bloom-videoReplayIcon",
            );
            if (button instanceof HTMLElement && pressed.contains(button)) {
                return button;
            }
        }
        return undefined;
    }

    private onBubbleDragMouseUp = (event: MouseEvent) => {
        this.mouseIsDown = false;
        this.snapProvider.endDrag();
        this.guideProvider.endDrag();
        document.removeEventListener("mouseup", this.onBubbleDragMouseUp, {
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
        // Inside a turned element the buttons never get the click themselves, so we deliver it.
        // The pause and replay buttons need this as much as play does: isMouseEventAlreadyHandled
        // leaves those two to their own listeners, which the comicaljs canvas stops from ever
        // firing on a turned element.
        const button = this.gotAMoveWhileMouseDown
            ? undefined
            : this.getVideoButtonInsideTurnedElement(event);
        if (button) {
            if (button.classList.contains("bloom-videoPlayIcon")) {
                handlePlayClick(event, true, button);
            } else if (button.classList.contains("bloom-videoPauseIcon")) {
                handlePauseClick(event, button);
            } else {
                handleReplayClick(event, button);
            }
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
        if (targetElement.closest("[data-target-of]")) {
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
