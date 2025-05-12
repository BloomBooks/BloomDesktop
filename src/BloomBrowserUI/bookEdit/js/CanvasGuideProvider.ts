// This class that helps visually align elements during drag operations by showing red lines
// and highlighting elements with equal dimensions during resize operations.

// ALIGNMENT RULES:
// 1. When a dragged element aligns horizontally (top/middle/bottom) or vertically (left/center/right)
//    with another element within a threshold, show a red alignment line.
// 2. Alignment lines should extend through ALL elements that are aligned at the same position.
// 3. Horizontal lines span from the leftmost edge to the rightmost edge of the aligned elements.
// 4. Vertical lines span from the topmost edge to the bottommost edge of the aligned elements.
// 5. Lines should NOT extend beyond the outermost edges of the aligned elements.
// 6. Use a threshold (e.g., 4px) for alignment checks to provide a "snap" feel.
//
// EQUAL DIMENSION RULES (during resize only):
// 1. Identify elements with the same width and/or height as the element being resized (within the threshold).
// 2. If at least one *other* element matches a dimension, display a thicker, semi-transparent line
//    in the middle of the resized element AND all other elements with that matching dimension.
// 3. For elements with the same width, show a horizontal line through their vertical center.
// 4. For elements with the same height, show a vertical line through their horizontal center.

/**
 * Defines the possible alignment points on an element's edges or center.
 */
enum AlignmentPosition {
    Top,
    VerticalCenter,
    Bottom,
    Left,
    HorizontalCenter,
    Right
}

/**
 * Defines the dimension types for equality checks.
 */
enum EqualDimension {
    Width,
    Height
}

/**
 * Stores information about a pre-created alignment line DOM element.
 */
interface AlignmentLine {
    position: AlignmentPosition; // The type of alignment this line represents
    element: HTMLElement; // The div element used as the visual line
}

/**
 * Stores information about a pre-created template for equal dimension indicators.
 */
interface EqualDimensionIndicator {
    type: EqualDimension; // The dimension this indicator represents (Width or Height)
    element: HTMLElement; // The template div element (cloned for each matching element)
}

/**
 * Represents the bounding box and center points of an element.
 * Cached for performance during drag operations.
 */
interface ElementBounds {
    element: HTMLElement;
    rect: DOMRect;
    top: number;
    middle: number; // Vertical center
    bottom: number;
    left: number;
    center: number; // Horizontal center
    right: number;
    width: number;
    height: number;
}

/**
 * Manages visual alignment guides (lines) and equal dimension indicators
 * during drag-and-drop or resize operations.
 */
export class CanvasGuideProvider {
    // --- Configuration ---
    // Max distance in pixels for snapping.
    // Review: does this want to be related to the grid size? A previous ToDo suggested that,  but if things are on
    // the grid, they will be closely aligned or not at all, except possibly for elements positioned using ctrl
    // or before we had the grid. I think it's better not to pretend such things are aligned. We seem to
    // need a non-zero value so that rounding errors don't cause us to miss things that are very close.
    // Maybe 0.5 would be even better?
    private readonly PROXIMITY_THRESHOLD = 1;
    private readonly GUIDE_COLOR = "#E54D2E"; // Typically red/orange
    private readonly EQUAL_DIM_COLOR = "rgba(139, 255, 131, 0.7)"; // Greenish, semi-transparent
    private readonly GUIDE_LINE_THICKNESS = "1px";
    private readonly EQUAL_DIM_LINE_THICKNESS = "3px";
    private readonly GUIDE_LINE_CLASS = "bloom-ui-canvas-guide-line";
    private readonly EQUAL_DIM_INDICATOR_CLASS =
        "bloom-ui-canvas-equal-dimension-indicator";
    private readonly Z_INDEX = "10000"; // Ensure guides appear above most elements

    // --- State ---
    private targetElements: HTMLElement[] = []; // Elements to check alignment against
    private alignmentLines: AlignmentLine[] = []; // Pool of alignment line elements
    private equalDimensionIndicators: EqualDimensionIndicator[] = []; // Pool of template indicator elements
    private dynamicEqualDimElements: HTMLElement[] = []; // Track dynamically created indicator clones
    private currentAction: "resize" | "move" = "move"; // Type of operation

    constructor() {}

    /**
     * Prepares the manager for a new drag operation.
     * @param action The type of operation ("move" or "resize").
     * @param elementsToAlignAgainst An array of HTML elements to check for alignment against the dragged element.
     */
    public startDrag(
        action: "resize" | "move",
        elementsToAlignAgainst: HTMLElement[]
    ): void {
        this.currentAction = action;
        // Filter out any null/undefined elements
        this.targetElements = (elementsToAlignAgainst || []).filter(el => el);
        this.createGuides();
        if (action === "resize") {
            this.createEqualDimensionIndicators(); // Create indicators for resize
        }
        // Ensure guides are hidden initially
        this.hideAllGuides();
    }

    /**
     * Updates alignment guides based on the current position of the dragged element.
     * Should be called repeatedly during a drag operation (e.g., on mousemove).
     * @param draggedElement The HTML element currently being dragged or resized.
     */
    public duringDrag(draggedElement: HTMLElement): void {
        if (!draggedElement || this.targetElements.length === 0) {
            this.hideAllGuides(); // Hide if no element or targets
            return;
        }

        // Always hide previous guides before calculating new ones
        this.hideAllGuides();

        // Pre-calculate bounds for efficiency
        const draggedBounds = this.getElementBounds(draggedElement);
        // Don't include the draggedElement itself
        const filteredTargetElements = this.targetElements.filter(
            el => el !== draggedElement
        );
        const targetBounds = filteredTargetElements.map(el =>
            this.getElementBounds(el)
        );

        // Check and display alignment lines
        this.checkAndShowGuides(draggedBounds, targetBounds);

        // Check and display equal dimension indicators only during resize
        if (this.currentAction === "resize") {
            this.checkAndShowEqualDimensions(draggedBounds, targetBounds);
        }
    }

    /**
     * Cleans up all visual guides and resets the internal state after dragging ends.
     */
    public endDrag(): void {
        this.hideAllGuides();
        this.targetElements = []; // Clear the list of target elements
        // First clean up the dynamic elements that are created during drag operations
        this.dynamicEqualDimElements.forEach(element => {
            if (element && element.parentNode) {
                element.parentNode.removeChild(element);
            }
        });
        this.dynamicEqualDimElements = [];

        // Remove template elements from the DOM
        this.alignmentLines.forEach(line => {
            if (line.element && line.element.parentNode) {
                line.element.parentNode.removeChild(line.element);
            }
        });

        this.equalDimensionIndicators.forEach(indicator => {
            if (indicator.element && indicator.element.parentNode) {
                indicator.element.parentNode.removeChild(indicator.element);
            }
        });

        // Clear all references to help garbage collection
        this.targetElements = [];
        this.alignmentLines = [];
        this.equalDimensionIndicators = [];
    }

    // ==========================================================================
    // Private Helper Methods
    // ==========================================================================

    /**
     * Creates the pool of alignment line DOM elements (initially hidden).
     */
    private createGuides(): void {
        const positions = [
            AlignmentPosition.Top,
            AlignmentPosition.VerticalCenter,
            AlignmentPosition.Bottom,
            AlignmentPosition.Left,
            AlignmentPosition.HorizontalCenter,
            AlignmentPosition.Right
        ];
        if (this.targetElements.length === 0) return; // No elements to align against
        const doc = this.targetElements[0].ownerDocument || document; // make them in the right iframe

        positions.forEach(position => {
            const line = this.createGuideElement(this.GUIDE_LINE_CLASS, doc);
            line.style.backgroundColor = this.GUIDE_COLOR;

            // Set thickness based on orientation
            const isHorizontal =
                position === AlignmentPosition.Top ||
                position === AlignmentPosition.VerticalCenter ||
                position === AlignmentPosition.Bottom;
            if (isHorizontal) {
                line.style.height = this.GUIDE_LINE_THICKNESS;
                line.style.width = "0"; // Width determined later
            } else {
                line.style.width = this.GUIDE_LINE_THICKNESS;
                line.style.height = "0"; // Height determined later
            }

            doc.body.appendChild(line);
            this.alignmentLines.push({ position, element: line });
        });
    }

    /**
     * Creates the template DOM elements for equal dimension indicators (initially hidden).
     */
    private createEqualDimensionIndicators(): void {
        const dimensions = [EqualDimension.Width, EqualDimension.Height];
        if (this.targetElements.length === 0) return; // No elements to align against
        const doc = this.targetElements[0].ownerDocument || document; // make them in the right iframe

        dimensions.forEach(dimension => {
            const indicator = this.createGuideElement(
                this.EQUAL_DIM_INDICATOR_CLASS,
                doc
            );
            indicator.style.backgroundColor = this.EQUAL_DIM_COLOR;

            // Set thickness based on dimension
            if (dimension === EqualDimension.Width) {
                indicator.style.height = this.EQUAL_DIM_LINE_THICKNESS; // Thicker horizontal line
                indicator.style.width = "0";
            } else {
                indicator.style.height = "0";
                indicator.style.width = this.EQUAL_DIM_LINE_THICKNESS; // Thicker vertical line
            }

            doc.body.appendChild(indicator);
            this.equalDimensionIndicators.push({
                type: dimension,
                element: indicator
            });
        });
    }

    /**
     * Creates a standard div element used for visual guides.
     * @param className CSS class name to apply.
     * @returns The created HTMLElement.
     */
    private createGuideElement(className: string, doc: Document): HTMLElement {
        const element = doc.createElement("div");
        element.className = className;
        element.style.position = "absolute";
        element.style.pointerEvents = "none"; // Prevent interference with mouse events
        element.style.display = "none"; // Initially hidden
        element.style.zIndex = this.Z_INDEX;
        element.style.boxSizing = "border-box"; // Ensure border/padding are included if needed
        return element;
    }

    /**
     * Hides all alignment lines and removes dynamic equal dimension indicators.
     */
    private hideAllGuides(): void {
        // Hide the reusable alignment lines
        this.alignmentLines.forEach(line => {
            line.element.style.display = "none";
        });
        // Remove the dynamically created clones for equal dimensions
        this.dynamicEqualDimElements.forEach(element => element.remove());
        this.dynamicEqualDimElements = []; // Clear the tracking array
    }

    /**
     * Calculates and caches the bounding box and center points for an element.
     * @param element The element to measure.
     * @returns An ElementBounds object.

     */
    private getElementBounds(element: HTMLElement): ElementBounds {
        const rect = element.getBoundingClientRect();
        // Adjust for scroll position to get absolute document coordinates
        const scrollX = window.scrollX || window.pageXOffset;
        const scrollY = window.scrollY || window.pageYOffset;

        return {
            element: element,
            rect: rect, // Keep original rect if needed elsewhere
            top: rect.top + scrollY,
            middle: rect.top + scrollY + rect.height / 2,
            bottom: rect.top + scrollY + rect.height,
            left: rect.left + scrollX,
            center: rect.left + scrollX + rect.width / 2,
            right: rect.left + scrollX + rect.width,
            width: rect.width,
            height: rect.height
        };
    }

    /**
     * Checks for alignments between the dragged element and target elements.
     * @param draggedBounds Bounds of the element being dragged.
     * @param targetBounds Array of bounds for the elements to align against.
     */
    private checkAndShowGuides(
        draggedBounds: ElementBounds,
        targetBounds: ElementBounds[]
    ): void {
        const allBounds = [draggedBounds, ...targetBounds];

        // --- Check Horizontal Alignments (Top, Middle, Bottom) ---
        const horizontalPoints: { pos: AlignmentPosition; value: number }[] = [
            { pos: AlignmentPosition.Top, value: draggedBounds.top },
            {
                pos: AlignmentPosition.VerticalCenter,
                value: draggedBounds.middle
            },
            { pos: AlignmentPosition.Bottom, value: draggedBounds.bottom }
        ];

        horizontalPoints.forEach(dragPoint => {
            const alignedElements = this.findAlignedElements(
                dragPoint.value,
                ["top", "middle", "bottom"],
                allBounds
            );
            if (alignedElements.length > 1) {
                // Need dragged element + at least one target
                this.showGuideLine(
                    dragPoint.pos,
                    dragPoint.value,
                    alignedElements
                );
            }
        });

        // --- Check Vertical Alignments (Left, Center, Right) ---
        const verticalPoints: { pos: AlignmentPosition; value: number }[] = [
            { pos: AlignmentPosition.Left, value: draggedBounds.left },
            {
                pos: AlignmentPosition.HorizontalCenter,
                value: draggedBounds.center
            },
            { pos: AlignmentPosition.Right, value: draggedBounds.right }
        ];

        verticalPoints.forEach(dragPoint => {
            const alignedElements = this.findAlignedElements(
                dragPoint.value,
                ["left", "center", "right"],
                allBounds
            );
            if (alignedElements.length > 1) {
                this.showGuideLine(
                    dragPoint.pos,
                    dragPoint.value,
                    alignedElements
                );
            }
        });
    }

    /**
     * Finds all elements (including the dragged one) that align near a specific coordinate.
     * @param coordinate The target coordinate (e.g., draggedBounds.top).
     * @param axesToCheck The axes ('top', 'middle', 'bottom' OR 'left', 'center', 'right') to check on each element.
     * @param allBounds An array containing the bounds of the dragged element and all target elements.
     * @returns An array of ElementBounds for the aligned elements.
     */
    private findAlignedElements(
        coordinate: number,
        axesToCheck: Array<
            "top" | "middle" | "bottom" | "left" | "center" | "right"
        >,
        allBounds: ElementBounds[]
    ): ElementBounds[] {
        const aligned: ElementBounds[] = [];
        let firstMatchCoord = coordinate; // Use the dragged element's coord as the initial target
        let foundMatch = false;

        // First pass: Find the first matching coordinate among all elements
        for (const bounds of allBounds) {
            for (const axis of axesToCheck) {
                const elementCoord = bounds[axis];
                if (
                    Math.abs(coordinate - elementCoord) <=
                    this.PROXIMITY_THRESHOLD
                ) {
                    // Use the first match's coordinate (more efficient)
                    firstMatchCoord = elementCoord;
                    foundMatch = true;
                    break; // Stop at first match for this element
                }
            }
            if (foundMatch) break; // Stop after finding first match overall
        }

        // Second pass: Collect all elements aligning to the found coordinate
        allBounds.forEach(bounds => {
            for (const axis of axesToCheck) {
                const elementCoord = bounds[axis];
                if (
                    Math.abs(firstMatchCoord - elementCoord) <=
                    this.PROXIMITY_THRESHOLD
                ) {
                    aligned.push(bounds);
                    break; // Only add element once per alignment group
                }
            }
        });

        return aligned;
    }

    /**
     * Displays a specific alignment line based on the group of aligned elements.
     * @param position The type of alignment (Top, Middle, Left, etc.).
     * @param coordinate The exact coordinate (y for horizontal, x for vertical) to draw the line at.
     * @param alignedElements An array of ElementBounds for the elements that are aligned.
     */
    private showGuideLine(
        position: AlignmentPosition,
        coordinate: number,
        alignedElements: ElementBounds[]
    ): void {
        const line = this.alignmentLines.find(l => l.position === position)
            ?.element;
        if (!line || alignedElements.length < 2) return; // Need at least two elements to align

        const isHorizontal =
            position === AlignmentPosition.Top ||
            position === AlignmentPosition.VerticalCenter ||
            position === AlignmentPosition.Bottom;

        if (isHorizontal) {
            // Find the min left and max right edges among the aligned elements
            const minLeft = Math.min(...alignedElements.map(b => b.left));
            const maxRight = Math.max(...alignedElements.map(b => b.right));

            line.style.top = `${coordinate -
                parseFloat(this.GUIDE_LINE_THICKNESS) / 2}px`; // Center line thickness
            line.style.left = `${minLeft}px`;
            line.style.width = `${maxRight - minLeft}px`;
            line.style.height = this.GUIDE_LINE_THICKNESS; // Ensure height is set correctly
        } else {
            // Vertical line
            // Find the min top and max bottom edges among the aligned elements
            const minTop = Math.min(...alignedElements.map(b => b.top));
            const maxBottom = Math.max(...alignedElements.map(b => b.bottom));

            line.style.left = `${coordinate -
                parseFloat(this.GUIDE_LINE_THICKNESS) / 2}px`; // Center line thickness
            line.style.top = `${minTop}px`;
            line.style.height = `${maxBottom - minTop}px`;
            line.style.width = this.GUIDE_LINE_THICKNESS; // Ensure width is set correctly
        }

        line.style.display = "block"; // Make the line visible
    }

    /**
     * Checks for elements with dimensions equal to the dragged element during resize.
     * Displays indicators on all matching elements (including the dragged element itself)
     * *if* at least one target element matches the dimension.
     * @param draggedBounds Bounds of the element being resized.
     * @param targetBounds Array of bounds for the potential matching elements.
     */
    private checkAndShowEqualDimensions(
        draggedBounds: ElementBounds,
        targetBounds: ElementBounds[]
    ): void {
        const dragWidth = draggedBounds.width;
        const dragHeight = draggedBounds.height;

        const matchingWidthElements: ElementBounds[] = [];
        const matchingHeightElements: ElementBounds[] = [];

        // Find target elements matching width or height
        targetBounds.forEach(bounds => {
            if (
                Math.abs(dragWidth - bounds.width) <= this.PROXIMITY_THRESHOLD
            ) {
                matchingWidthElements.push(bounds);
            }
            if (
                Math.abs(dragHeight - bounds.height) <= this.PROXIMITY_THRESHOLD
            ) {
                matchingHeightElements.push(bounds);
            }
        });

        // Only show indicators if at least one *other* element matches.
        // If matches were found, add the dragged element itself to the list for visualization.
        if (matchingWidthElements.length > 0) {
            // Add the dragged element ONLY if there are other matches
            matchingWidthElements.push(draggedBounds);
            this.showEqualDimensionIndicators(
                EqualDimension.Width,
                matchingWidthElements
            );
        }
        if (matchingHeightElements.length > 0) {
            // Add the dragged element ONLY if there are other matches
            matchingHeightElements.push(draggedBounds);
            this.showEqualDimensionIndicators(
                EqualDimension.Height,
                matchingHeightElements
            );
        }
    }

    /**
     * Displays the equal dimension indicators for a set of matching elements.
     * Clones the template indicator for each element.
     * @param dimension The dimension type (Width or Height).
     * @param matchingElements Array of ElementBounds with the matching dimension.
     */
    private showEqualDimensionIndicators(
        dimension: EqualDimension,
        matchingElements: ElementBounds[]
    ): void {
        const templateIndicator = this.equalDimensionIndicators.find(
            ind => ind.type === dimension
        )?.element;
        if (!templateIndicator || matchingElements.length === 0) return;

        const lineThickness = parseFloat(this.EQUAL_DIM_LINE_THICKNESS);

        matchingElements.forEach(bounds => {
            // Clone the template indicator for each matching element
            const indicatorClone = templateIndicator.cloneNode(
                true
            ) as HTMLElement;

            if (dimension === EqualDimension.Width) {
                // Horizontal line centered vertically
                indicatorClone.style.left = `${bounds.left}px`;
                indicatorClone.style.top = `${bounds.middle -
                    lineThickness / 2}px`; // Center the line
                indicatorClone.style.width = `${bounds.width}px`;
                indicatorClone.style.height = this.EQUAL_DIM_LINE_THICKNESS; // Ensure height is set
            } else {
                // Height
                // Vertical line centered horizontally
                indicatorClone.style.left = `${bounds.center -
                    lineThickness / 2}px`; // Center the line
                indicatorClone.style.top = `${bounds.top}px`;
                indicatorClone.style.height = `${bounds.height}px`;
                indicatorClone.style.width = this.EQUAL_DIM_LINE_THICKNESS; // Ensure width is set
            }

            indicatorClone.style.display = "block"; // Make visible
            templateIndicator.ownerDocument.body.appendChild(indicatorClone); // Add to DOM
            this.dynamicEqualDimElements.push(indicatorClone); // Track for removal later
        });
    }
}
