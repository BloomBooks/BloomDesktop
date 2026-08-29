// The little toolbar that appears just below a text box too small to hold the affordances
// Bloom usually draws inside one. It carries the two things that box gives up: the format
// cog of the editable that has focus, and the name of that editable's language. A box that
// is a cell of a table also gets a "..." button, which opens the table's own Cell menu, the
// menu a right-click in the cell opens.
//
// Which boxes are "too small" is decided in translationGroupSizeMarking.ts, and the rules
// that stop drawing the language name and the cog inside such a box live in editMode.less.
//
// This runs in the page iframe, as part of the page's own setup. The toolbar is rendered
// into a div of the page document's body, outside the .bloom-page, for the same reason the
// canvas element's context controls and the calendar grid's button are: what Bloom saves is
// the page element and what is inside it, so an affordance kept outside it can never end up
// in the book.

import { css } from "@emotion/react";
import * as React from "react";
import { useLayoutEffect } from "react";
import { default as MenuIcon } from "@mui/icons-material/MoreHorizSharp";
import { ThemeProvider } from "@mui/material/styles";
import { kBloomBlue, lightTheme } from "../../bloomMaterialUITheme";
import { useL10n } from "../../react_components/l10nHooks";
import { renderRoot, unmountRoot } from "../../utils/reactRender";
import { CogIcon } from "./CogIcon";
import { GetEditor, getLanguageNameToShow } from "./bloomEditing";
import { Point } from "./point";
import { kTooSmallForInBoxAffordancesClass } from "./translationGroupSizeMarking";

const kHostId = "small-translation-group-toolbar";

// The host is much wider than the bar it holds, and the bar is centered in it, which is how
// the canvas element context controls stay centered on what they belong to as they change
// width. Anything wider than the bar can ever be will do.
const kHostWidth = 300;

// How far the bar sits from the edge of the box, in the page's own pixels.
const kGapFromBox = 3;

// How positionToolbar finds the bar inside the host.
const kBarClass = "small-translation-group-toolbar-bar";

// The group whose editable has the focus, and so whose toolbar is showing. Undefined when
// no toolbar is showing.
let currentGroup: HTMLElement | undefined;

let listenersInstalled = false;

/**
 * Make the toolbar follow the focus on this page.
 *
 * Called from SetupElements in bloomEditing.ts on every page load. The listeners go on the
 * page document rather than on the container, so that they serve an editable that does not
 * exist yet: a table makes new cells long after the page has loaded.
 */
export function setupSmallTranslationGroupToolbar(
    container: HTMLElement,
): void {
    if (listenersInstalled) return;
    listenersInstalled = true;
    const pageDocument = container.ownerDocument;
    pageDocument.body.addEventListener("focusin", onFocusIn);
    pageDocument.body.addEventListener("focusout", onFocusOut);
    const pageWindow = pageDocument.defaultView;
    pageWindow?.addEventListener("resize", positionToolbar);
    // Capture, because the thing that scrolls is usually a div inside the page rather than
    // the window, and a scroll event does not bubble.
    pageWindow?.addEventListener("scroll", positionToolbar, true);
}

/**
 * Take the toolbar and its listeners down. Called from removeEditingDebris in
 * bloomEditing.ts before we navigate away from the page.
 */
export function teardownSmallTranslationGroupToolbar(
    container: HTMLElement,
): void {
    const pageDocument = container.ownerDocument;
    pageDocument.body.removeEventListener("focusin", onFocusIn);
    pageDocument.body.removeEventListener("focusout", onFocusOut);
    const pageWindow = pageDocument.defaultView;
    pageWindow?.removeEventListener("resize", positionToolbar);
    pageWindow?.removeEventListener("scroll", positionToolbar, true);
    listenersInstalled = false;
    currentGroup = undefined;
    const host = pageDocument.getElementById(kHostId);
    if (host) {
        unmountRoot(host);
        host.remove();
    }
}

/**
 * The translation group whose toolbar this element should get, if it should get one: only
 * an editable of a group that has been marked too small for its own affordances has one.
 * Exported for testing.
 */
export function groupWantingToolbar(
    target: EventTarget | null,
): HTMLElement | undefined {
    const element = target as HTMLElement | null;
    if (!element?.closest) return undefined;
    const editable = element.closest<HTMLElement>(".bloom-editable");
    if (!editable) return undefined;
    const group = editable.closest<HTMLElement>(".bloom-translationGroup");
    if (!group?.classList.contains(kTooSmallForInBoxAffordancesClass))
        return undefined;
    return group;
}

function onFocusIn(event: FocusEvent): void {
    const group = groupWantingToolbar(event.target);
    if (!group) {
        hideToolbar();
        return;
    }
    showToolbar(
        (event.target as HTMLElement).closest<HTMLElement>(".bloom-editable")!,
        group,
    );
}

function onFocusOut(event: FocusEvent): void {
    // The focus may be on its way to another editable, or to nothing at all, and this event
    // arrives before we can tell which. Waiting a turn lets the focusin that may follow put
    // the toolbar where it belongs; if none follows, the check below takes it down.
    const pageDocument = (event.target as HTMLElement).ownerDocument;
    pageDocument.defaultView?.setTimeout(() => {
        const active = pageDocument.activeElement;
        if (currentGroup && active && currentGroup.contains(active)) return;
        if (!groupWantingToolbar(active)) hideToolbar();
    }, 0);
}

function showToolbar(editable: HTMLElement, group: HTMLElement): void {
    currentGroup = group;
    const host = getOrCreateHost(group.ownerDocument);
    host.style.visibility = "hidden"; // until the component has measured and placed it
    renderRoot(
        <SmallTranslationGroupToolbar editable={editable} group={group} />,
        host,
    );
}

function hideToolbar(): void {
    currentGroup = undefined;
    const host = document.getElementById(kHostId);
    if (!host) return;
    host.style.visibility = "hidden";
    // Emptied rather than unmounted, because the focus moves between boxes constantly and
    // a React root may be created only once for a given element.
    renderRoot(<React.Fragment />, host);
}

function getOrCreateHost(pageDocument: Document): HTMLElement {
    const existing = pageDocument.getElementById(kHostId);
    if (existing) return existing;
    const host = pageDocument.createElement("div");
    host.setAttribute("id", kHostId);
    pageDocument.body.appendChild(host);
    return host;
}

/**
 * Put the toolbar just below the box it belongs to, or above it when below will not do.
 *
 * The host is not inside the page, so it does not get the page's own scaling; it is given
 * the same transform the scaling container has, from its top left corner, and a width in
 * the page's own units, so that the bar stays the size of the page's other affordances at
 * any zoom.
 */
function positionToolbar(): void {
    const host = document.getElementById(kHostId);
    if (!host || !currentGroup?.isConnected) return;
    const bar = host.querySelector<HTMLElement>(`.${kBarClass}`);
    if (!bar) return;

    const scalingContainer = document.getElementById("page-scaling-container");
    host.style.transform = scalingContainer?.style.transform ?? "";
    host.style.transformOrigin = "top left";
    host.style.width = `${kHostWidth}px`;
    const scale = Point.getScalingFactor() || 1;
    const groupRect = currentGroup.getBoundingClientRect();

    // Centered on the box: half the host's width, in the scaled units the group's rect is
    // in, comes off the middle of the box.
    const centeredLeft =
        groupRect.left +
        window.scrollX +
        groupRect.width / 2 -
        (kHostWidth / 2) * scale;
    host.style.left = `${centeredLeft}px`;
    host.style.top = `${groupRect.bottom + window.scrollY + kGapFromBox * scale}px`;
    host.style.visibility = "visible";

    // Everything from here on reads the bar where those numbers have just put it.
    const pageRect = currentGroup
        .closest<HTMLElement>(".bloom-page")
        ?.getBoundingClientRect();
    if (pageRect) {
        const barRect = bar.getBoundingClientRect();
        let shift = 0;
        if (barRect.left < pageRect.left) shift = pageRect.left - barRect.left;
        else if (barRect.right > pageRect.right)
            shift = pageRect.right - barRect.right;
        if (shift !== 0) host.style.left = `${centeredLeft + shift}px`;
    }
    if (shouldGoAboveTheBox(bar, pageRect)) {
        host.style.top = `${
            groupRect.top +
            window.scrollY -
            bar.getBoundingClientRect().height -
            kGapFromBox * scale
        }px`;
    }
}

/**
 * True when the bar, where it now sits below the box, runs off the bottom of the page or
 * lands on top of the canvas element context controls. A table on a canvas has those
 * controls under its bottom edge, which is exactly where the bottom row's cells would put
 * their toolbars.
 */
function shouldGoAboveTheBox(
    bar: HTMLElement,
    pageRect: DOMRect | undefined,
): boolean {
    const barRect = bar.getBoundingClientRect();
    if (pageRect && barRect.bottom > pageRect.bottom) return true;
    const canvasControls = document.getElementById(
        "canvas-element-context-controls",
    );
    if (!canvasControls || canvasControls.style.visibility === "hidden")
        return false;
    // The element with the id is a wide, invisible box that only positions the bar within
    // it, exactly as our host does, so it is the bar inside that we must not land on.
    const controlsRect = (
        canvasControls.firstElementChild ?? canvasControls
    ).getBoundingClientRect();
    // A hidden or empty set of controls has no area, and so overlaps nothing.
    if (controlsRect.width === 0 || controlsRect.height === 0) return false;
    return (
        barRect.left < controlsRect.right &&
        barRect.right > controlsRect.left &&
        barRect.top < controlsRect.bottom &&
        barRect.bottom > controlsRect.top
    );
}

const SmallTranslationGroupToolbar: React.FunctionComponent<{
    editable: HTMLElement;
    group: HTMLElement;
}> = (props) => {
    // Placing the bar takes measuring it, so it happens after every render: the tooltip and
    // the language name can both change the bar's width after it first appears.
    useLayoutEffect(() => positionToolbar());

    const formatTooltip = useL10n(
        "Format text...",
        "EditTab.Toolbox.ComicTool.Options.Format",
    );
    const cellMenuTooltip = useL10n(
        "Cell options",
        "EditTab.Table.CellOptions",
    );

    // Worked out rather than read off data-languageTipContent: AddLanguageTags never puts
    // that attribute on a box narrower than 100px, and a box that narrow is most of what
    // this toolbar serves. A calendar month grid's cells had no attribute to read, so the
    // bar came up with no name in it at all.
    const langName = getLanguageNameToShow(props.editable) ?? "";
    // Only a cell of a table has a Cell menu to open.
    const cell = props.group.closest<HTMLElement>(".bloom-cell");
    const cellOfATable = cell?.closest(".bloom-table") ? cell : undefined;

    // A click is a mouse down and a mouse up, and the mouse down would take the focus off
    // the editable, which would take this toolbar down before the mouse up could act. So
    // both halves are handled here, and neither is allowed its default. This is what the
    // canvas element context controls do with their own menu button.
    const swallow = (e: React.MouseEvent): void => {
        e.preventDefault();
        e.stopPropagation();
    };
    const actOnMouseUp =
        (act: () => void) =>
        (e: React.MouseEvent): void => {
            swallow(e);
            act();
        };

    const openFormatDialog = (): void =>
        // The same call the in-box format cog makes; see AttachToBox in StyleEditor.ts.
        GetEditor().runFormatDialog(props.editable);

    const openCellMenu = (e: React.MouseEvent): void => {
        if (!cellOfATable) return;
        // bloom-table opens its Cell menu from a contextmenu on the cell, and takes the
        // place to draw the menu from the event's coordinates. Sending it one is how we get
        // the very menu a right-click in the cell gives, rather than a second menu of our
        // own that would have to be kept in step with it.
        const button = e.currentTarget as HTMLElement;
        const buttonRect = button.getBoundingClientRect();
        cellOfATable.dispatchEvent(
            new MouseEvent("contextmenu", {
                bubbles: true,
                cancelable: true,
                view: cellOfATable.ownerDocument.defaultView!,
                clientX: buttonRect.left,
                clientY: buttonRect.bottom,
            }),
        );
    };

    return (
        <ThemeProvider theme={lightTheme}>
            <div
                className={kBarClass}
                css={css`
                    background-color: white;
                    border: 0.757px solid rgba(255, 255, 255, 0.2);
                    box-shadow: 0px 0px 4px 0px rgba(0, 0, 0, 0.25);
                    border-radius: 4px;
                    display: flex;
                    align-items: center;
                    padding: 0px 6px;
                    margin: 0 auto;
                    width: fit-content;
                    // needed because the host it sits in is not clickable
                    pointer-events: all;
                    line-height: 0.8em;
                `}
            >
                <button
                    data-testid="small-translation-group-format-button"
                    title={formatTooltip}
                    aria-label={formatTooltip}
                    css={buttonCss}
                    onMouseDown={swallow}
                    onMouseUp={actOnMouseUp(openFormatDialog)}
                >
                    <CogIcon color="primary" />
                </button>
                {langName && (
                    <div
                        css={css`
                            color: ${kBloomBlue};
                            font-size: 10px;
                            margin: 0 4px;
                            white-space: nowrap;
                        `}
                    >
                        {langName}
                    </div>
                )}
                {cellOfATable && (
                    <button
                        data-testid="small-translation-group-cell-menu-button"
                        title={cellMenuTooltip}
                        aria-label={cellMenuTooltip}
                        css={buttonCss}
                        onMouseDown={swallow}
                        onMouseUp={(e) => {
                            swallow(e);
                            openCellMenu(e);
                        }}
                    >
                        <MenuIcon color="primary" />
                    </button>
                )}
            </div>
        </ThemeProvider>
    );
};

const buttonCss = css`
    border-color: transparent;
    background-color: transparent;
    color: ${kBloomBlue};
    vertical-align: middle;
    width: 22px;
    padding: 0;
    line-height: 0.7em;
    cursor: pointer;
    svg {
        font-size: 1.3rem;
    }
`;
