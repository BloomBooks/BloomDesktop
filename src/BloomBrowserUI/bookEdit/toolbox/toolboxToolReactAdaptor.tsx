import { renderRoot } from "../../utils/reactRender";
import { ITool, IToolboxSettings } from "./toolbox";
import { ReactElement } from "react";
import { isPageBloomGame } from "./games/GameInfo";
import { getBloomPageElement } from "../../utils/shared";

// Provides a base class with some common code for react-based tools that live
// in Bloom's Edit Page Toolbox.
export default abstract class ToolboxToolReactAdaptor implements ITool {
    imageUpdated(_img: HTMLImageElement | undefined): void {
        // does nothing by default
    }
    public abstract makeRootElement(): HTMLDivElement;
    public abstract id(): string;

    public requiresToolId(): boolean {
        return false;
    }

    /**
     * The URL of the icon for this tool's toolbox section header. Tools that don't show one
     * (the "More..." section, and tools that only appear on pages that ask for them) don't
     * override this.
     */
    public iconPath(): string | undefined {
        return undefined;
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
    // See ITool.featureName. Tools that require a subscription set this.
    public featureName?: string;

    /**
     * Restores this tool's state from the book's saved toolbox settings (see ITool).
     * Tools that save no state don't override this.
     */
    public beginRestoreSettings(_settings: IToolboxSettings): Promise<void> {
        // Nothing to do, so return an already-resolved promise.
        return Promise.resolve();
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
    public configureElements(_container: HTMLElement) {}
    /* eslint-enable @typescript-eslint/no-empty-function */

    // Note: the general helpers for getting at the page being edited (the page iframe, its
    // body, the .bloom-page element, and whether the page is xmatter) live in
    // utils/shared.ts. The few below remain here because they are about particular things
    // tools do with the page: attributes we deliberately store URL-encoded, and games.

    /** The value of an attribute of the .bloom-page element that we store URL-encoded. */
    public static getBloomPageAttrDecoded(name: string): string | undefined {
        const page = getBloomPageElement();
        if (!page) return undefined;
        const v = page.getAttribute(name);
        return v ? decodeURIComponent(v) : undefined;
    }

    /** Stores a value, URL-encoded, in an attribute of the .bloom-page element. */
    public static encodeAndSetPageAttr(
        name: string,
        unencodedValue: string,
    ): void {
        const page = getBloomPageElement();
        if (!page) return;
        page.setAttribute(name, encodeURIComponent(unencodedValue));
    }

    /** Is the page currently being edited one of our games? */
    public static isCurrentPageABloomGame(): boolean {
        const page = getBloomPageElement();
        if (!page) {
            return false; // huh??
        }
        return isPageBloomGame(page);
    }
}
