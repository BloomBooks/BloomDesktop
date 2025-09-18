import { EditableDivUtils } from "./editableDivUtils";

// This Enum is here so that consumers of this class can give a more descriptive value to the isScaled parameter
// of the constructor other than "true" or "false", which don't say much
// I structured this so that it can basically be synonymous with a boolean called "isScaling"
export enum PointScaling {
    Unscaled,
    Scaled,
}

// Represents a point, and handles ensuring the scaling units remain consistent
// Basically, I do this by forcing the caller to determine whether it is scaled or not at creation
// From there, we internally represent everything as unscaled. Then we can do all our operations on the unscaled point.
// When you want to get the x/y values back out, you can choose to get it out using getUnscaledX/Y() or getScaledX/Y() depending on your needs
// For example: suppose the page is scaled to 120%.
// Then, to get the top left of a box that is visually 120 pixels from the left of its container
// and 240 from the top, you can do either
// new Point (box.offsetLeft, box.offsetTop, PointScaling.Unscaled, "whatever")
// (since offsetX methods are unscaled), or
// new Point (box.getBoundingClientRect().left, box.getBoundingClientRect().top, Point.Scaled, "whatever")
// (since getBoundingClientRect resurns values that are scaled, that is, they are affected by containing
// transform:scale settings).
// Both will result in a Point with x = 100, y = 200.
export class Point {
    // These are internally represented as unscaled units
    private x: number;
    private y: number;

    // This is just a description that might be of use to help keep things clear during debugging
    public comment: string;

    public constructor(
        x: number,
        y: number,
        isScaled: PointScaling,
        comment: string,
    ) {
        if (isScaled) {
            const scale = Point.getScalingFactor();
            x /= scale;
            y /= scale;
        }

        this.x = x;
        this.y = y;

        this.comment = comment;
    }

    public clone(): Point {
        return new Point(this.x, this.y, PointScaling.Unscaled, this.comment);
    }

    public static getScalingFactor(): number {
        return EditableDivUtils.getPageScale();
    }

    public getScaledX(): number {
        return this.x * Point.getScalingFactor();
    }

    public getScaledY(): number {
        return this.y * Point.getScalingFactor();
    }

    public getUnscaledX(): number {
        return this.x;
    }

    public getUnscaledY(): number {
        return this.y;
    }

    public toString(): string {
        return `Point(${this.x}, ${this.y}) (${this.comment})`;
    }

    public length(): number {
        return Math.sqrt(this.x * this.x + this.y * this.y);
    }

    // Returns the result of "this" + "other" in vector arithmetic
    public add(other: Point): Point {
        return new Point(
            this.getUnscaledX() + other.getUnscaledX(),
            this.getUnscaledY() + other.getUnscaledY(),
            PointScaling.Unscaled,
            "Addition result",
        );
    }

    // Returns the result of "this" - "other" in vector arithmetic
    public subtract(other: Point): Point {
        return new Point(
            this.getUnscaledX() - other.getUnscaledX(),
            this.getUnscaledY() - other.getUnscaledY(),
            PointScaling.Unscaled,
            "Subtraction result",
        );
    }

    // Returns the result of "this" multiplied by a scalar factor
    public multiply(multiplier: number): Point {
        return new Point(
            this.x * multiplier,
            this.y * multiplier,
            PointScaling.Unscaled,
            "Multiplication result",
        );
    }

    // Returns the result of "this" divided by a scalar denominator
    public divide(denominator: number): Point {
        return new Point(
            this.x / denominator,
            this.y / denominator,
            PointScaling.Unscaled,
            "Division result",
        );
    }
}
