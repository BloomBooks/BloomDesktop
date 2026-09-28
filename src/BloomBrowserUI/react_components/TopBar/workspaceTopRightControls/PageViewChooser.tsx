import { css } from "@emotion/react";
import * as React from "react";
import ToggleButton from "@mui/material/ToggleButton";
import ToggleButtonGroup from "@mui/material/ToggleButtonGroup";
import { BloomTooltip } from "../../BloomToolTip";
import {
    ViewAllPagesIcon,
    ViewOnePageIcon,
} from "../../../bookEdit/js/PageViewIcons";
import {
    getEditablePageBundleExports,
    getWorkspaceBundleExports,
} from "../../../bookEdit/js/workspaceFrames";
import { get, postJson } from "../../../utils/bloomApi";
import {
    getSpreadShape,
    pagesAcrossASpread,
} from "../../../bookEdit/js/bookGridLayout";

type PageView = "one" | "all";

const kBorder = "1px solid rgba(0, 0, 0, 0.4)";

// In the Edit tab, chooses between editing one page by itself and seeing all the pages of the book
// around it. The pages are drawn in the page frame (see bookGridView.ts), so the choice is stored
// where the next page will find it (workspaceRoot.ts) and handed to the page being edited now.
export const PageViewChooser: React.FunctionComponent = () => {
    const [view, setView] = React.useState<PageView>(
        getWorkspaceBundleExports().isShowingOtherPages() ? "all" : "one",
    );

    const choose = (newView: PageView | null) => {
        // Clicking the segment that is already chosen gives null; the choice stays as it is.
        if (!newView || newView === view) return;
        setView(newView);
        getWorkspaceBundleExports().storeShowingOtherPages(newView === "all");
        getEditablePageBundleExports()?.setShowingOtherPages(newView === "all");
        if (newView === "all") zoomOutToFitASpread();
    };

    return (
        <ToggleButtonGroup
            exclusive
            size="small"
            value={view}
            onChange={(_event, newView: PageView | null) => choose(newView)}
            data-testid="page-view-control"
            css={css`
                height: 20px;
                margin-right: 3px;
                // Level with the bottom of the zoom percentage, clear of the bar's edge.
                margin-bottom: 2px;
                border: ${kBorder};
                border-radius: 3px;
                overflow: hidden;
                // BloomTooltip wraps each segment in a span.
                > span {
                    display: flex;
                }
                > span + span {
                    border-left: ${kBorder};
                }
                && .MuiToggleButton-root {
                    width: 22px;
                    height: 100%;
                    padding: 0;
                    border: none;
                    border-radius: 0;
                    color: #1a1a1a;
                    background-color: transparent;
                    opacity: 0.55;
                    &.Mui-selected {
                        background-color: rgba(255, 255, 255, 0.35);
                        opacity: 1;
                    }
                }
            `}
        >
            <BloomTooltip
                tip={{
                    l10nKey: "EditTab.PageView.OnePage.Tooltip",
                    english: "Show one page at a time",
                }}
                placement="bottom"
            >
                <ToggleButton value="one" data-testid="view-one-page">
                    <span
                        css={css`
                            display: flex;
                            svg {
                                width: 10px;
                                height: 13px;
                            }
                        `}
                    >
                        <ViewOnePageIcon />
                    </span>
                </ToggleButton>
            </BloomTooltip>
            <BloomTooltip
                tip={{
                    l10nKey: "EditTab.PageView.AllPages.Tooltip",
                    english: "Show all pages",
                }}
                placement="bottom"
            >
                <ToggleButton value="all" data-testid="view-all-pages">
                    <span
                        css={css`
                            display: flex;
                            svg {
                                width: 11px;
                                height: 13px;
                            }
                        `}
                    >
                        <ViewAllPagesIcon />
                    </span>
                </ToggleButton>
            </BloomTooltip>
        </ToggleButtonGroup>
    );
};

// Seeing all the pages is meant to show at least the whole spread holding the page being edited
// (two pages side by side, except in calendars and books sized for a screen; see
// bookGridLayout.ts), so if the page frame is too narrow for it at the current zoom, zoom out until
// it is not (to a whole step of 10%, and no further than Bloom allows). The full height of the
// spread does not have to fit.
function zoomOutToFitASpread(): void {
    const frame = document.getElementById("page") as HTMLIFrameElement;
    const pageDocument = frame.contentDocument!;
    const page = pageDocument.querySelector(
        ".bloom-page",
    ) as HTMLElement | null;
    // While the Edit tab is changing pages the frame does not hold a page yet. The choice itself
    // is already stored, so all that is lost is the zoom.
    if (!page) return;
    const spreadWidth =
        pagesAcrossASpread(getSpreadShape(page)) * page.offsetWidth;
    // The width the zoomed page can use; see setZoom() in workspaceRoot.ts.
    const availableWidth = pageDocument.body.clientWidth - 5;
    get("workspace/topRight/zoom", (result) => {
        const zoomInfo = result.data as { zoom: number; minZoom: number };
        const fittingZoom = Math.max(
            zoomInfo.minZoom,
            Math.floor((availableWidth / spreadWidth) * 10) * 10,
        );
        if (fittingZoom < zoomInfo.zoom) {
            postJson("workspace/topRight/zoom", { zoom: fittingZoom });
        }
    });
}
