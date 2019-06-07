import * as React from "react";
import * as ReactDOM from "react-dom";
import { ITool, IReactTool } from "./toolbox";
import { ReactElement } from "react";

// Provides a base class with some common code for react-based tools that live
// in Bloom's Edit Page Toolbox.
export default abstract class ToolboxToolReactAdaptor
    implements ITool, IReactTool {
    public hasRestoredSettings: boolean;
    public abstract makeRootElement(): HTMLDivElement;
    public abstract id(): string;

    protected adaptReactElement(element: ReactElement<any>): HTMLDivElement {
        // We need a wrapperDiv to hand back to our the toolbox because react wants some freedom to render asynchronously.
        // So we just create empty div now to hand back to the toolbox, and ask React to render into it eventually.
        const wrapperDiv = document.createElement("div");
        ReactDOM.render(element, wrapperDiv);
        return wrapperDiv as HTMLDivElement;
    }
    public isAlwaysEnabled(): boolean {
        return false;
    }
    public isExperimental(): boolean {
        return false;
    }
    public toolRequiresEnterprise(): boolean {
        return false;
    }
    public beginRestoreSettings(settings: string): JQueryPromise<void> {
        // Nothing to do, so return an already-resolved promise.
        const result = $.Deferred<void>();
        result.resolve();
        return result;
    }
    public showTool() {}
    public hideTool() {}
    public updateMarkup() {}
    public newPageReady() {}
    public detachFromPage() {}
    public configureElements(container: HTMLElement) {}
    public finishToolLocalization(pane: HTMLElement) {}

    public static getPageFrame(): HTMLIFrameElement {
        return parent.window.document.getElementById(
            "page"
        ) as HTMLIFrameElement;
    }

    // The body of the editable page, a root for searching for document content.
    public static getPage(): HTMLElement | null {
        const page = this.getPageFrame();
        if (!page || !page.contentWindow) return null;
        return page.contentWindow.document.body;
    }

    public static getBloomPage(): HTMLElement | null {
        const page = this.getPage();
        if (!page) return null;
        return page.querySelector(".bloom-page") as HTMLElement;
    }

    public static getBloomPageAttr(name: string): string | null {
        const page = this.getBloomPage();
        if (!page) return null;
        return page.getAttribute(name);
    }

    public static setBloomPageAttr(name: string, val: string): void {
        const page = this.getBloomPage();
        if (!page) return;
        page.setAttribute(name, val);
    }
}
