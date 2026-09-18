import { get } from "../../../../utils/bloomApi";
import ToolboxToolReactAdaptor from "../../toolboxToolReactAdaptor";
import { isReaderToolEnabledOnCurrentPage } from "../readerToolPageState";
import { beginInitializeLeveledReaderTool } from "../readerTools";
import { getTheOneReaderToolsModel } from "../readerToolsModel";
import { LeveledReaderToolControls } from "./LeveledReaderToolControls";
import { IToolboxSettings } from "../../toolbox";

// This class renders the LeveledReaderToolControls React component
// in the toolbox, and implements all the functionality/logic needed
// for detaching the tool, reattaching the tool, updating the markup,
// and restoring the current tool state
export class LeveledReaderTool extends ToolboxToolReactAdaptor {
    // renders the leveled reader React tool, for the toolbox to display in this
    // tool's accordion section
    public renderPanel(): JSX.Element {
        return (
            <div>
                <LeveledReaderToolControls />
            </div>
        );
    }

    // returns the id for this tool, which is used in the
    // bootstrapping process
    public id(): string {
        return "leveledReader";
    }

    /** The icon for this tool's section header in the toolbox. */
    public iconPath(): string {
        return "/bloom/images/steps-white.png";
    }

    // this function restores the level that was last saved,
    // as well as the data for that stage, so that the tool
    // doesn't restart at level 1 all the time
    public async beginRestoreSettings(
        settings: IToolboxSettings,
    ): Promise<void> {
        await beginInitializeLeveledReaderTool();
        // Despite the type, settings can be undefined/null at runtime when the tool is
        // activated for a book that has no saved leveled-reader settings. Guard before
        // indexing so we fall through to the default-level path instead of throwing an
        // unhandled promise rejection that aborts tool activation
        // (Sentry BLOOM-DESKTOP-FFH).
        const leveledReaderState = settings?.["leveledReaderState"];
        if (leveledReaderState) {
            // The true passed here prevents re-saving the state we just read.
            // One non-obvious implication is that simply opening a level-4 book
            // will not switch the default level for new books to 4. That only
            // happens when you CHANGE the level in the toolbox.
            getTheOneReaderToolsModel().setLevelNumber(
                parseInt(leveledReaderState, 10),
                true,
            );
            return;
        }
        await new Promise<void>((resolve) => {
            get(
                "readers/io/defaultLevel",
                (result) => {
                    // Presumably a brand new book. We'd better save the settings we come up with in it.
                    getTheOneReaderToolsModel().setLevelNumber(
                        parseInt(result.data, 10),
                    );
                    resolve();
                },
                () => resolve(),
            );
        });
    }

    public configureElements(_container: HTMLElement) {
        // Leveled reader makes use of the setup in this.setupReaderKeyAndFocusHandlers(container).
        // This would be the place to call it, but it is called by decodableReaderTool.tsx' configureElements().
        // And configureElements gets called for every tool, whether or not that tool is open,
        // so it always get initialized by decodableReaderTool.tsx, and we don't want to call it twice.
    }

    // this function sets up the markup type and all the data for a page when
    // that page has been opened, or when the tool has been opened.
    public newPageReady(): void {
        const model = getTheOneReaderToolsModel();
        model.clearWholeBookCache();
        model.setMarkupType(isReaderToolEnabledOnCurrentPage(true) ? 2 : 0);
        model.updateControlContents();
        // usually updateMarkup will do this, unless we are coming from showTool
        model.doMarkup();
    }

    // this function removes all markup from a page when either that page has been
    // closed or the tool has been closed.
    public detachFromPage(): void {
        getTheOneReaderToolsModel().setMarkupType(0);
    }

    // this function is called whenever the user types something on the page, since
    // the thing that the user typed may need to receieve markup or have markup taken
    // away from it
    public updateMarkup() {
        // Don't let this lower-level code create ckeditor bookmarks in this case.
        // We've already created them in toolbox.ts which calls this.
        const createCkEditorBookMarks = false;
        getTheOneReaderToolsModel().doMarkup(createCkEditorBookMarks);
    }

    public showTool() {
        // change markup based on visible options
        getTheOneReaderToolsModel().setCkEditorLoaded(); // we don't call showTool until it is.
        // Toggle render is handled in newPageReady(), where page reader classes are settled.
    }
}
