import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import { kCanvasToolId } from "../toolIds";
import { EnableAllImageEditing } from "../../js/bloomImages";
import { getCanvasElementManager } from "./canvasElementPageBridge";
import type { CanvasElementManager } from "../../js/canvasElementManager/CanvasElementManager";
import CanvasToolControls from "./CanvasToolControls";

// Possibly wants to be CanvasElementTool, but we may think of a better UI name and want to use that instead, so leaving for now.
export class CanvasTool extends ToolboxToolReactAdaptor {
    public static theOneCanvasTool: CanvasTool | undefined;

    public callOnNewPageReady: () => void | undefined;

    public constructor() {
        super();

        CanvasTool.theOneCanvasTool = this;
    }

    public renderPanel(): JSX.Element {
        return (
            <div className="CanvasBody">
                <CanvasToolControls />
            </div>
        );
    }

    public id(): string {
        return kCanvasToolId;
    }

    /** The icon for this tool's section header in the toolbox. */
    public iconPath(): string {
        return "/bloom/bookEdit/toolbox/canvas/Canvas%20Icon.svg";
    }

    public featureName? = kCanvasToolId;

    public newPageReady() {
        const canvasElementManager = getCanvasElementManager();
        if (!canvasElementManager) {
            // probably the toolbox just finished loading before the page.
            // No clean way to fix this
            window.setTimeout(() => this.newPageReady(), 100);
            return;
        }

        if (this.callOnNewPageReady) {
            this.callOnNewPageReady();
        } else {
            console.assert(
                false,
                "CallOnNewPageReady is always expected to be defined but it is not.",
            );
        }
    }

    public detachFromPage() {
        const canvasElementManager = getCanvasElementManager();
        if (canvasElementManager) {
            // For now we are leaving canvas element editing on, because even with the toolbox hidden,
            // the user might edit text, delete canvas elements, move handles, etc.
            // We turn it off only when about to save the page.
            //CanvasElementManager.turnOffBubbleEditing();

            EnableAllImageEditing();
            canvasElementManager.detachCanvasElementChangeNotification(
                "canvasElement",
            );
        }
    }

    // In the process of moving this to a minimal-dependency utility file, but a lot of
    // code still expects to find it here.
    public static getCanvasElementManager(): CanvasElementManager | undefined {
        return getCanvasElementManager();
    }
}
