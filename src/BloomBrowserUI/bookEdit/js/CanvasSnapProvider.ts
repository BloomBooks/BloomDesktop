/**
 * @fileoverview Manages snapping behavior for elements being dragged on the canvas.
 * Provides functionality for snapping to a grid and locking movement to a single axis (horizontal or vertical).
 */

import { Point, PointScaling } from "./point";

// The size of the grid cells for snapping.
const gridSize = 10;

// Type definition for functions that modify a position based on snapping rules.
type SnapPositionFunction = (
    event: MouseEvent | KeyboardEvent | undefined,
    x: number,
    y: number
) => { x: number; y: number };

/**
 * Stores temporary data related to a drag operation, used by snap functions.
 * This context is reset at the end of each drag, or a client can simply
 * make a new instance for each drag.
 */
interface DragContext {
    startX: number | undefined; // Initial X position when dragging started.
    startY: number | undefined; // Initial Y position when dragging started.
    axis: "horizontal" | "vertical" | undefined; // The determined axis for single-axis snapping (if Shift is held).
}

export class CanvasSnapProvider {
    // Array of functions to apply snapping logic. Order matters.
    private snapFunctions: SnapPositionFunction[] = [];

    // Holds state information during a drag operation.
    private dragContext: DragContext;

    constructor() {
        // Initialize the drag context.
        this.resetDragContext();

        // Define the snapping functions to be applied.
        this.snapFunctions = [
            this.snapToGrid.bind(this), // Apply grid snapping first.
            this.snapToOneAxis.bind(this) // Then apply axis locking if needed.
        ];
    }

    /**
     * Resets the drag context to its initial state.
     * Called when a drag operation starts or ends.
     */
    private resetDragContext(): void {
        this.dragContext = {
            startX: undefined,
            startY: undefined,
            axis: undefined
        };
    }

    /**
     * Called when a drag operation ends, if the snap provider will be reused for other drags.
     * If the snap provider will not be reused, it need not be called. Resets the drag context.
     */
    public endDrag(): void {
        this.resetDragContext();
    }

    /**
     * Calculates the potentially snapped position based on the current mouse event and raw coordinates.
     * Applies registered snap functions unless the CTRL key is pressed.
     * @param event The mouse event triggering the position update.
     * @param x The current raw X coordinate.
     * @param y The current raw Y coordinate.
     * @returns The potentially snapped {x, y} coordinates.
     */
    public getPosition(
        event: MouseEvent | KeyboardEvent | undefined,
        x: number,
        y: number
    ): { x: number; y: number } {
        // Record the starting position on the first call during a drag.
        if (this.dragContext.startX === undefined) {
            this.dragContext.startX = x;
            this.dragContext.startY = y;
        }

        let snappedPosition = { x, y };

        // If CTRL key is pressed, bypass all snapping.
        if (event && event.ctrlKey) {
            return snappedPosition;
        }

        // Apply each registered snap function sequentially.
        for (const snapFunction of this.snapFunctions) {
            snappedPosition = snapFunction(
                event,
                snappedPosition.x,
                snappedPosition.y
            );
        }

        return snappedPosition;
    }

    public getSnappedPoint(
        point: Point,
        event: MouseEvent | KeyboardEvent | undefined
    ): Point {
        // Get the adjusted position based on the current event and point.
        const adjustedPosition = this.getPosition(
            event,
            point.getUnscaledX(),
            point.getUnscaledY()
        );
        return new Point(
            adjustedPosition.x,
            adjustedPosition.y,
            PointScaling.Unscaled,
            "snapped point"
        );
    }

    public getSnappedX(
        x: number,
        event: MouseEvent | KeyboardEvent | undefined
    ): number {
        // Get the adjusted X position based on the current event and X coordinate.
        const adjustedPosition = this.getPosition(event, x, 0);
        return adjustedPosition.x;
    }
    public getSnappedY(
        y: number,
        event: MouseEvent | KeyboardEvent | undefined
    ): number {
        // Get the adjusted Y position based on the current event and Y coordinate.
        const adjustedPosition = this.getPosition(event, 0, y);
        return adjustedPosition.y;
    }

    /**
     * Snaps the given coordinates to the nearest point on the defined grid.
     * @param _event The mouse event (unused in this function but part of the signature).
     * @param x The current X coordinate.
     * @param y The current Y coordinate.
     * @returns The grid-snapped {x, y} coordinates.
     */
    private snapToGrid(
        _event: MouseEvent | KeyboardEvent | undefined,
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
     * The axis (horizontal or vertical) is determined only after the mouse has moved
     * a minimum distance (axisLockThreshold) from the starting point of the drag (or since Shift was pressed).
     * Once determined, movement is locked to that axis until Shift is released.
     * @param event The mouse event, used to check if the Shift key is pressed.
     * @param x The current X coordinate after potentially being snapped by previous functions.
     * @param y The current Y coordinate after potentially being snapped by previous functions.
     * @returns The axis-snapped {x, y} coordinates if Shift is held and axis is locked, otherwise the input {x, y}.
     */
    private snapToOneAxis(
        event: MouseEvent | KeyboardEvent | undefined,
        x: number,
        y: number
    ): { x: number; y: number } {
        // Minimum distance in pixels the mouse must move before locking to an axis.
        const axisLockThreshold = 5;

        if (!event || !event.shiftKey) {
            // If Shift key is not pressed, reset the locked axis (if any) and allow free movement.
            this.dragContext.axis = undefined;
            return { x, y };
        }

        // Shift key IS pressed.

        // If the axis hasn't been determined yet for this Shift press sequence:
        if (this.dragContext.axis === undefined) {
            // Calculate the distance moved from the starting point.
            const xDistance = Math.abs(x - this.dragContext.startX!);
            const yDistance = Math.abs(y - this.dragContext.startY!);

            // Check if the movement exceeds the threshold in either direction.
            if (
                xDistance > axisLockThreshold ||
                yDistance > axisLockThreshold
            ) {
                // Determine the axis based on the larger movement.
                this.dragContext.axis =
                    xDistance > yDistance ? "horizontal" : "vertical";
            } else {
                // If movement is below the threshold, don't lock yet, allow free movement.
                return { x, y };
            }
        }

        // If we reach here, Shift is pressed and the axis has been determined.
        // Apply the axis constraint.
        if (this.dragContext.axis === "horizontal") {
            // Lock to horizontal movement (keep original Y).
            return { x, y: this.dragContext.startY! };
        } else {
            // Lock to vertical movement (keep original X).
            // (this.dragContext.axis === "vertical")
            return { x: this.dragContext.startX!, y };
        }
        // Note: The logic guarantees axis is either "horizontal" or "vertical" here if shift is pressed.
    }

    /**
     * Returns the minimum step size for keyboard movements based on whether CTRL key is pressed.
     * When CTRL is pressed, returns a precision movement (1 pixel).
     * When CTRL is not pressed, returns the grid size for snapping to grid positions.
     * @param event The keyboard event to check for modifier keys
     * @returns The step size in pixels
     */
    public getMinimumStepSize(event: KeyboardEvent): number {
        // If CTRL key is pressed, use precise movement (1 pixel)
        if (event.ctrlKey) {
            return 1;
        }
        // Otherwise, use grid size for snapping
        return gridSize;
    }
}
