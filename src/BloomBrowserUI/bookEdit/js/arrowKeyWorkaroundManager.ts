// This handler should be in a keydown event, not a keyup event.
// If you use keyup, the selection will already have changed by the time you get the event.
// But we would like to know the selection before pressing the arrow key.
export function fixUpDownArrowEventHandler(keyEvent: KeyboardEvent): void {
    const manager = new ArrowKeyWorkaroundManager();
    manager.handleUpDownArrowsInFlexboxEditables(keyEvent);
}

// This class provides a workaround to a Firefox bug where the up/down arrow keys do not work properly
// if the bloom-editable is a flexbox. The Firefox behavior is that if you are on the boundary line of a paragraph,
// the up/down arrow keys will not cross a paragraph boundary, but only bring you to the start of the paragraph.
// (See bug report: https://bugzilla.mozilla.org/show_bug.cgi?id=1414884)
// This class provides an instance method, handleUpDownArrowsInFlexboxEditables,
// which can be attached to the keydown event to intercept the up/down arrow keys
// If necessary, it will set the cursor to the correct position and then prevent the default event.
// Otherwise, it will allow the default event to proceed and handle the up/down arrow keys the normal way.
export class ArrowKeyWorkaroundManager {
    // Controls whether the this.log() and this.printNode() statements actually print anything to log.
    // These are normally commented out to reduce overhead.
    // If you need to do some serious debugging of this code, you can Find all the this.log and this.printNode statements
    // and uncomment them.
    private debug: boolean = false;

    private direction: "up" | "down";
    public constructor() {}

    // Checks a keyboard event to see if we want to modify the behavior.
    // If so, calls moveAnchor() and stops the event's default behavior.
    // Otherwise, allows the event's default behavior to proceed as normal.
    public handleUpDownArrowsInFlexboxEditables(keyEvent: KeyboardEvent): void {
        // Avoid modifying the keydown behavior by returning early unless it's the specific problem case
        if (keyEvent.key !== "ArrowUp" && keyEvent.key !== "ArrowDown") {
            return;
        }

        // this.log("Handling " + keyEvent.key);

        const targetElem = keyEvent.target as Element;
        if (!targetElem) {
            // this.log("SKIP - Target could not be cast to Element");
            return;
        }

        const style = window.getComputedStyle(targetElem);
        if (style.display !== "flex") {
            // Problem only happens if the bloom-editable is a flexbox.
            // this.log("SKIP - Not a flexbox.");
            return;
        }

        this.direction = keyEvent.key === "ArrowUp" ? "up" : "down";
        this.moveAnchor(keyEvent);
    }

    // Moves the anchor by one line according to this.direction
    private moveAnchor(event: Event): void {
        console.assert(
            this.direction,
            "ArrowKeyWorkaroundManager.direction must be set"
        );
        const sel = window.getSelection();
        if (!sel) {
            // this.log("SKIP - Could not get selection.");
            return;
        }

        let oldAnchor: Anchor;
        if (sel.anchorNode?.nodeType === Node.TEXT_NODE) {
            oldAnchor = new Anchor(sel.anchorNode, sel.anchorOffset);
        } else if (sel.anchorNode?.nodeType === Node.ELEMENT_NODE) {
            // When you first load up a page, it may be pointing to an elementNode instead.
            // (It's also sometimes possible to arrive at an elementNode by using the arrow keys to navigate,
            // e.g. pressing down arrow to the very end was sometimes observed to do the trick)
            // Force it to point to a text node instead.
            if (sel.anchorOffset >= sel.anchorNode.childNodes.length) {
                // this.log("ABORT - anchorOffset > length.");
                return;
            }
            const pointedToNode = sel.anchorNode.childNodes.item(
                sel.anchorOffset
            );
            const firstLeafNode = this.getFirstLeafNode(pointedToNode);
            if (!firstLeafNode) {
                // this.log("ABORT - firstLeafNode could not be found.");
                return;
            } else {
                oldAnchor = new Anchor(firstLeafNode, 0);
            }
        } else {
            // this.log("SKIP - Invalid nodeType: " + sel.anchorNode?.nodeType);
            return;
        }

        // Limit it to text nodes inside paragraphs, for now.
        // Not really clear what should happen if it's not a paragraph,
        // or how we even arrive at that hypothetical state.
        const oldElement = oldAnchor.node.parentElement;
        if (!oldElement) {
            // this.log("SKIP - oldElement was null.");
            return;
        }

        const oldParagraph = oldElement.closest("p");
        if (!oldParagraph) {
            // this.log("SKIP - closestParagraph not found.");
            return;
        }

        const analysis = this.analyzeCurrentPosition(oldParagraph, oldAnchor);
        if (!analysis) {
            // this.log("SKIP - Error analyzing current position.");
            return;
        }

        if (!analysis.isOnBoundary) {
            // this.log(`SKIP - Not on first/last line`);
            return;
        }

        const targetX = analysis.currentX;

        const newAnchor = this.getNewLocation(oldParagraph, targetX);

        if (!newAnchor) {
            // this.log("ABORT - Could not determine newAnchor");
            return;
        }

        this.setSelectionTo(sel, newAnchor.node, newAnchor.offset);

        // Hmm, for some reason, after modifying the selection, now the default seems to work
        // so now we need to prevent it in order to avoiding moving twice the desired amount.
        // this.log("preventDefault called.");
        event.preventDefault();
    }

    private setSelectionTo(sel: Selection, node: Node, offset: number): void {
        // this.log(`setSelectionTo offset ${offset} within node: `);
        // this.printNode(node);
        sel.setBaseAndExtent(node, offset, node, offset);
    }

    // Return the first leaf, even if it's not a text node.
    // <br> is notable for being a leaf that is an element, not a text node.
    private getFirstLeafNode(startNode: Node): Node | null {
        // A callback that stops the DFS search and returns the leaf node back to this function
        const actionAtLeaf = node => {
            return {
                stop: true,
                result: node
            };
        };

        const firstLeaf = this.doActionAtLeaves(startNode, actionAtLeaf);
        return firstLeaf;
    }

    // Checks if the current position is
    // 1) On a relevant boundary line, and if so
    // 2) what the current position's location is on the x-axis
    //
    // Precondition: oldAnchor should point to a leaf node
    private analyzeCurrentPosition(
        ancestor: Element,
        oldAnchor: Anchor
    ):
        | {
              isOnBoundary: boolean;
              currentX: number;
          }
        | undefined {
        if (oldAnchor.node.nodeType === Node.ELEMENT_NODE) {
            if (
                oldAnchor.offset === 0 &&
                oldAnchor.node.childNodes.length === 0
            ) {
                // Handle this corner case right away so that the rest of this function can ignore this scenario.
                // this.log("analyzeCurrentPosition handling corner case.");
                const position = new PosInfo(oldAnchor.node as Element);
                return {
                    isOnBoundary: true, // Presumably just one line, so should always be on a boundary
                    currentX: position.left
                };
            } else {
                // Give up.
                return undefined;
            }
        } else if (oldAnchor.node.nodeType !== Node.TEXT_NODE) {
            return undefined;
        }

        // From this point onward, we can safely assume that oldAnchor.node is a textNode.
        // That makes life easier for some of the recursive functions that we call after this point.
        const oldIndexFromStart = oldAnchor.convertToIndexFromStart(ancestor);
        if (oldIndexFromStart === null) {
            // this.log("ABORT - oldIndexFromStart is null.");
            return undefined;
        }

        // Our goal is now to determine the position of the 1st or last line (depending on direction)
        // and the position of the cursor.
        // From there we can trivially fill out the values of isOnBoundary and cursorX
        const result = this.getPositionOfBoundaryLineAndCursor(
            ancestor,
            oldIndexFromStart
        );
        if (!result) {
            // this.log("getPosition failed.");
            return undefined;
        }
        const [boundaryLinePosition, cursorPosition] = result;

        const yBoundary = boundaryLinePosition.top;
        const yCursor = cursorPosition.top;

        const isOnBoundary = this.isOnSameLine(yBoundary, yCursor);
        return {
            isOnBoundary,
            currentX: cursorPosition.left
        };
    }

    // Returns true if y1 and y2 are more-or-less at the same height.
    private isOnSameLine(y1, y2, toleranceInPixels = 1): boolean {
        return Math.abs(y1 - y2) < toleranceInPixels;
    }

    // Calculates the position of the boundary line (the first or last line, depending on direction)
    // and the position of the cursor.
    // Returns undefined if there is an error while doing so.
    //
    // Determining the position can be expensive, so goes through a bunch of hoops to try to do less DOM modification.
    private getPositionOfBoundaryLineAndCursor(
        ancestor: Element,
        indexFromStart: number
    ): [PosInfo, PosInfo] | undefined {
        // It's easier to work with a clone of the element, so that inserting the marking spans
        // doesn't reset the selection object, references to lives nodes, etc.
        const clone = ancestor.cloneNode(true);
        ancestor.parentElement!.appendChild(clone);

        try {
            // This action converts from indexWithinStart to anchor format (node and indexWithinNode) instead.
            const action = (node, indexWithinNode) => {
                return new Anchor(node, indexWithinNode);
            };
            const valBoundary =
                this.direction === "up"
                    ? this.doActionAtIndex(clone, 0, action)
                    : this.doActionAtLastIndex(clone, action);

            if (!valBoundary.actionPerformed) {
                return undefined;
            }

            const valCursor = this.doActionAtIndex(
                clone,
                indexFromStart,
                action
            );

            if (!valCursor.actionPerformed && !valCursor.isAtEndOfNode) {
                return undefined;
            }

            const boundaryAnchor = valBoundary.actionResult as Anchor;
            const boundaryNode = boundaryAnchor.node;
            const boundaryOffset = boundaryAnchor.offset;

            let cursorAnchor: Anchor;
            if (!valCursor.isAtEndOfNode) {
                // Normal case
                cursorAnchor = valCursor.actionResult as Anchor;
            } else {
                // Special processing if it's to the right of the last char
                const last = valCursor.lastIndex!;
                cursorAnchor = new Anchor(last.node, last.indexWithinNode);
            }
            const cursorNode = cursorAnchor.node;
            const cursorOffset = cursorAnchor.offset;

            let boundaryPos: PosInfo;
            let cursorPos: PosInfo;
            if (boundaryNode !== cursorNode) {
                // Case 1: Handle boundary / cursor independently
                const boundaryParent = this.insertMarkingSpansAtAnchor(
                    boundaryAnchor
                );
                boundaryPos = this.getPosOfNthMarkingSpan(
                    boundaryParent,
                    boundaryOffset
                );

                // Remove the marking spans (sort of) just enough for it not to interfere
                // with getting the position of the next item.
                // (even though textNodeBoundary !== textNodeCursor, parentBoundary could still equal parentCursor
                boundaryParent
                    .querySelectorAll("span.temp")
                    .forEach(matchingElem => {
                        matchingElem.classList.remove("temp");
                    });

                const cursorParent = this.insertMarkingSpansAtAnchor(
                    cursorAnchor
                );
                cursorPos = this.getPosOfNthMarkingSpan(
                    cursorParent,
                    cursorOffset
                );
            } else {
                // Case 2: Handle boundary and cursor at the same time
                const higherOffset = Math.max(boundaryOffset, cursorOffset);
                const parent = this.insertMarkingSpansInNode(
                    boundaryNode,
                    higherOffset
                );

                boundaryPos = this.getPosOfNthMarkingSpan(
                    parent,
                    boundaryOffset
                );
                cursorPos = this.getPosOfNthMarkingSpan(parent, cursorOffset);
            }

            if (valCursor.isAtEndOfNode) {
                // The cursor is to the right of the last character.
                // Return to position to the right of the last character
                cursorPos.left = cursorPos.right;
            }

            return [boundaryPos, cursorPos];
        } finally {
            // Completely remove the temporary clone and with it, the marking spans in it
            ancestor.parentElement!.removeChild(clone);

            // Make sure not to return anything in the finally block. Let the try block's return go through.
        }
    }

    private insertMarkingSpansAtAnchor(anchor: Anchor) {
        return this.insertMarkingSpansInNode(anchor.node, anchor.offset);
    }

    // Helper function that insert spans around each character in the node
    // This can later be leveraged to determine the position
    // Our goal is to create as few spans as possible
    // Returns the parent element which contains the marking spans.
    // Precondition: node must have a parentElement.
    private insertMarkingSpansInNode(node: Node, index?: number): HTMLElement {
        const parent = node.parentElement!;

        if (!node.textContent) {
            return parent;
        }

        const relevantText = index
            ? node.textContent.slice(0, index + 1)
            : node.textContent;
        const remainingText = index ? node.textContent.slice(index + 1) : "";
        const chars = Array.from(relevantText);

        chars.forEach(c => {
            const tempSpan = document.createElement("span");
            tempSpan.classList.add("temp");
            tempSpan.innerText = c;

            parent.insertBefore(tempSpan, node);
        });

        if (remainingText) {
            const remTextNode = document.createTextNode(remainingText);
            parent.insertBefore(remTextNode, node);
        }

        parent.removeChild(node);

        return parent;
    }

    // A more expensive version that inserts marking spans around every character.
    // This can be useful for debugging purposes, when you just want to print out the position
    // of every single character.
    private insertMarkingSpansAroundEachChar(element: Element): void {
        const initialChildNodes = Array.from(element.childNodes);
        initialChildNodes.forEach(childNode => {
            if (childNode.nodeType === Node.TEXT_NODE) {
                const chars = Array.from(childNode.textContent!);

                chars.forEach(c => {
                    const tempSpan = document.createElement("span");
                    tempSpan.classList.add("temp");
                    tempSpan.innerText = c;

                    childNode.parentElement!.insertBefore(tempSpan, childNode);
                });

                childNode.parentElement!.removeChild(childNode);
            } else if (childNode.nodeType === Node.ELEMENT_NODE) {
                // Recursion.
                this.insertMarkingSpansAroundEachChar(childNode as Element);
            } else {
                // Just ignore any other node types.
            }
        });
    }

    // Returns the position of the nth marking span
    // Precondition: Marking spans must have already be inserted into a child of parentElement
    private getPosOfNthMarkingSpan(parentElement: Element, n: number): PosInfo {
        const markingSpans = parentElement.querySelectorAll("span.temp");
        console.assert(markingSpans.length > n);
        const span = markingSpans.item(n);

        return new PosInfo(span);
    }

    // Determines the next paragraph and offset within that paragraph where the anchor should be moved to.
    private getNewLocation(
        oldParagraph: HTMLParagraphElement,
        targetX: number
    ): Anchor | null {
        const sibling =
            this.direction === "up"
                ? oldParagraph.previousSibling
                : oldParagraph.nextSibling;
        if (!sibling) {
            // this.log("SKIP - sibling was null");
            return null;
        }

        const siblingElement =
            sibling.nodeType === Node.TEXT_NODE
                ? sibling.parentElement!
                : (sibling as Element);
        const newParagraph = siblingElement.closest("p");
        if (!newParagraph) {
            // this.log("SKIP - newParagraph not found.");
            return null;
        }

        return this.getAnchorClosestToTargetX(newParagraph, targetX);
    }

    // Finds the character within {newAncestor} that is closest to {targetX} and returns an Anchor to it.
    private getAnchorClosestToTargetX(
        newAncestor: Element,
        targetX: number
    ): Anchor {
        let anchor: Anchor;

        // In order to reduce the amount of expensive HTML modifications that happens,
        // we only mark up one node at a time, check if  reached the stopping condition,
        // and return or keep processing as needed.
        const clone = newAncestor.cloneNode(true) as Element;
        newAncestor.parentElement!.appendChild(clone);
        try {
            let bestAnchorIntoClone: Anchor | undefined;

            // These numbers should be tracked across leaves. (because there may be 2 spans on the same node)
            let expectedTop: number | undefined = undefined;
            let lastDelta = Number.POSITIVE_INFINITY;

            // Traverse each text node on the boundary line to find where the closest position to targetX is.
            const useReverseOrder = this.direction === "up";
            this.doActionAtLeaves(
                clone,
                leaf => {
                    if (leaf.nodeType !== Node.TEXT_NODE) {
                        // This doesn't support non-text leaves yet, though it probably could be made it to.
                        return { stop: false };
                    }

                    const val = this.findBestIndexWithinTextNode(
                        leaf,
                        targetX,
                        expectedTop,
                        lastDelta
                    );

                    bestAnchorIntoClone = val.bestAnchor;

                    if (val.isFinalAnswerFound) {
                        // Break
                        return { stop: true };
                    } else {
                        expectedTop = val.expectedTop;
                        lastDelta = val.lastDelta;

                        // Continue
                        return { stop: false };
                    }
                },
                useReverseOrder
            );

            if (bestAnchorIntoClone) {
                // This points into our temp cloned node, but we need to point into the original node.
                const bestIndex = bestAnchorIntoClone.convertToIndexFromStart(
                    clone
                )!;
                anchor = this.getAnchorAtIndex(newAncestor, bestIndex)!;
            } else {
                // This case can happen if the next target would be a <br> node, which is an element with no children.
                // this.log("Falling back.");
                anchor = new Anchor(newAncestor, 0);
            }
        } finally {
            clone.remove();
        }

        return anchor;
    }

    // Finds the index within {leaf} closest to {targetX}
    private findBestIndexWithinTextNode(
        leaf: Node,
        targetX: number,
        expectedTop,
        lastDelta
    ) {
        let isFinalAnswerFound = false;

        // These represent the best we know so far.
        // If they also represent the global best, then set isFinalAnswerFound to true.
        let bestNode: Node = leaf;
        let bestIndexWithinNode = 0;

        // Add marking spans which enable us to determine position of each character
        const parent = leaf.parentElement!; // Need to save it before inserting the marking spans.
        this.insertMarkingSpansInNode(leaf);
        const markingSpans = parent.querySelectorAll("span.temp");

        let lastNonZeroWidthBounds;
        let lastNonZeroWidthSpan;
        for (let i = 0; i < markingSpans.length; ++i) {
            const adjustedIndex =
                this.direction === "up" ? markingSpans.length - 1 - i : i;
            const span = markingSpans[adjustedIndex];

            const bounds = span.getBoundingClientRect();
            const top = bounds.top;

            if (expectedTop === undefined) {
                expectedTop = top;
            }

            if (i === 0) {
                bestNode = span;

                if (this.direction === "up") {
                    // We'll be processing these right to left.
                    // ENHANCE: Handle RTLlanguages?
                    // I think you would want bounds.left here instead.
                    // I think it's just an off by one error (if you're supposed to match the end) if we don't handle RTL though
                    // That's not too terrible.
                    lastDelta = Math.abs(bounds.right - targetX);
                    bestIndexWithinNode = 1;
                } else {
                    // We'll be processing these left to right.
                    bestIndexWithinNode = 0;
                }
            }

            const delta = Math.abs(bounds.left - targetX);

            if (this.isOnSameLine(top, expectedTop)) {
                // This character is still on relevant line.

                // We assume that spans is always arranged in sequential order
                // That means once our absolute error starts increasing, there's no point going any further.
                if (delta > lastDelta) {
                    // this.log(`BREAK at ${i} because ${delta} > ${lastDelta}`);
                    isFinalAnswerFound = true;
                    break;
                } else {
                    lastDelta = delta;
                    if (bounds.width > 0.00001) {
                        lastNonZeroWidthBounds = bounds;
                        lastNonZeroWidthSpan = span;
                    }

                    bestNode = span;

                    // These are one-character spans, so they have length 1 and
                    // should almost always have indexWithinNode=0 (except when you're at the very end)
                    if (
                        this.direction === "down" &&
                        i === markingSpans.length - 1
                    ) {
                        const deltaEnd = Math.abs(bounds.right - targetX);
                        bestIndexWithinNode = delta < deltaEnd ? 0 : 1;
                    } else {
                        bestIndexWithinNode = 0;
                    }
                }
            } else {
                // No longer on relevant line.
                // We need to stop and return something.
                isFinalAnswerFound = true;

                // Up direction: Safe to return as is.
                // Down direction: Check if we need to move 1 char over (decide whether to return to the left or right of the previous char)
                if (this.direction === "down") {
                    // The reason we use lastNonZeroWidthSpan vs. bestNode
                    // is because the last character on the line may be a space, which has left = right,
                    // so the more worthwhile check is the last non-space character.
                    if (lastNonZeroWidthSpan && lastNonZeroWidthBounds) {
                        const deltaFromLeft = Math.abs(
                            lastNonZeroWidthBounds.left - targetX
                        );
                        const deltaFromRight = Math.abs(
                            lastNonZeroWidthBounds.right - targetX
                        );

                        if (deltaFromLeft < deltaFromRight) {
                            bestNode = lastNonZeroWidthSpan;
                        }

                        bestIndexWithinNode = 0;
                    }
                }

                break;
            }
        }

        // Clean up enough so that you don't mess up the next iteration.
        markingSpans.forEach(span => {
            span.classList.remove("temp");
        });

        return {
            isFinalAnswerFound, // If false, we'll return the closest point, but other textNodes may have closer.
            bestAnchor: new Anchor(bestNode, bestIndexWithinNode),
            expectedTop,
            lastDelta
        };
    }

    private getAnchorAtIndex(node: Node, index: number): Anchor | null {
        const action = (node, index) => {
            return new Anchor(node, index);
        };
        const val = this.doActionAtIndex(node, index, action);

        if (val.actionPerformed) {
            return val.actionResult as Anchor;
        } else if (val.isAtEndOfNode) {
            const lastIndex = val.lastIndex!;
            return new Anchor(lastIndex.node, lastIndex.indexWithinNode + 1);
        } else {
            return null;
        }
    }

    // A generalized helper method that invokes an action at the specified index.
    // Recursively traverses the tree starting at {node} until the {index}-th character is found.
    // When it reaches the {index}-th character, invokes the action callback there.
    private doActionAtIndex(
        node: Node,
        index: number,
        action: (node: Node, index: number, char: string) => any
    ): {
        actionPerformed?: boolean;
        actionResult?: any;
        numCharsProcessed?: number;
        isAtEndOfNode?: boolean; // True if the index points to the exact end of the last thing in the node
        // If isAtEndOfNode is true, then this will be set to point to the last character within the node.
        // (Note: the end of the node is 1 past lastIndex.indexWithinNode)
        lastIndex?: {
            node: Node;
            indexWithinNode: number;
            char: string;
        };
    } {
        if (node.nodeType === Node.TEXT_NODE) {
            // Base Case
            // FYI: TextNodes are always leaf nodes and don't have any childNodes

            if (!node.textContent || node.textContent.length <= 0) {
                return { numCharsProcessed: 0 };
            } else if (index < node.textContent.length) {
                // Note: Theoretically, one should only apply an action if index is strictly less than the length.
                // But if you're dealing with offsets, it may also validly represent the case where it is to the right
                // of the last character as index = length, in which case you would be OK with applying the action here
                const char = node.textContent.charAt(index);
                const actionResult = action(node, index, char);
                return {
                    actionPerformed: true,
                    actionResult
                };
            } else if (index === node.textContent.length) {
                return {
                    numCharsProcessed: node.textContent.length,
                    isAtEndOfNode: true,
                    lastIndex: {
                        node,
                        indexWithinNode: index - 1,
                        char: node.textContent.charAt(index - 1)
                    }
                };
            } else {
                // That is, index > allowed index
                return { numCharsProcessed: node.textContent.length };
            }
        } else if (node.hasChildNodes()) {
            const numChildren = node.childNodes.length;
            const result: any = {};
            let numCharsProcessed = 0;
            for (let i = 0; i < numChildren; ++i) {
                const childNode = node.childNodes.item(i);
                const adjustedIndex = index - numCharsProcessed;
                const val = this.doActionAtIndex(
                    childNode,
                    adjustedIndex,
                    action
                );
                if (val.actionPerformed) {
                    return val;
                }
                // Else: No action performed yet.

                if (val.numCharsProcessed! > 0) {
                    // If we processed any chars, reset isAtEndOfNode to whatever val retursn.
                    result.isAtEndOfNode = val.isAtEndOfNode;
                    result.lastIndex = val.lastIndex;
                }

                // Mark down the number of chars checked so far, then continue to the next child.
                numCharsProcessed += val.numCharsProcessed!;
            }

            result.numCharsProcessed = numCharsProcessed;
            return result;
        } else {
            // This can be an element without any children. Like a <br > node, in particular...
            return { numCharsProcessed: 0 };
        }
    }

    // Applies an action at the last character in the node
    private doActionAtLastIndex(
        node: Node,
        action: (node: Node, index: number, char: string) => any
    ): {
        actionPerformed?: boolean;
        actionResult?: any;
    } {
        if (node.nodeType === Node.TEXT_NODE) {
            // Base Case
            // FYI: TextNodes are always leaf nodes and don't have any childNodes

            if (!node.textContent || node.textContent.length <= 0) {
                return { actionPerformed: false };
            } else {
                const index = node.textContent.length - 1;
                const char = node.textContent.charAt(index);
                const actionResult = action(node, index, char);
                return {
                    actionPerformed: true,
                    actionResult
                };
            }
        } else if (node.hasChildNodes()) {
            for (let i = node.childNodes.length - 1; i >= 0; --i) {
                const childNode = node.childNodes.item(i);
                const val = this.doActionAtLastIndex(childNode, action);
                if (val.actionPerformed) {
                    return val;
                }
                // else, continue to the next child.
            }

            // Went thru all the children w/o applying any actions.
            return { actionPerformed: false };
        } else {
            return { actionPerformed: false };
        }
    }

    // This performs a depth-first traversal of the tree starting at {startNode}
    // using a loop-based approach (less overhead than recursion)
    // At each leaf node, the actionAtLeaf callback will be invoked.
    //  It should return an object.
    //    The "stop" field of that object indicates whether to continue the DFS traversal or to abort/return/break out.
    //    The "result" field is optional and can be used to return any value desired back to the caller
    private doActionAtLeaves(
        startNode: Node,
        actionAtLeaf: (node) => { stop: boolean; result?: any },
        useReverseOrder?: boolean
    ): any | null {
        const nodeStack = [startNode as Node];
        do {
            const current = nodeStack.pop()!;
            if (current.hasChildNodes()) {
                pushNodeListOntoStack(
                    current.childNodes,
                    nodeStack,
                    useReverseOrder
                );
            } else {
                // We found a leaf node
                const val = actionAtLeaf(current);
                if (val.stop) {
                    return val.result;
                }
            }
        } while (nodeStack.length > 0);

        return null;
    }

    // Prints a message, if debugging is enabled for thsi object.
    private log(message: string): void {
        if (this.debug) {
            console.log(message);
        }
    }

    // Prints a node (for debugging purposes) if debugging is enabled on this object
    private printNode(node: Node | null | undefined, prefix: string = "") {
        if (this.debug) {
            ArrowKeyWorkaroundManager.printNode(node, prefix);
        }
    }

    // Prints a node (for debugging purposes)
    public static printNode(
        node: Node | null | undefined,
        prefix: string = ""
    ) {
        if (node === undefined) {
            console.log(`${prefix}Undefined`);
        } else if (node === null) {
            console.log(`${prefix}Null`);
        } else if (node.nodeType === Node.TEXT_NODE) {
            console.log(`${prefix}TextNode: "${node.textContent}"`);
        } else {
            console.log(`${prefix}ElementNode: ${(node as Element).outerHTML}`);
        }
    }

    // Prints out the position of each character in element.
    // This can be useful for debugging.
    public static printCharPositions(element: Element): void {
        const myNav = new ArrowKeyWorkaroundManager();
        myNav.debug = true;

        if (!element.parentElement) {
            return;
        }

        // Clones the element so that it doesn't directly modify the original element.
        const clone = element.cloneNode(true) as Element;

        // Append the clone into the parent so that it'll have the same width, styling, etc.
        // Unnecessary to make it invisible. Due to Javascript event loop,
        // as long as we don't await stuff, it should be removed before the UI gets to re-render things
        element.parentElement.appendChild(clone);

        // Insert temporary inline elements around each character so we can measure their position
        myNav.insertMarkingSpansAroundEachChar(clone);

        const markedSpans = clone.querySelectorAll("span.temp");

        // Actually measure the position of each character
        for (let i = 0; i < markedSpans.length; ++i) {
            const span = markedSpans[i] as HTMLElement;

            // Note: span.offsetLeft is relative to the immediate parent,
            // whereas getBoundingClientRect() is relative to the viewport.
            // That means the getBoundingClientRect() results are more easily compared.
            const bounds = span.getBoundingClientRect();

            console.log(
                `index:\t${i}\tchar:\t${span.innerText}\tleft:\t${Math.round(
                    bounds.left
                )}\tright:\t${Math.round(bounds.right)}\ttop:\t${Math.round(
                    bounds.top
                )}`
            );
        }

        // Cleanup
        const updatedChildNodes = element.parentElement.childNodes;
        const lastChild = updatedChildNodes[updatedChildNodes.length - 1];
        const removed = element.parentElement.removeChild(lastChild);
        console.assert(removed, "removeChild failed.");
    }
}

class PosInfo {
    public left: number;
    public right: number;
    public top: number;

    public constructor(element: Element) {
        const bounds = element.getBoundingClientRect();
        this.left = bounds.left;
        this.right = bounds.right;
        this.top = bounds.top;
    }
}

// Represents an AnchorNode and AnchorOffset, which are useful for Selections
export class Anchor {
    public node: Node;
    public offset: number;

    public constructor(node: Node, offset: number) {
        this.node = node;
        this.offset = offset;
    }

    // Returns the number of characters between the 1st character within startElement (including any of its descendants)
    // and this anchor
    // (the anchorOffset tells you the index from the start of anchorNode, but if startElement contains multiple nodes including anchorNode,
    // this will tell you the index from the start of startElement)
    public convertToIndexFromStart(startElement: Element): number | null {
        let numCharsProcessed = 0;
        const nodeStack = [startElement as Node];
        while (nodeStack.length > 0) {
            const current = nodeStack.pop()!;

            if (current === this.node) {
                return numCharsProcessed + this.offset;
            } else if (current.nodeType === Node.TEXT_NODE) {
                numCharsProcessed += current.textContent?.length ?? 0;
            } else if (current.hasChildNodes()) {
                pushNodeListOntoStack(current.childNodes, nodeStack);
            } else {
                // Just ignore any strange nodes and continue onward.
            }
        }

        return null;
    }
}

// Appends the specified node list to the top of {stack}
// If useReverseOrder = true, {nodes} will be added in reverse order to the top of the stack.
function pushNodeListOntoStack(
    nodes: NodeListOf<ChildNode>,
    stack: Node[],
    useReverseOrder: boolean = false
) {
    const oldCount = stack.length;
    const childCount = nodes.length;
    stack.length += childCount;
    for (let i = 0; i < childCount; ++i) {
        // Note: In this "stack", the "top" is the array's end.
        const adjustedIndex = useReverseOrder ? i : childCount - 1 - i;
        stack[oldCount + i] = nodes.item(adjustedIndex);
    }
}
