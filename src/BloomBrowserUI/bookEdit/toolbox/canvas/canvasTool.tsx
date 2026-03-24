import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import * as ReactDOM from "react-dom";
import { kCanvasToolId } from "../toolIds";
import { EnableAllImageEditing } from "../../js/bloomImages";
import { getCanvasElementManager } from "./canvasElementUtils";
import $ from "jquery";
import type { CanvasElementManager } from "../../js/CanvasElementManager";
import CanvasToolControls from "./CanvasToolControls";

// Possibly wants to be CanvasElementTool, but we may think of a better UI name and want to use that instead, so leaving for now.
export class CanvasTool extends ToolboxToolReactAdaptor {
    public static theOneCanvasTool: CanvasTool | undefined;

    public callOnNewPageReady: () => void | undefined;

    public constructor() {
        super();

        CanvasTool.theOneCanvasTool = this;
    }

    public makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");
        root.setAttribute("class", "CanvasBody");

        ReactDOM.render(<CanvasToolControls />, root);
        return root as HTMLDivElement;
    }

    public id(): string {
        return kCanvasToolId;
    }

    public featureName? = kCanvasToolId;

    public isExperimental(): boolean {
        return false;
    }

    public beginRestoreSettings(_settings: string): JQueryPromise<void> {
        // Nothing to do, so return an already-resolved promise.
        const result = $.Deferred<void>();
        result.resolve();
        return result;
    }

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
