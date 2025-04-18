// Manages snapping behavior for elements being dragged on the canvas.
// Provides functionality for snapping to a grid and locking movement to a single axis (horizontal or vertical).
const gridSize = 10;

export class SnapManager {
    private snapFunctions: SnapPositionFunction[] = [];

    // a place functions can store data between calls, reset when drag starts and ends
    private dragContext: {
        startX: number | undefined;
        startY: number | undefined;
        axis: "horizontal" | "vertical" | undefined;
    };
    constructor() {
        this.snapFunctions = [
            this.snapToGrid.bind(this),
            this.snapToOneAxis.bind(this)
        ];
    }
    public startDrag() {
        this.dragContext = {
            startX: undefined,
            startY: undefined,
            axis: undefined
        };
    }
    public endDrag() {
        this.dragContext = {
            startX: undefined,
            startY: undefined,
            axis: undefined
        };
    }
    public getPosition(
        event: MouseEvent,
        x: number,
        y: number
    ): { x: number; y: number } {
        if (this.dragContext.startX === undefined) {
            this.dragContext.startX = x;
            this.dragContext.startY = y;
        }

        let snappedPosition = { x, y };
        // if CTRL is pressed, do not snap
        if (event.ctrlKey) {
            return snappedPosition;
        }
        for (const snapFunction of this.snapFunctions) {
            snappedPosition = snapFunction(
                event,
                snappedPosition.x,
                snappedPosition.y
            );
        }
        return snappedPosition;
    }

    private snapToGrid(
        event: MouseEvent,
        x: number,
        y: number
    ): { x: number; y: number } {
        return {
            x: Math.round(x / gridSize) * gridSize,
            y: Math.round(y / gridSize) * gridSize
        };
    }
    /**
     * Restricts movement to a single axis (horizontal or vertical) when the SHIFT key is held down.
     * The axis is determined only after the movement exceeds the threshold value in either direction.
     * At the start, it uses the previous position to determine which axis to lock, and records
     * the axis of movement in dragMemory. After that, it uses the recorded axis to lock the movement.
     * If the SHIFT key becomes unpressed, it resets the dragMemory and then if SHIFT is pressed again,
     * it uses the previous position to determine which axis to lock again.
     */
    private snapToOneAxis(
        event: MouseEvent,
        x: number,
        y: number
    ): { x: number; y: number } {
        // Minimum distance in pixels before locking to an axis
        const axisLockThreshold = 5;
        if (event.shiftKey) {
            // Calculate the distance moved in each direction
            const xDistance = Math.abs(x - this.dragContext.startX!);
            const yDistance = Math.abs(y - this.dragContext.startY!);

            if (this.dragContext.axis === undefined) {
                // Only determine the axis if movement exceeds the threshold in at least one direction
                if (
                    xDistance > axisLockThreshold ||
                    yDistance > axisLockThreshold
                ) {
                    this.dragContext.axis =
                        xDistance > yDistance ? "horizontal" : "vertical";
                    console.log(
                        `x=${x}, y=${y}, startX=${this.dragContext.startX}, startY=${this.dragContext.startY}, XChange=${xDistance}, YChange=${yDistance}`
                    );
                    console.log("axis", this.dragContext.axis);
                } else {
                    // If movement is below threshold, allow free movement
                    return { x, y };
                }
            } // Apply axis constraint if axis is determined
            if (this.dragContext.axis === "horizontal") {
                return { x, y: this.dragContext.startY! };
            } else if (this.dragContext.axis === "vertical") {
                return { x: this.dragContext.startX!, y };
            }
            // Default fallback (should not normally happen)
            return { x, y };
        } else {
            // Reset axis when shift key is released
            if (this.dragContext.axis !== undefined) {
                this.dragContext.axis = undefined;
            }
            return { x, y };
        }
    }
}

type SnapPositionFunction = (
    event: MouseEvent,
    x: number,
    y: number
) => { x: number; y: number };
