import { Bubble, BubbleSpec, Comical, TailSpec } from "comicaljs";
import { Point, PointScaling } from "../point";
import {
    kBackgroundImageClass,
    kBloomButtonClass,
} from "../../toolbox/canvas/canvasElementConstants";
import AudioRecording from "../../toolbox/talkingBook/audioRecording";
import { postData, postJson } from "../../../utils/bloomApi";

const kComicalGeneratedClass: string = "comical-generated";

export interface ICanvasElementDuplicationHost {
    getPatriarchBubbleOfActiveElement: () => Bubble | undefined;
    setActiveElement: (element: HTMLElement | undefined) => void;

    getSelectedItemBubbleSpec: () => BubbleSpec | undefined;
    updateSelectedItemBubbleSpec: (spec: BubbleSpec) => void;

    refreshCanvasElementEditing: (
        bloomCanvas: HTMLElement,
        bubble: Bubble | undefined,
        attachEventsToEditables: boolean,
        activateCanvasElement: boolean,
    ) => void;

    removeJQueryResizableWidget: () => void;
    initializeCanvasElementEditing: () => void;

    addCanvasElementFromOriginal: (
        offsetX: number,
        offsetY: number,
        originalElement: HTMLElement,
        style?: string,
    ) => HTMLElement | undefined;

    findBestLocationForNewCanvasElement: (
        parentElement: HTMLElement,
        proposedOffsetX: number,
        proposedOffsetY: number,
    ) => Point | undefined;

    reorderRectangleCanvasElement: (
        rectangle: HTMLElement,
        bloomCanvas: HTMLElement,
    ) => void;

    addChildInternal: (
        parentElement: HTMLElement,
        offsetX: number,
        offsetY: number,
    ) => HTMLElement | undefined;

    adjustRelativePointToBloomCanvas: (
        bloomCanvas: Element,
        point: Point,
    ) => Point;
}

export class CanvasElementDuplication {
    private host: ICanvasElementDuplicationHost;

    public constructor(host: ICanvasElementDuplicationHost) {
        this.host = host;
    }

    // We verify that 'textElement' is the active element before calling this method.
    public duplicateCanvasElementBox(
        textElement: HTMLElement,
        sameLocation?: boolean,
    ): HTMLElement | undefined {
        // simple guard
        if (!textElement || !textElement.parentElement) {
            return undefined;
        }
        const bloomCanvas = textElement.parentElement;
        // Make sure comical is up-to-date before we clone things.
        if (
            bloomCanvas.getElementsByClassName(kComicalGeneratedClass).length >
            0
        ) {
            Comical.update(bloomCanvas);
        }
        // Get the patriarch canvas element of this comical family. Can only be undefined if no active element.
        const patriarchBubble = this.host.getPatriarchBubbleOfActiveElement();
        if (patriarchBubble) {
            if (textElement !== patriarchBubble.content) {
                this.host.setActiveElement(patriarchBubble.content);
            }
            const bubbleSpecToDuplicate = this.host.getSelectedItemBubbleSpec();
            if (!bubbleSpecToDuplicate) {
                // Oddness! Bail!
                // reset active element to what it was
                this.host.setActiveElement(textElement as HTMLElement);
                return;
            }

            const result = this.duplicateCanvasElementFamily(
                patriarchBubble,
                bubbleSpecToDuplicate,
                sameLocation,
            );
            if (result) {
                const isRectangle =
                    result.getElementsByClassName("bloom-rectangle").length > 0;
                if (isRectangle) {
                    // adjust the new rectangle's z-order and comical level to match the original.
                    this.host.reorderRectangleCanvasElement(
                        result,
                        bloomCanvas,
                    );
                }
            }
            // The JQuery resizable event handler needs to be removed after the duplicate canvas element
            // family is created, and then the over picture editing needs to be initialized again.
            // See BL-13617.
            this.host.removeJQueryResizableWidget();
            this.host.initializeCanvasElementEditing();
            return result;
        }
        return undefined;
    }

    // Should duplicate all canvas elements and their size and relative placement and color, etc.,
    // and the actual text in the canvas elements.
    // The 'patriarchSourceBubble' is the head of a family of canvas elements to duplicate,
    // although this one canvas element may be all there is.
    // The content of 'patriarchSourceBubble' is now the active element.
    // The 'bubbleSpecToDuplicate' param is the bubbleSpec for the patriarch source canvas element.
    // The function returns the patriarch canvas element of the new
    // duplicated canvas element family.
    // This method handles all needed refreshing of the duplicate canvas elements.
    private duplicateCanvasElementFamily(
        patriarchSourceBubble: Bubble,
        bubbleSpecToDuplicate: BubbleSpec,
        sameLocation: boolean = false,
    ): HTMLElement | undefined {
        const sourceElement = patriarchSourceBubble.content;
        const proposedOffset = 15;
        const newPoint = this.host.findBestLocationForNewCanvasElement(
            sourceElement,
            sameLocation ? 0 : proposedOffset + sourceElement.clientWidth, // try to not overlap too much
            sameLocation ? 0 : proposedOffset,
        );
        if (!newPoint) {
            return;
        }
        const patriarchDuplicateElement =
            this.host.addCanvasElementFromOriginal(
                newPoint.getScaledX(),
                newPoint.getScaledY(),
                sourceElement,
                bubbleSpecToDuplicate.style,
            );
        if (!patriarchDuplicateElement) {
            return;
        }
        patriarchDuplicateElement.classList.remove(kBackgroundImageClass);
        patriarchDuplicateElement.style.color = sourceElement.style.color; // preserve text color
        patriarchDuplicateElement.innerHTML =
            this.safelyCloneHtmlStructure(sourceElement);
        // Preserve the Auto Height setting.  See BL-13931.
        if (sourceElement.classList.contains("bloom-noAutoHeight"))
            patriarchDuplicateElement.classList.add("bloom-noAutoHeight");
        // Preserve the bloom-gif class, which is used to indicate that this is a GIF. (BL-15037)
        if (sourceElement.classList.contains("bloom-gif"))
            patriarchDuplicateElement.classList.add("bloom-gif");
        if (sourceElement.classList.contains(kBloomButtonClass))
            patriarchDuplicateElement.classList.add(kBloomButtonClass);

        // copy any data-sound
        const sourceDataSound = sourceElement.getAttribute("data-sound");
        if (sourceDataSound) {
            patriarchDuplicateElement.setAttribute(
                "data-sound",
                sourceDataSound,
            );
        }
        // copy any sound files found in an editable div
        this.copyAnySoundFileAndAttributesForEditable(
            sourceElement,
            patriarchDuplicateElement,
        );

        this.host.setActiveElement(patriarchDuplicateElement);
        this.matchSizeOfSource(sourceElement, patriarchDuplicateElement);
        const container = patriarchDuplicateElement.closest(
            ".bloom-canvas",
        ) as HTMLElement | null;
        if (!container) {
            return; // highly unlikely!
        }
        const adjustedTailSpec = this.getAdjustedTailSpec(
            container,
            bubbleSpecToDuplicate.tails,
            sourceElement,
            patriarchDuplicateElement,
        );
        // This is the bubbleSpec for the brand new (now active) copy of the patriarch canvas element.
        // We will overwrite most of it, but keep its level and version properties. The level will be
        // different so the copied canvas element(s) will be in a separate child chain from the original(s).
        // The version will probably be the same, but if it differs, we want the new one.
        // We will update this bubbleSpec with an adjusted version of the original tail and keep
        // other original properties (like backgroundColor and border style/color and order).
        const specOfCopiedElement = this.host.getSelectedItemBubbleSpec();
        if (!specOfCopiedElement) {
            return; // highly unlikely!
        }
        this.host.updateSelectedItemBubbleSpec({
            ...bubbleSpecToDuplicate,
            tails: adjustedTailSpec,
            level: specOfCopiedElement.level,
            version: specOfCopiedElement.version,
        });
        // OK, now we're done with our manipulation of the patriarch canvas element and we're about to go on
        // and deal with the child canvas elements (if any). But we replaced the innerHTML after creating the
        // initial duplicate canvas element and the editable divs may not have the appropriate events attached,
        // so we'll refresh again with 'attachEventsToEditables' set to 'true'.
        this.host.refreshCanvasElementEditing(
            container,
            new Bubble(patriarchDuplicateElement),
            true,
            true,
        );
        const childBubbles = Comical.findRelatives(patriarchSourceBubble);
        childBubbles.forEach((childBubble) => {
            const childOffsetFromPatriarch = this.getOffsetFrom(
                sourceElement,
                childBubble.content,
            );
            this.duplicateOneChildCanvasElement(
                childOffsetFromPatriarch,
                patriarchDuplicateElement,
                childBubble,
            );
            // Make sure comical knows about each child as it's created, otherwise it gets the order wrong.
            Comical.convertBubbleJsonToCanvas(container as HTMLElement);
        });
        return patriarchDuplicateElement;
    }

    private duplicateOneChildCanvasElement(
        offsetFromPatriarch: Point,
        parentElement: HTMLElement,
        childSourceBubble: Bubble,
    ): void {
        const newChildElement = this.host.addChildInternal(
            parentElement,
            offsetFromPatriarch.getScaledX(),
            offsetFromPatriarch.getScaledY(),
        );
        if (!newChildElement) {
            return;
        }
        const sourceElement = childSourceBubble.content;
        newChildElement.innerHTML =
            this.safelyCloneHtmlStructure(sourceElement);
        this.copyAnySoundFileAndAttributesForEditable(
            sourceElement,
            newChildElement,
        );
        // Preserve the Auto Height setting.  See BL-13931.
        if (sourceElement.classList.contains("bloom-noAutoHeight"))
            newChildElement.classList.add("bloom-noAutoHeight");
        // Preserve the bloom-gif class, which is used to indicate that this is a GIF. (BL-15037)
        if (sourceElement.classList.contains("bloom-gif"))
            newChildElement.classList.add("bloom-gif");

        this.matchSizeOfSource(sourceElement, newChildElement);
        // We just replaced the bloom-editables from the 'addChildInternal' with a clone of the source
        // canvas element's HTML. This will undo any event handlers that might have been attached by the
        // refresh triggered by 'addChildInternal'. So we send the newly modified child through again,
        // with 'attachEventsToEditables' set to 'true'.
        this.host.refreshCanvasElementEditing(
            parentElement.closest(".bloom-canvas") as HTMLElement,
            new Bubble(newChildElement),
            true,
            true,
        );
    }

    private copyAnySoundFileAndAttributesForEditable(
        sourceElement: HTMLElement,
        copiedElement: HTMLElement,
    ): void {
        const sourceEditable = sourceElement.querySelector(".bloom-editable");
        if (!sourceEditable) return;
        const copiedEditable = copiedElement.querySelector(".bloom-editable");
        if (!copiedEditable) return;
        const sourceId = sourceEditable.getAttribute("id");
        const mode = sourceEditable.getAttribute("data-audiorecordingmode");
        if (sourceId && mode === "TextBox") {
            this.copySoundFileAndAttributes(
                sourceEditable,
                sourceId,
                copiedEditable,
            );
        } else if (mode === "Sentence") {
            const sourceSpans = sourceEditable.querySelectorAll(
                "span.audio-sentence[id][recordingmd5]",
            );
            const copiedSpans = copiedEditable.querySelectorAll(
                "span.audio-sentence[recordingmd5]",
            );
            if (
                sourceSpans.length === copiedSpans.length &&
                sourceSpans.length > 0
            ) {
                sourceSpans.forEach((sourceSpan, index) => {
                    const copiedSpan = copiedSpans[index];
                    const sourceSpanId = sourceSpan.getAttribute("id");
                    if (sourceSpanId) {
                        this.copySoundFileAndAttributes(
                            sourceSpan,
                            sourceSpanId,
                            copiedSpan,
                        );
                    }
                });
            }
        }
    }

    private copySoundFileAndAttributes(
        sourceElement: Element,
        sourceId: string,
        copiedElement: Element,
    ): void {
        const newId = AudioRecording.createValidXhtmlUniqueId();
        copiedElement.setAttribute("id", newId);
        void copyAudioFileAsync(sourceId, newId); // we don't need to wait for this to finish
        const duration = sourceElement.getAttribute("data-duration");
        if (duration) {
            copiedElement.setAttribute("data-duration", duration);
        }
        const endTimes = sourceElement.getAttribute(
            "data-audiorecordingendtimes",
        );
        if (endTimes) {
            copiedElement.setAttribute("data-audiorecordingendtimes", endTimes);
        }
    }

    private getAdjustedTailSpec(
        bloomCanvas: Element,
        originalTailSpecs: TailSpec[],
        sourceElement: HTMLElement,
        duplicateElement: HTMLElement,
    ): TailSpec[] {
        if (originalTailSpecs.length === 0) {
            return originalTailSpecs;
        }
        const offSetFromSource = this.getOffsetFrom(
            sourceElement,
            duplicateElement,
        );
        return originalTailSpecs.map((spec) => {
            const tipPoint = this.host.adjustRelativePointToBloomCanvas(
                bloomCanvas,
                new Point(
                    spec.tipX + offSetFromSource.getUnscaledX(),
                    spec.tipY + offSetFromSource.getUnscaledY(),
                    PointScaling.Unscaled,
                    "getAdjustedTailSpec.tip",
                ),
            );
            const midPoint = this.host.adjustRelativePointToBloomCanvas(
                bloomCanvas,
                new Point(
                    spec.midpointX + offSetFromSource.getUnscaledX(),
                    spec.midpointY + offSetFromSource.getUnscaledY(),
                    PointScaling.Unscaled,
                    "getAdjustedTailSpec.mid",
                ),
            );
            return {
                ...spec,
                tipX: tipPoint.getUnscaledX(),
                tipY: tipPoint.getUnscaledY(),
                midpointX: midPoint.getUnscaledX(),
                midpointY: midPoint.getUnscaledY(),
            };
        });
    }

    private matchSizeOfSource(
        sourceElement: HTMLElement,
        destElement: HTMLElement,
    ): void {
        destElement.style.width = sourceElement.clientWidth.toFixed(0) + "px";
        // text elements adjust their height automatically based on width and content...
        // picture over picture and video over picture don't.
        destElement.style.height = sourceElement.clientHeight.toFixed(0) + "px";
    }

    private getOffsetFrom(
        sourceElement: HTMLElement,
        destElement: HTMLElement,
    ): Point {
        return new Point(
            destElement.offsetLeft - sourceElement.offsetLeft,
            destElement.offsetTop - sourceElement.offsetTop,
            PointScaling.Scaled,
            "Destination scaled offset from Source",
        );
    }

    private safelyCloneHtmlStructure(elementToClone: HTMLElement): string {
        // eliminate .bloom-ui and ?
        const clonedElement = elementToClone.cloneNode(true) as HTMLElement;
        this.cleanClonedNode(clonedElement);
        return clonedElement.innerHTML;
    }

    private cleanClonedNode(element: Element): void {
        if (this.clonedNodeNeedsDeleting(element)) {
            element.parentElement!.removeChild(element);
            return;
        }
        if (element.nodeName === "#text") {
            return;
        }

        // Cleanup this node
        this.safelyRemoveAttribute(element, "id");
        // Picture over picture elements need the tabindex (="0") in order to be focusable.
        // But for text-based canvas elements we need to delete positive tabindex, so we don't do weird
        // things to talking book playback order when we duplicate a family of canvas elements.
        this.removePositiveTabindex(element);
        this.safelyRemoveAttribute(element, "data-duration");
        this.safelyRemoveAttribute(element, "data-audiorecordingendtimes");

        // Clean children
        const childArray = Array.from(element.childNodes);
        childArray.forEach((child) => {
            this.cleanClonedNode(child as Element);
        });
    }

    private removePositiveTabindex(element: Element): void {
        if (!element.hasAttribute("tabindex")) {
            return;
        }
        const indexStr = element.getAttribute("tabindex");
        if (!indexStr) {
            return;
        }
        const indexValue = parseInt(indexStr, 10);
        if (indexValue > 0) {
            element.attributes.removeNamedItem("tabindex");
        }
    }

    private safelyRemoveAttribute(element: Element, attrName: string): void {
        if (element.hasAttribute(attrName)) {
            element.attributes.removeNamedItem(attrName);
        }
    }

    private clonedNodeNeedsDeleting(element: Element): boolean {
        const htmlElement = element as HTMLElement;
        return (
            !htmlElement ||
            (htmlElement.classList &&
                htmlElement.classList.contains("bloom-ui"))
        );
    }
}

async function copyAudioFileAsync(
    sourceId: string,
    newId: string,
): Promise<void> {
    const folderInfo = await postJson(
        "fileIO/getSpecialLocation",
        "CurrentBookAudioDirectory",
    );
    if (!folderInfo || !folderInfo.data) {
        return; // huh??
    }
    const sourcePath = `${folderInfo.data}/${sourceId}.mp3`;
    const targetPath = `${folderInfo.data}/${newId}.mp3`;
    await postData("fileIO/copyFile", {
        from: encodeURIComponent(sourcePath),
        to: encodeURIComponent(targetPath),
    });
}
