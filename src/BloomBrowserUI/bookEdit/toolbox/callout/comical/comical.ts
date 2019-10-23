import { Color, project, setup, Layer } from "paper";

import Bubble from "./bubble";
import { uniqueIds } from "./uniqueId";
import { BubbleSpec } from "bubbleSpec";

// Manages a collection of comic bubbles warpped around HTML elements that share a common parent.
// Each element that has a comic bubble has a data-bubble attribute specifying the appearance
// of the bubble. Comical can help with initializing this to add a bubble to an element.
// The data-bubble attributes contain a modified JSON representation of a BubbleSpec
// describing the bubble.
// Comical is designed to be the main class exported by Comical.js, and provides methods
// for setting things up (using a canvas overlayed on the common parent of the bubbles
// and paper.js shapes) so that the bubbles can be edited by dragging handles.
// It also supports drawing groups of bubbles in layers, with appropriate merging
// of bubbles at the same level.
// As the bubbles are edited using Comical handles, the data-bubble attributes are
// automatically updated. It's also possible to alter a data-bubble attribute using
// external code, and tell Comical to update things to match.
// Finally, Comical can replace a finished bubble canvas with a single SVG, resulting in
// a visually identical set of bubbles that can be rendered without using Canvas and
// Javascript.
export default class Comical {
    static backColor = new Color("white");

    static bubbleLists = new Map<Element, Bubble[]>();

    static allBubbles: Bubble[];

    static activeBubble: Bubble | undefined;

    static handleLayer: Layer;

    public static convertCanvasToSvgImg(parent: HTMLElement) {
        const canvas = parent.getElementsByTagName("canvas")[0];
        if (!canvas) {
            return;
        }
        // Remove drag handles
        project!
            .getItems({
                recursive: true,
                match: (x: any) => {
                    return x.name && x.name.startsWith("handle");
                }
            })
            .forEach(x => x.remove());
        const svg = project!.exportSVG() as SVGElement;
        svg.classList.add("comical-generated");
        uniqueIds(svg);
        canvas.parentElement!.insertBefore(svg, canvas);
        canvas.remove();
        Comical.stopMonitoring(parent);
    }

    // This logic is designed to prevent accumulating mutation observers.
    // Not yet fully tested.
    private static stopMonitoring(parent: HTMLElement) {
        const bubbles = Comical.bubbleLists.get(parent);
        if (bubbles) {
            bubbles.forEach(bubble => bubble.stopMonitoring());
        }
    }

    // Make the bubble for the specified element (if any) active. This means
    // showing its edit handles. Must first call convertBubbleJsonToCanvas(),
    // passing the appropriate parent element.
    public static activateElement(contentElement: Element) {
        let newActiveBubble: Bubble | undefined = undefined;
        if (contentElement) {
            newActiveBubble = Comical.allBubbles.find(
                x => x.content === contentElement
            );
        }
        Comical.activateBubble(newActiveBubble);
    }

    // Make active (show handles) the specified bubble.
    public static activateBubble(newActiveBubble: Bubble | undefined) {
        if (newActiveBubble == Comical.activeBubble) {
            return;
        }
        Comical.hideHandles();
        Comical.activeBubble = newActiveBubble;
        if (Comical.activeBubble) {
            Comical.activeBubble.showHandles();
        }
    }

    public static hideHandles() {
        if (Comical.handleLayer) {
            Comical.handleLayer.removeChildren();
        }
    }

    // call after adding or deleting elements with data-bubble
    // assumes convertBubbleJsonToCanvas has been called and canvas exists
    public static update(parent: HTMLElement) {
        Comical.stopMonitoring(parent);
        while (project!.layers.length > 1) {
            const layer = project!.layers.pop();
            if (layer) {
                layer.remove(); // Erase this layer
            }
        }
        if (project!.layers.length > 0) {
            project!.layers[0].activate();
        }
        project!.activeLayer.removeChildren();

        const elements = parent.ownerDocument!.evaluate(
            ".//*[@data-bubble]",
            parent,
            null,
            XPathResult.UNORDERED_NODE_SNAPSHOT_TYPE,
            null
        );
        const bubbles: Bubble[] = [];
        Comical.bubbleLists.set(parent, bubbles);

        var zLevelList: number[] = [];
        Comical.allBubbles = [];
        for (let i = 0; i < elements.snapshotLength; i++) {
            const element = elements.snapshotItem(i) as HTMLElement;
            const bubble = new Bubble(element);
            Comical.allBubbles.push(bubble);

            let zLevel = bubble.getSpecLevel();
            if (!zLevel) {
                zLevel = 0;
            }
            zLevelList.push(zLevel);
        }

        // Ensure that they are in ascending order
        zLevelList.sort();

        // First we need to create all the layers in order. (Because they automatically get added to the end of the project's list of layers)
        // Precondition: Assumes zLevelList is sorted.
        const levelToLayer = {};
        for (let i = 0; i < zLevelList.length; ++i) {
            // Check if different than previous. (Ignore duplicate z-indices)
            if (i == 0 || zLevelList[i - 1] != zLevelList[i]) {
                const zLevel = zLevelList[i];
                var lowerLayer = new Layer();
                var upperLayer = new Layer();
                levelToLayer[zLevel] = [lowerLayer, upperLayer];
            }
        }
        Comical.handleLayer = new Layer();

        // Now that the layers are created, we can go back and place objects into the correct layers and ask them to draw themselves.
        for (let i = 0; i < Comical.allBubbles.length; ++i) {
            const bubble = Comical.allBubbles[i];

            let zLevel = bubble.getSpecLevel();
            if (!zLevel) {
                zLevel = 0;
            }

            const [lowerLayer, upperLayer] = levelToLayer[zLevel];
            bubble.setLayers(lowerLayer, upperLayer, Comical.handleLayer);
            bubble.initialize();
            bubbles.push(bubble);
        }
    }

    public static getMaxLevel(): number {
        if (!Comical.allBubbles || Comical.allBubbles.length === 0) {
            return 0;
        }
        let maxLevel = Number.MIN_VALUE;
        Comical.allBubbles.forEach(
            b => (maxLevel = Math.max(maxLevel, b.getBubbleSpec().level || 0))
        );
        return maxLevel;
    }

    public static convertBubbleJsonToCanvas(parent: HTMLElement) {
        const canvas = parent.ownerDocument!.createElement("canvas");
        canvas.style.position = "absolute";
        canvas.style.top = "0";
        canvas.style.left = "0";
        canvas.classList.add("comical-generated");
        canvas.classList.add("comical-editing");
        const oldSvg = parent.getElementsByClassName("comical-generated")[0];
        if (oldSvg) {
            oldSvg.parentElement!.insertBefore(canvas, oldSvg);
            oldSvg.remove();
        } else {
            parent.insertBefore(canvas, parent.firstChild); // want to use prepend, not in FF45.
        }
        canvas.width = parent.clientWidth;
        canvas.height = parent.clientHeight;
        setup(canvas);
        Comical.update(parent);
    }

    // Make appropriate JSON changes so that childElement becomes a child of parentElement.
    // This means they are at the same level and, if they don't overlap, a joiner is drawn
    // between them.
    // The conceptual model is that all elements at the same level form a family, provided
    // they have distinct order properties. The one with the lowest order is considered
    // the overall parent. A child can be added to a family by specifying any member of the
    // family as a parentElement. It is expected that both elements are children of
    // the root element most recently configured for Comical with convertBubbleJsonToCanvas().
    public static initializeChild(
        childElement: HTMLElement,
        parentElement: HTMLElement
    ) {
        const parentBubble = Comical.allBubbles.find(
            x => x.content === parentElement
        );
        if (!parentBubble) {
            console.error(
                "trying to make child of element not already active in Comical"
            );
            return;
        }
        const parentSpec = parentBubble.getBubbleSpec();
        let familyLevel = parentSpec.level;
        if (!parentSpec.order) {
            // It's important not to use zero for order, since that will be treated
            // as an unspecified order.
            parentSpec.order = 1;
            parentBubble.persistBubbleSpec();
        }
        // enhance: if familyLevel is undefined, set it to a number one greater than
        // any level that occurs in allBubbles.
        let childBubble = Comical.allBubbles.find(
            x => x.content === childElement
        );
        if (!childBubble) {
            childBubble = new Bubble(childElement);
        }
        const lastInFamily = Comical.getLastInFamily(familyLevel);
        const maxOrder = lastInFamily.getBubbleSpec().order || 1;
        const tip = lastInFamily.calculateTailStartPoint();
        const root = childBubble.calculateTailStartPoint();
        const mid = Bubble.defaultMid(root, tip);
        // We deliberately do NOT keep any properties the child bubble already has.
        // Apart from the necessary properties for being a child, it will take
        // all its properties from the parent.
        const newBubbleSpec: BubbleSpec = {
            version: Comical.bubbleVersion,
            style: parentSpec.style,
            tails: [
                {
                    tipX: tip.x!,
                    tipY: tip.y!,
                    midpointX: mid.x!,
                    midpointY: mid.y!,
                    joiner: true
                }
            ],
            level: parentSpec.level,
            order: maxOrder + 1
        };
        childBubble.setBubbleSpec(newBubbleSpec);
        // enhance: we could possibly do something here to make the appropriate
        // shapes for childBubble and the tail that links it to the previous bubble.
        // However, currently our only client always does a fresh convertBubbleJsonToCanvas
        // after making a new child. That will automatically sort things out.
        // Note that getting all the shapes updated properly could be nontrivial
        // if childElement already has a bubble...it may need to change shape, lose tails,
        // change other properties,...
    }

    private static getLastInFamily(familyLevel: number | undefined): Bubble {
        const family = Comical.allBubbles
            .filter(
                x =>
                    x.getBubbleSpec().level === familyLevel &&
                    x.getBubbleSpec().order
            )
            .sort(
                (a, b) => a.getBubbleSpec().order! - b.getBubbleSpec().order!
            );
        // we set order on parentBubble, so there is at least one in the family.
        return family[family.length - 1];
    }

    public static findChild(bubble: Bubble): Bubble | undefined {
        const familyLevel = bubble.getSpecLevel();
        const orderWithinFamily = bubble.getBubbleSpec().order;
        if (!orderWithinFamily) {
            return undefined;
        }
        const family = Comical.allBubbles
            .filter(
                x =>
                    x.getBubbleSpec().level === familyLevel &&
                    x.getBubbleSpec().order &&
                    x.getBubbleSpec().order! > orderWithinFamily
            )
            .sort(
                (a, b) => a.getBubbleSpec().order! - b.getBubbleSpec().order!
            );
        if (family.length > 0) {
            return family[0];
        }
        return undefined;
    }

    // Return the parents of the bubble. The first item in the array
    // is the earliest ancestor (if any); any intermediate bubbles are returned too.
    public static findParents(bubble: Bubble): Bubble[] {
        const familyLevel = bubble.getSpecLevel();
        const orderWithinFamily = bubble.getBubbleSpec().order;
        if (!orderWithinFamily) {
            return [];
        }
        return Comical.allBubbles
            .filter(
                x =>
                    x.getBubbleSpec().level === familyLevel &&
                    x.getBubbleSpec().order &&
                    x.getBubbleSpec().order! < orderWithinFamily
            )
            .sort(
                (a, b) => a.getBubbleSpec().order! - b.getBubbleSpec().order!
            );
    }

    public static bubbleVersion = "1.0";
}

// planned next steps
// 1. When we wrap a shape around an element, record the shape as the data-bubble attr, a block of json as indicted in the design doc.
// Tricks will be needed if it is an arbitrary SVG.
// 2. Add function ConvertSvgToCanvas(parent). Does more or less the opposite of ConvertCanvasToSvg,
// but using the data-X attributes of children of parent that have them to initialize the canvas paper elements.
// Enhance test code to make Finish button toggle between Save and Edit.
// (Once the logic to create a canvas as an overlay on a parent is in place, can probably get all the paper.js
// stuff out of the test code.)
