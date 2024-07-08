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

    public requiresToolId(): boolean {
        return false;
    }

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
    // We need these to implement the interface, but don't need them to do anything.
    /* eslint-disable @typescript-eslint/no-empty-function */
    public showTool() {}
    public hideTool() {}
    public updateMarkup() {}
    public async updateMarkupAsync() {
        // If you implement this, you may need to do something like cleanUpCkEditorHtml() in audioRecording.ts.
        throw "not implemented...you must override this if you make isUpdateMarkupAsync return true";
        return () => {};
    }
    public isUpdateMarkupAsync(): boolean {
        return false;
    }
    public newPageReady() {}
    public detachFromPage() {}
    public configureElements(container: HTMLElement) {}
    public finishToolLocalization(pane: HTMLElement) {}
    /* eslint-enable @typescript-eslint/no-empty-function */

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

    public static getBloomPageAttrDecoded(name: string): string | undefined {
        const page = this.getBloomPage();
        if (!page) return undefined;
        const v = page.getAttribute(name);
        return v ? decodeURIComponent(v) : undefined;
    }

    public static encodeAndSetPageAttr(
        name: string,
        unencodedValue: string
    ): void {
        const page = this.getBloomPage();
        if (!page) return;
        page.setAttribute(name, encodeURIComponent(unencodedValue));
    }

    public static isXmatter(): boolean {
        const pageClass = this.getBloomPageAttrDecoded("class");
        return !pageClass
            ? false // paranoia
            : pageClass.indexOf("bloom-frontMatter") >= 0 ||
                  pageClass.indexOf("bloom-backMatter") >= 0;
    }
}
