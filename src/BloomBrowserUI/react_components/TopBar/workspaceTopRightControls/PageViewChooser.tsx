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
import { getAsync, postJsonAsync } from "../../../utils/bloomApi";
import { reportError } from "../../../lib/errorHandler";
import type { PageViewMode } from "../../../bookEdit/js/pageViewModeSetting";
import {
    getSpreadShape,
    kSpreadGap,
    pagesAcrossASpread,
} from "../../../bookEdit/js/bookGridLayout";

const kBorder = "1px solid rgba(0, 0, 0, 0.4)";

// In the Edit tab, chooses between editing one page by itself and seeing all the pages of the book
// around it. The pages are drawn in the page frame (see bookGridView.ts), so the choice is stored
// where the next page will find it (pageViewModeSetting.ts) and handed to the page being edited now.
export const PageViewChooser: React.FunctionComponent = () => {
    const getChosenMode = (): PageViewMode =>
        getWorkspaceBundleExports().isShowingOtherPages() ? "all" : "one";
    const [mode, setMode] = React.useState<PageViewMode>(getChosenMode);
    // The choice Bloom kept can arrive after this is first shown.
    React.useEffect(
        () =>
            getWorkspaceBundleExports().listenForPageViewModeSetting(() =>
                setMode(getChosenMode()),
            ),
        [],
    );

    const choose = (newMode: PageViewMode | null) => {
        // Clicking the segment that is already chosen gives null; the choice stays as it is.
        if (!newMode || newMode === mode) return;
        setMode(newMode);
        getWorkspaceBundleExports().storeShowingOtherPages(newMode === "all");
        getEditablePageBundleExports()?.setShowingOtherPages(newMode === "all");
        switchToZoomOfMode(mode, newMode);
    };

    return (
        <ToggleButtonGroup
            exclusive
            size="small"
            value={mode}
            onChange={(_event, newMode: PageViewMode | null) => choose(newMode)}
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

type ZoomInfo = { zoom: number; minZoom: number };

// Each page view mode keeps the zoom the user last had in it, so that going back to one page undoes the
// zooming out that seeing all the pages may have needed. Going to all pages zooms out further if
// the zoom kept for it would not fit a spread. Bloom's own zoom setting is the zoom of
// the mode showing now; the zoom of the other mode is kept with the choice of mode, across Bloom
// sessions (see pageViewModeSetting.ts).
// Switches run one after another: each reads Bloom's zoom as the switch before it left it, so a
// quick change of mode and back cannot store one mode's zoom as the other's.
function switchToZoomOfMode(
    oldMode: PageViewMode,
    newMode: PageViewMode,
): void {
    // A switch that fails is reported, and the switches after it still run.
    zoomSwitchesDone = zoomSwitchesDone
        .then(() => switchZoomNow(oldMode, newMode))
        .catch((e: Error) => reportError(e.message, e.stack));
}

// Settles when every zoom switch asked for so far has reached Bloom.
let zoomSwitchesDone: Promise<void> = Promise.resolve();

// Store the zoom of the mode being left, and give Bloom the zoom of the mode being shown.
async function switchZoomNow(
    oldMode: PageViewMode,
    newMode: PageViewMode,
): Promise<void> {
    const zoomInfo = (await getAsync("workspace/zoom")).data as ZoomInfo;
    getWorkspaceBundleExports().storeZoomOfMode(oldMode, zoomInfo.zoom);
    const zoomOfNewMode =
        getWorkspaceBundleExports().getStoredZoomOfMode(newMode);
    let zoom = zoomInfo.zoom;
    if (zoomOfNewMode !== undefined && zoomOfNewMode !== zoom) {
        await postJsonAsync("workspace/zoom", {
            zoom: zoomOfNewMode,
        });
        zoom = zoomOfNewMode;
    }
    // The zoom kept for all pages may be too large for a spread of this book, or for this window.
    if (newMode === "all") {
        await zoomOutToFitASpread({ zoom, minZoom: zoomInfo.minZoom });
    }
}

// Seeing all the pages is meant to show at least the whole spread holding the page being edited
// (two pages side by side, except in calendars and books sized for a screen; see
// bookGridLayout.ts), so if the page frame is too narrow for it at the given zoom, zoom out until
// it is not (to a whole step of 10%, and no further than Bloom allows). The full height of the
// spread does not have to fit.
async function zoomOutToFitASpread(zoomInfo: ZoomInfo): Promise<void> {
    const frame = document.getElementById("page") as HTMLIFrameElement;
    const pageDocument = frame.contentDocument!;
    const page = pageDocument.querySelector(
        ".bloom-page",
    ) as HTMLElement | null;
    // While the Edit tab is changing pages the frame does not hold a page yet. The choice itself
    // is already stored, so all that is lost is the zoom.
    if (!page) return;
    // With the room the grid leaves at its two edges, which together is at least one kSpreadGap
    // (see layoutGrid() in bookGridView.ts).
    const spreadWidth =
        pagesAcrossASpread(getSpreadShape(page)) * page.offsetWidth +
        kSpreadGap;
    // The width the zoomed page can use; see setZoom() in workspaceRoot.ts.
    const availableWidth = pageDocument.body.clientWidth - 5;
    const fittingZoom = Math.max(
        zoomInfo.minZoom,
        Math.floor((availableWidth / spreadWidth) * 10) * 10,
    );
    if (fittingZoom < zoomInfo.zoom) {
        await postJsonAsync("workspace/zoom", { zoom: fittingZoom });
    }
}
