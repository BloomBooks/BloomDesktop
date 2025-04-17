// This file exposes some utility functions that are needed in both iframes. The idea is
// to make them available to import with a minimum of dependencies.

import { getEditablePageBundleExports } from "../../editViewFrame";
import { CanvasElementManager } from "../../js/CanvasElementManager";

export const kCanvasElementClass = "bloom-canvas-element";
export const kCanvasElementSelector = `.${kCanvasElementClass}`;
export const kHasCanvasElementClass = "bloom-has-canvas-element";

// Grid size for snapping objects in games
export const GRID_SIZE = 40;

/**
 * Snaps a value to the nearest grid line
 * @param value The value to snap
 * @param gridSize The size of the grid
 * @returns The snapped value
 */
export function snapValueToGrid(
    value: number,
    gridSize: number = GRID_SIZE
): number {
    return Math.round(value / gridSize) * gridSize;
}

/**
 * Snaps a position (x, y) to the nearest grid intersection
 * @param x The x coordinate
 * @param y The y coordinate
 * @param gridSize The size of the grid
 * @returns The snapped position
 */
export function snapPositionToGrid(
    x: number,
    y: number,
    gridSize: number = GRID_SIZE
): { x: number; y: number } {
    return {
        x: snapValueToGrid(x, gridSize),
        y: snapValueToGrid(y, gridSize)
    };
}

/**
 * Snaps dimensions (width, height) to the nearest grid size, ensuring minimum size
 * @param width The width to snap
 * @param height The height to snap
 * @param gridSize The size of the grid
 * @returns The snapped dimensions
 */
export function snapDimensionsToGrid(
    width: number,
    height: number,
    gridSize: number = GRID_SIZE
): { width: number; height: number } {
    // Ensure minimum size of one grid cell
    const snappedWidth = Math.max(snapValueToGrid(width, gridSize), gridSize);
    const snappedHeight = Math.max(snapValueToGrid(height, gridSize), gridSize);

    return {
        width: snappedWidth,
        height: snappedHeight
    };
}

// Enhance: we could reduce cross-bundle dependencies by separately defining the CanvasElementManager interface
// and just importing that here.
export function getCanvasElementManager(): CanvasElementManager | undefined {
    const editablePageBundleExports = getEditablePageBundleExports();
    return editablePageBundleExports
        ? editablePageBundleExports.getTheOneCanvasElementManager()
        : undefined;
}

/**
 * Snaps the bounds (position and dimensions) of a rectangle to the grid.
 * If ctrlPressed is true, snapping is bypassed.
 * @param rect The rectangle bounds { x, y, width, height }
 * @param ctrlPressed Whether the CTRL key is pressed (bypasses snapping)
 * @param gridSize The size of the grid
 * @returns The potentially snapped bounds
 */
export function snapBoundsToGrid(
    rect: { x: number; y: number; width: number; height: number },
    ctrlPressed: boolean,
    gridSize: number = GRID_SIZE
): { x: number; y: number; width: number; height: number } {
    if (ctrlPressed) {
        return rect; // Bypass snapping if CTRL is pressed
    }

    const snappedPosition = snapPositionToGrid(rect.x, rect.y, gridSize);
    const snappedDimensions = snapDimensionsToGrid(
        rect.width,
        rect.height,
        gridSize
    );

    return {
        ...snappedPosition,
        ...snappedDimensions
    };
}
