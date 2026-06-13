import * as React from "react";
import { useEffect, useState } from "react";
import { ThemeProvider } from "@mui/material/styles";
import { TableMenu } from "bloom-table";
import { renderRoot } from "../../../utils/reactRender";
import { toolboxTheme } from "../../../bloomMaterialUITheme";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import { kTableToolId } from "../toolIds";
import { getTableApi } from "./tableToolPageBridge";

// Find the editable page's document. The toolbox and page are sibling iframes
// under the workspace root, so reach the page frame via the parent window.
function getPageDocument(): Document | null {
    const frame = window.parent?.document.getElementById(
        "page",
    ) as HTMLIFrameElement | null;
    return frame?.contentWindow?.document ?? null;
}

// Hosts the bloom-table library's TableMenu in the toolbox. The menu's
// structural operations are injected from the page frame (getTableApi), so they
// run in the realm where the tables are attached. We only track which cell is
// selected (in the page frame) and feed it to the menu.
const TableToolControls: React.FunctionComponent<{
    pageGeneration: number;
}> = (props) => {
    const [currentCell, setCurrentCell] = useState<HTMLElement | null>(null);

    useEffect(() => {
        // A new page may have loaded; start fresh and bind to its document.
        setCurrentCell(null);
        const pageDoc = getPageDocument();
        if (!pageDoc) return;
        const onFocusIn = (e: Event) => {
            const target = e.target as HTMLElement | null;
            const cell = target?.closest(".cell") as HTMLElement | null;
            // Leave the selection as-is on blur so the menu stays usable when
            // focus moves to it (mirrors the library demo's Toolbar).
            if (cell) setCurrentCell(cell);
        };
        pageDoc.addEventListener("focusin", onFocusIn, true);
        return () => pageDoc.removeEventListener("focusin", onFocusIn, true);
    }, [props.pageGeneration]);

    // The page-frame operations API. Until the page bundle is ready this is
    // undefined; in that case we show the menu's "click in a cell" placeholder
    // rather than risk routing ops through the toolbox's own (unattached) module.
    const tableApi = getTableApi();

    return (
        <ThemeProvider theme={toolboxTheme}>
            <TableMenu
                currentCell={tableApi ? currentCell : null}
                tableApi={tableApi}
            />
        </ThemeProvider>
    );
};

export class TableTool extends ToolboxToolReactAdaptor {
    private root: HTMLDivElement | undefined;
    private pageGeneration = 0;

    public makeRootElement(): HTMLDivElement {
        this.root = document.createElement("div") as HTMLDivElement;
        this.renderRoot();
        return this.root;
    }

    private renderRoot(): void {
        if (!this.root) return;
        renderRoot(
            <TableToolControls pageGeneration={this.pageGeneration} />,
            this.root,
        );
    }

    public id(): string {
        return kTableToolId;
    }

    public isExperimental(): boolean {
        return true;
    }

    public requiresToolId(): boolean {
        return false;
    }

    // A new page loaded: re-render so the controls re-bind their focus listener
    // to the new page document. Tables themselves are attached in the page frame
    // by SetupTableEditing.
    public newPageReady() {
        this.pageGeneration++;
        this.renderRoot();
    }

    // No detachFromPage() override needed: the menu holds no per-page resources
    // (tables are detached by TeardownTableEditing in the page frame), and the
    // controls' effect removes its focus listener when the page generation
    // changes or it unmounts. The base class's empty detachFromPage() suffices.
}
