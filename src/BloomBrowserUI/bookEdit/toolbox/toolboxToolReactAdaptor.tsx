import { renderRoot } from "../../utils/reactRender";
import $ from "jquery";
import { ITool, IReactTool } from "./toolbox";
import { ReactElement } from "react";
import { isPageBloomGame } from "./games/GameInfo";

// Provides a base class with some common code for react-based tools that live
// in Bloom's Edit Page Toolbox.
export default abstract class ToolboxToolReactAdaptor
    implements ITool, IReactTool
{
    imageUpdated(_img: HTMLImageElement | undefined): void {
        // does nothing by default
    }
    public hasRestoredSettings: boolean;
    public abstract makeRootElement(): HTMLDivElement;
    public abstract id(): string;

    public requiresToolId(): boolean {
        return false;
    }

    protected adaptReactElement(
        element: ReactElement<unknown>,
    ): HTMLDivElement {
        // We need a wrapperDiv to hand back to our the toolbox because react wants some freedom to render asynchronously.
        // So we just create empty div now to hand back to the toolbox, and ask React to render into it eventually.
        const wrapperDiv = document.createElement("div");
        renderRoot(element, wrapperDiv);
        return wrapperDiv as HTMLDivElement;
    }
    public isAlwaysEnabled(): boolean {
        return false;
    }
    public isExperimental(): boolean {
        return false;
    }
    public featureName?: string;

    public beginRestoreSettings(_settings: string): JQueryPromise<void> {
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
    // Most tools' editing markup is either marked bloom-ui (which the C# save pipeline strips) or
    // lives outside the page div (which is never saved), so they have nothing to remove. See
    // ITool.removeToolMarkup for what to do if yours does.
    public removeToolMarkup(_pageOrClone: HTMLElement): void {}
    public configureElements(_container: HTMLElement) {}
    public finishToolLocalization(_pane: HTMLElement) {}
    /* eslint-enable @typescript-eslint/no-empty-function */

    private removedToolMarkupWhileDetaching = false;

    /// Take this tool's markup off the live page. A tool that has nothing live-only to clean up
    /// needs only to implement removeToolMarkup(); it gets this for free, and the save path gets
    /// the identical cleanup by calling the same method on a clone. If you do override this to add
    /// live-only teardown, call super.detachFromPage() at the point where the markup should come
    /// off — see ITool.detachFromPage.
    public detachFromPage(): void {
        this.removedToolMarkupWhileDetaching = true;
        const bloomPage = ToolboxToolReactAdaptor.getBloomPage();
        if (bloomPage) {
            this.removeToolMarkup(bloomPage);
        }
    }

    // See ITool.didRemoveToolMarkupWhileDetaching. Reading it also resets it, so that each detach
    // is judged on its own.
    public didRemoveToolMarkupWhileDetaching(): boolean {
        const result = this.removedToolMarkupWhileDetaching;
        this.removedToolMarkupWhileDetaching = false;
        return result;
    }

    public static getPageFrame(): HTMLIFrameElement {
        return parent.window.document.getElementById(
            "page",
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
        unencodedValue: string,
    ): void {
        const page = this.getBloomPage();
        if (!page) return;
        page.setAttribute(name, encodeURIComponent(unencodedValue));
    }

    // Generally returns true if the page is xmatter. Some callers (enabling canvas tool) want to treat
    // a custom page as not being xmatter, so we support an override for that. Could be just a boolean,
    // but using an object with a named field makes it clearer what the argument is for where it is used.
    public static isXmatter(
        args: { returnFalseForCustomPage: boolean } = {
            returnFalseForCustomPage: false,
        },
    ): boolean {
        const pageClass = this.getBloomPageAttrDecoded("class");
        if (!pageClass) return false; // paranoia
        if (
            args?.returnFalseForCustomPage &&
            pageClass.indexOf("bloom-customLayout") >= 0
        ) {
            return false;
        }
        return (
            pageClass.indexOf("bloom-frontMatter") >= 0 ||
            pageClass.indexOf("bloom-backMatter") >= 0
        );
    }

    public static isCurrentPageABloomGame(): boolean {
        const page = this.getBloomPage();
        if (!page) {
            return false; // huh??
        }
        return isPageBloomGame(page);
    }
}
