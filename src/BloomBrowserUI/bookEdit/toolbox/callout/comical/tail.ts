import { Path, Point, Color, Layer, ToolEvent } from "paper";
import Comical from "./comical";
import { TailSpec } from "bubbleSpec";
import Bubble from "./bubble";

// This is an abstract base class for tails. A concrete class must at least
// override makeShapes; if it has additional control points, it will probably
// override showHandles, adjustForChangedRoot(), and adjustForChangedTip().
export class Tail {
    // the path representing the line around the tail
    pathstroke: Path;
    // the path representing the space within the tail
    pathFill: Path;

    public debugMode: boolean;

    lowerLayer: Layer;
    upperLayer: Layer;
    handleLayer: Layer;

    root: Point;
    tip: Point;
    spec: TailSpec;
    bubble: Bubble | undefined;
    clickAction: () => void;
    state: string; // various values used during handle drag

    public constructor(
        root: Point,
        tip: Point,
        lowerLayer: Layer,
        upperLayer: Layer,
        handleLayer: Layer,
        spec: TailSpec,
        bubble: Bubble | undefined
    ) {
        this.lowerLayer = lowerLayer;
        this.upperLayer = upperLayer;
        this.handleLayer = handleLayer;
        this.spec = spec;

        this.root = root;
        this.tip = tip;
        this.bubble = bubble;
    }

    getFillColor(): Color {
        if (this.debugMode) {
            return new Color("yellow");
        }
        if (this.bubble) {
            return this.bubble.getBackgroundColor();
        }
        return Comical.backColor;
    }

    // Make the shapes that implement the tail.
    // If there are existing shapes (typically representing an earlier tail position),
    // remove them after putting the new shapes in the same z-order and layer.
    public makeShapes() {
        throw new Error("Each subclass must implement makeShapes");
    }

    public onClick(action: () => void) {
        this.clickAction = action;
        if (this.pathFill) {
            this.pathFill.onClick = action;
        }
    }

    adjustForChangedRoot(delta: Point) {
        // a hook for subclasses to adjust anything AFTER the root has moved distance delta.
        // Called from inside adjustRoot, which takes care of calling makeShapes() and
        // persistSpecChanges() AFTER calling this.
    }

    adjustRoot(newRoot: Point): void {
        const delta = newRoot.subtract(this.root!);
        if (Math.abs(delta.x!) + Math.abs(delta.y!) < 0.0001) {
            // hasn't moved; very likely adjustSize triggered by an irrelevant change to object;
            // We MUST NOT trigger the mutation observer again, or we get an infinte loop that
            // freezes the whole page.
            return;
        }
        this.root = newRoot;
        this.adjustForChangedRoot(delta);
        this.makeShapes();
        this.persistSpecChanges();
    }

    adjustForChangedTip(delta: Point) {
        // a hook for subclasses to adjust anything AFTER the tip has moved distance delta.
        // Called from inside adjustTip, which takes care of calling makeShapes() and
        // persistSpecChanges() AFTER calling this.
    }

    adjustTip(newTip: Point): void {
        const delta = newTip.subtract(this.tip!);
        if (Math.abs(delta.x!) + Math.abs(delta.y!) < 0.0001) {
            // hasn't moved; very likely adjustSize triggered by an irrelevant change to object;
            // We MUST NOT trigger the mutation observer again, or we get an infinte loop that
            // freezes the whole page.
            return;
        }
        this.tip = newTip;
        this.adjustForChangedTip(delta);
        this.makeShapes();
        if (this.spec) {
            this.spec.tipX = this.tip.x!;
            this.spec.tipY = this.tip.y!;
        }
        this.persistSpecChanges();
    }

    // Erases the tail from the canvas
    remove() {
        this.pathFill.remove();
        this.pathstroke.remove();
    }

    currentStartPoint(): Point {
        if (this.bubble) {
            return this.bubble.calculateTailStartPoint();
        }
        return this.root;
    }

    public showHandles() {
        // Setup event handlers
        this.state = "idle";

        let tipHandle: Path.Circle | undefined;

        if (!this.spec.joiner) {
            // usual case...we want a handle for the tip.
            const isHandleSolid = false;
            tipHandle = this.makeHandle(this.tip, isHandleSolid);
            tipHandle.onMouseDown = () => {
                this.state = "dragTip";
            };
            tipHandle.onMouseUp = () => {
                this.state = "idle";
            };
            tipHandle.onMouseDrag = (event: ToolEvent) => {
                if (this.state !== "dragTip") {
                    return;
                }
                // tipHandle can't be undefined at this point
                const delta = event
                    .point!.subtract(tipHandle!.position!)
                    .divide(2);
                tipHandle!.position = event.point;
                this.tip = event.point!;
                this.adjustForChangedTip(delta);
                this.makeShapes();

                // Update this.spec.tips to reflect the new handle positions
                this.spec.tipX = this.tip.x!;
                this.spec.tipY = this.tip.y!;
                this.persistSpecChanges();
            };
        }
    }

    persistSpecChanges() {
        if (this.bubble) {
            this.bubble.persistBubbleSpecWithoutMonitoring();
        }
    }

    // Helps determine unique names for the handles
    static handleIndex = 0;

    makeHandle(tip: Point, solid: boolean): Path.Circle {
        this.handleLayer.activate();
        const result = new Path.Circle(tip, 5);
        result.strokeColor = new Color("#1d94a4");
        result.fillColor = new Color("#1d94a4"); // a distinct instance of Color, may get made transparent below
        result.strokeWidth = 1;
        // We basically want non-solid bubbles transparent, especially for the tip, so
        // you can see where the tip actually ends up. But if it's perfectly transparent,
        // paper.js doesn't register hit tests on the transparent part. So go for a very
        // small alpha.
        if (!solid) {
            result.fillColor.alpha = 0.01;
        }
        result.name = "handle" + Tail.handleIndex++;
        return result;
    }
}
