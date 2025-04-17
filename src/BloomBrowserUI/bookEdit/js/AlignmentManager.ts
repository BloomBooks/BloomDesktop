// AlignmentManager.ts
// A class that helps visually align elements during drag operations by showing red lines

/**
 * Represents an alignment position (horizontal or vertical)
 */
enum AlignmentPosition {
    Top,
    Middle,
    Bottom,
    Left,
    Center,
    Right
}

/**
 * Represents an alignment line with its position and DOM element
 */
interface AlignmentLine {
    position: AlignmentPosition;
    element: HTMLElement;
}

/**
 * Manages visual alignment guides during drag operations
 */
export class AlignmentManager {
    private elements: HTMLElement[] = [];
    private alignmentLines: AlignmentLine[] = [];
    private readonly ALIGNMENT_THRESHOLD = 1;

    constructor() {
        this.createAlignmentLines();
    }

    /**
     * Set the elements to align with during a drag operation
     * @param elements Array of HTML elements to align with
     */
    public startDrag(elements: HTMLElement[]): void {
        this.elements = elements || [];
        this.hideAllAlignmentLines();
    }

    /**
     * Show alignment lines during drag operations
     * @param element The element being dragged
     */
    public duringDrag(element: HTMLElement): void {
        if (!element || this.elements.length === 0) {
            return;
        }

        // Hide all lines first
        this.hideAllAlignmentLines();

        // Check for horizontal alignments (top, middle, bottom)
        this.checkHorizontalAlignment(element);

        // Check for vertical alignments (left, center, right)
        this.checkVerticalAlignment(element);
    }

    /**
     * Clean up after dragging is complete
     */
    public endDrag(): void {
        this.hideAllAlignmentLines();
        this.elements = [];
    }

    /**
     * Dispose of resources
     */
    public dispose(): void {
        this.hideAllAlignmentLines();
        this.elements = [];

        // Remove all alignment lines from the DOM
        this.alignmentLines.forEach(line => {
            if (line.element.parentNode) {
                line.element.parentNode.removeChild(line.element);
            }
        });

        this.alignmentLines = [];
    }

    /**
     * Create the alignment line DOM elements
     * @private
     */
    private createAlignmentLines(): void {
        const positions = [
            AlignmentPosition.Top,
            AlignmentPosition.Middle,
            AlignmentPosition.Bottom,
            AlignmentPosition.Left,
            AlignmentPosition.Center,
            AlignmentPosition.Right
        ];

        positions.forEach(position => {
            const line = document.createElement("div");
            line.className = "alignment-line";
            line.style.position = "absolute";
            line.style.backgroundColor = "red";
            line.style.pointerEvents = "none"; // Make sure it doesn't interfere with mouse events

            // Set appropriate styles based on position
            if (
                position === AlignmentPosition.Top ||
                position === AlignmentPosition.Middle ||
                position === AlignmentPosition.Bottom
            ) {
                // Horizontal line
                line.style.height = "1px";
            } else {
                // Vertical line
                line.style.width = "1px";
            }

            // Initially hide the line
            line.style.display = "none";

            // Add to document body
            document.body.appendChild(line);

            // Store the alignment line
            this.alignmentLines.push({
                position,
                element: line
            });
        });
    }

    /**
     * Hide all alignment lines
     * @private
     */
    private hideAllAlignmentLines(): void {
        this.alignmentLines.forEach(line => {
            line.element.style.display = "none";
        });
    }

    /**
     * Check for horizontal alignment (top, middle, bottom)
     * @param dragElement Element being dragged
     * @private
     */
    private checkHorizontalAlignment(dragElement: HTMLElement): void {
        const dragRect = dragElement.getBoundingClientRect();
        const dragTop = dragRect.top;
        const dragMiddle = dragTop + dragRect.height / 2;
        const dragBottom = dragTop + dragRect.height;
        const dragLeft = dragRect.left;
        const dragRight = dragRect.right;

        // Track which alignments we've detected to avoid duplicates
        const detectedAlignments = {
            [AlignmentPosition.Top]: false,
            [AlignmentPosition.Middle]: false,
            [AlignmentPosition.Bottom]: false
        };

        // Check alignment with each element
        this.elements.forEach(element => {
            if (element === dragElement) return;

            const elementRect = element.getBoundingClientRect();
            const elementTop = elementRect.top;
            const elementMiddle = elementTop + elementRect.height / 2;
            const elementBottom = elementTop + elementRect.height;
            const elementLeft = elementRect.left;
            const elementRight = elementRect.right;

            // Check top alignment
            if (Math.abs(dragTop - elementTop) <= this.ALIGNMENT_THRESHOLD) {
                this.showAlignmentLine(
                    AlignmentPosition.Top,
                    elementTop,
                    dragLeft,
                    dragRight,
                    elementLeft
                );
                detectedAlignments[AlignmentPosition.Top] = true;
            }

            // Check top to middle alignment
            if (Math.abs(dragTop - elementMiddle) <= this.ALIGNMENT_THRESHOLD) {
                this.showAlignmentLine(
                    AlignmentPosition.Top,
                    elementMiddle,
                    dragLeft,
                    dragRight,
                    elementLeft
                );
                detectedAlignments[AlignmentPosition.Top] = true;
            }

            // Check top to bottom alignment
            if (Math.abs(dragTop - elementBottom) <= this.ALIGNMENT_THRESHOLD) {
                this.showAlignmentLine(
                    AlignmentPosition.Top,
                    elementBottom,
                    dragLeft,
                    dragRight,
                    elementLeft
                );
                detectedAlignments[AlignmentPosition.Top] = true;
            }

            // Check middle alignment
            if (Math.abs(dragMiddle - elementTop) <= this.ALIGNMENT_THRESHOLD) {
                this.showAlignmentLine(
                    AlignmentPosition.Middle,
                    elementTop,
                    dragLeft,
                    dragRight,
                    elementLeft
                );
                detectedAlignments[AlignmentPosition.Middle] = true;
            }

            if (
                Math.abs(dragMiddle - elementMiddle) <= this.ALIGNMENT_THRESHOLD
            ) {
                this.showAlignmentLine(
                    AlignmentPosition.Middle,
                    elementMiddle,
                    dragLeft,
                    dragRight,
                    elementLeft
                );
                detectedAlignments[AlignmentPosition.Middle] = true;
            }

            if (
                Math.abs(dragMiddle - elementBottom) <= this.ALIGNMENT_THRESHOLD
            ) {
                this.showAlignmentLine(
                    AlignmentPosition.Middle,
                    elementBottom,
                    dragLeft,
                    dragRight,
                    elementLeft
                );
                detectedAlignments[AlignmentPosition.Middle] = true;
            }

            // Check bottom alignment
            if (Math.abs(dragBottom - elementTop) <= this.ALIGNMENT_THRESHOLD) {
                this.showAlignmentLine(
                    AlignmentPosition.Bottom,
                    elementTop,
                    dragLeft,
                    dragRight,
                    elementLeft
                );
                detectedAlignments[AlignmentPosition.Bottom] = true;
            }

            if (
                Math.abs(dragBottom - elementMiddle) <= this.ALIGNMENT_THRESHOLD
            ) {
                this.showAlignmentLine(
                    AlignmentPosition.Bottom,
                    elementMiddle,
                    dragLeft,
                    dragRight,
                    elementLeft
                );
                detectedAlignments[AlignmentPosition.Bottom] = true;
            }

            if (
                Math.abs(dragBottom - elementBottom) <= this.ALIGNMENT_THRESHOLD
            ) {
                this.showAlignmentLine(
                    AlignmentPosition.Bottom,
                    elementBottom,
                    dragLeft,
                    dragRight,
                    elementLeft
                );
                detectedAlignments[AlignmentPosition.Bottom] = true;
            }
        });
    }

    /**
     * Check for vertical alignment (left, center, right)
     * @param dragElement Element being dragged
     * @private
     */
    private checkVerticalAlignment(dragElement: HTMLElement): void {
        const dragRect = dragElement.getBoundingClientRect();
        const dragLeft = dragRect.left;
        const dragCenter = dragLeft + dragRect.width / 2;
        const dragRight = dragRect.left + dragRect.width;
        const dragTop = dragRect.top;
        const dragBottom = dragRect.bottom;

        // Track which alignments we've detected to avoid duplicates
        const detectedAlignments = {
            [AlignmentPosition.Left]: false,
            [AlignmentPosition.Center]: false,
            [AlignmentPosition.Right]: false
        };

        // Check alignment with each element
        this.elements.forEach(element => {
            if (element === dragElement) return;

            const elementRect = element.getBoundingClientRect();
            const elementLeft = elementRect.left;
            const elementCenter = elementLeft + elementRect.width / 2;
            const elementRight = elementLeft + elementRect.width;
            const elementTop = elementRect.top;

            // Check left alignment
            if (Math.abs(dragLeft - elementLeft) <= this.ALIGNMENT_THRESHOLD) {
                this.showAlignmentLine(
                    AlignmentPosition.Left,
                    elementLeft,
                    dragTop,
                    dragBottom,
                    elementTop
                );
                detectedAlignments[AlignmentPosition.Left] = true;
            }

            // Check left to center alignment
            if (
                Math.abs(dragLeft - elementCenter) <= this.ALIGNMENT_THRESHOLD
            ) {
                this.showAlignmentLine(
                    AlignmentPosition.Left,
                    elementCenter,
                    dragTop,
                    dragBottom,
                    elementTop
                );
                detectedAlignments[AlignmentPosition.Left] = true;
            }

            // Check left to right alignment
            if (Math.abs(dragLeft - elementRight) <= this.ALIGNMENT_THRESHOLD) {
                this.showAlignmentLine(
                    AlignmentPosition.Left,
                    elementRight,
                    dragTop,
                    dragBottom,
                    elementTop
                );
                detectedAlignments[AlignmentPosition.Left] = true;
            }

            // Check center alignment
            if (
                Math.abs(dragCenter - elementLeft) <= this.ALIGNMENT_THRESHOLD
            ) {
                this.showAlignmentLine(
                    AlignmentPosition.Center,
                    elementLeft,
                    dragTop,
                    dragBottom,
                    elementTop
                );
                detectedAlignments[AlignmentPosition.Center] = true;
            }

            if (
                Math.abs(dragCenter - elementCenter) <= this.ALIGNMENT_THRESHOLD
            ) {
                this.showAlignmentLine(
                    AlignmentPosition.Center,
                    elementCenter,
                    dragTop,
                    dragBottom,
                    elementTop
                );
                detectedAlignments[AlignmentPosition.Center] = true;
            }

            if (
                Math.abs(dragCenter - elementRight) <= this.ALIGNMENT_THRESHOLD
            ) {
                this.showAlignmentLine(
                    AlignmentPosition.Center,
                    elementRight,
                    dragTop,
                    dragBottom,
                    elementTop
                );
                detectedAlignments[AlignmentPosition.Center] = true;
            }

            // Check right alignment
            if (Math.abs(dragRight - elementLeft) <= this.ALIGNMENT_THRESHOLD) {
                this.showAlignmentLine(
                    AlignmentPosition.Right,
                    elementLeft,
                    dragTop,
                    dragBottom,
                    elementTop
                );
                detectedAlignments[AlignmentPosition.Right] = true;
            }

            if (
                Math.abs(dragRight - elementCenter) <= this.ALIGNMENT_THRESHOLD
            ) {
                this.showAlignmentLine(
                    AlignmentPosition.Right,
                    elementCenter,
                    dragTop,
                    dragBottom,
                    elementTop
                );
                detectedAlignments[AlignmentPosition.Right] = true;
            }

            if (
                Math.abs(dragRight - elementRight) <= this.ALIGNMENT_THRESHOLD
            ) {
                this.showAlignmentLine(
                    AlignmentPosition.Right,
                    elementRight,
                    dragTop,
                    dragBottom,
                    elementTop
                );
                detectedAlignments[AlignmentPosition.Right] = true;
            }
        });
    }

    /**
     * Show an alignment line at the given position
     * @param position Alignment position
     * @param coordinate Coordinate for the line
     * @param dragElementStart Drag element's start position (left for horizontal, top for vertical)
     * @param dragElementEnd Drag element's end position (right for horizontal, bottom for vertical)
     * @param alignedElementCoord Aligned element's coordinate that matched
     * @private
     */
    private showAlignmentLine(
        position: AlignmentPosition,
        coordinate: number,
        dragElementStart: number,
        dragElementEnd: number,
        alignedElementCoord: number
    ): void {
        const line = this.alignmentLines.find(l => l.position === position)
            ?.element;
        if (!line) return;

        line.style.display = "block";

        const isHorizontal =
            position === AlignmentPosition.Top ||
            position === AlignmentPosition.Middle ||
            position === AlignmentPosition.Bottom;

        if (isHorizontal) {
            // Horizontal line
            line.style.top = `${coordinate}px`;

            // For horizontal lines, extend from the drag element to the aligned element
            const left =
                alignedElementCoord < dragElementStart
                    ? alignedElementCoord
                    : dragElementStart;
            const right =
                alignedElementCoord > dragElementEnd
                    ? alignedElementCoord
                    : dragElementEnd;

            line.style.left = `${left}px`;
            line.style.width = `${right - left}px`;
            line.style.height = "1px"; // 1px thin line
        } else {
            // Vertical line
            line.style.left = `${coordinate}px`;

            // For vertical lines, extend from the drag element to the aligned element
            const top =
                alignedElementCoord < dragElementStart
                    ? alignedElementCoord
                    : dragElementStart;
            const bottom =
                alignedElementCoord > dragElementEnd
                    ? alignedElementCoord
                    : dragElementEnd;

            line.style.top = `${top}px`;
            line.style.height = `${bottom - top}px`;
            line.style.width = "1px"; // 1px thin line
        }
    }
}
