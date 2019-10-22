import { Tail } from "./tail";
import { Point, Layer, Path, Color } from "paper";
import { TailSpec } from "bubbleSpec";
import Bubble from "bubble";

//  straight tail is a simple triangle, with only the tip handle
export class StraightTail extends Tail {
    public constructor(
        root: Point,
        tip: Point,
        lowerLayer: Layer,
        upperLayer: Layer,
        handleLayer: Layer,
        spec: TailSpec,
        bubble: Bubble | undefined
    ) {
        super(root, tip, lowerLayer, upperLayer, handleLayer, spec, bubble);
    }

    // Make the shapes that implement the tail.
    // If there are existing shapes (typically representing an earlier tail position),
    // remove them after putting the new shapes in the same z-order and layer.
    public makeShapes() {
        const oldFill = this.pathFill;
        const oldStroke = this.pathstroke;

        this.lowerLayer.activate();

        const tailWidth = 12;

        // We want to make two lines from the tip to a bit either side
        // of the root.

        // we want to make the base of the tail a line of length tailWidth
        // at right angles to the line from root to tip
        // centered at root.
        const angleBase = new Point(
            this.tip.x! - this.root.x!,
            this.tip.y! - this.root.y!
        ).angle!;
        const deltaBase = new Point(0, 0);
        deltaBase.angle = angleBase + 90;
        deltaBase.length = tailWidth / 2;
        const begin = this.root.add(deltaBase);
        const end = this.root.subtract(deltaBase);

        this.pathstroke = new Path.Line(begin, this.tip);
        const pathLine2 = new Path.Line(this.tip, end);
        this.pathstroke.addSegments(pathLine2.segments!);
        pathLine2.remove();
        if (oldStroke) {
            this.pathstroke.insertBelow(oldStroke);
            oldStroke.remove();
        }
        this.upperLayer.activate();
        this.pathFill = this.pathstroke.clone({ insert: false }) as Path;
        if (oldFill) {
            this.pathFill.insertAbove(oldFill);
            oldFill.remove();
        } else {
            this.upperLayer.addChild(this.pathFill);
        }
        this.pathstroke.strokeColor = new Color("black");
        this.pathFill.fillColor = this.getFillColor();
        if (this.clickAction) {
            this.pathFill.onClick = this.clickAction;
        }
    }
}
