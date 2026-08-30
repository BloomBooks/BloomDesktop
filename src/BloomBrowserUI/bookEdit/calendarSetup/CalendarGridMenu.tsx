// The little calendar button that appears over a month grid while the pointer is on it, and
// the menu it opens: which month the grid is for, and whether it shows the dates of the
// neighboring months.
//
// This runs in the page iframe, as part of the page's own setup, so it serves a grid page of a
// Wall Calendar and a grid the user has dropped onto a canvas in any other book alike.
//
// The button lives in a div of the page document's body, outside the .bloom-page, for the same
// reason the canvas element's context controls do: what Bloom saves is the page element and
// what is inside it, so an affordance kept outside it can never end up in the book.

import { css } from "@emotion/react";
import * as React from "react";
import { useEffect, useRef, useState } from "react";
import Menu from "@mui/material/Menu";
import { ThemeProvider } from "@mui/material/styles";
import { lightTheme, kBloomBlue } from "../../bloomMaterialUITheme";
import {
    LocalizableNestedMenuItem,
    LocalizableSelectableMenuItem,
} from "../../react_components/localizableMenuItem";
import { useL10n } from "../../react_components/l10nHooks";
import { renderRoot, unmountRoot } from "../../utils/reactRender";
import { Point } from "../js/point";
import { getCalendarGrids, layOutGridIfNeeded } from "./calendarGrids";
import {
    getFirstDayOfWeekOfGrid,
    getGridShowsNeighborDays,
    getMonthNamesToOffer,
    getMonthOfGrid,
    getWeekdayNamesToOffer,
    getYearOfGrid,
    getYearsToOffer,
    kMonthCount,
    setFirstDayOfWeekOfGrid,
    setGridShowsNeighborDays,
    setMonthOfGrid,
    setYearOfGrid,
} from "./calendarGridActions";

/** One grid and the body-level div holding its button. */
interface IGridMenuHost {
    grid: HTMLElement;
    host: HTMLElement;
}

// Every grid we have put a button on, so that a second pass over the same page adds nothing and
// a grid the user has deleted takes its button with it.
const hosts: IGridMenuHost[] = [];

let windowListenersInstalled = false;

/**
 * Set the month grids of a page up: lay each one out for the year and month it says it is for,
 * and give it its menu button.
 *
 * Called from SetupElements before the table library attaches, so that a grid's first drawing
 * is already the right one and nothing has to be drawn twice. Safe to call again on the same
 * page or on part of it, which SetupElements does whenever a canvas element is added.
 */
export function setupCalendarGrids(container: HTMLElement): void {
    forgetHostsOfGridsThatAreGone();
    const grids = getCalendarGrids(container);
    grids.forEach((grid) => {
        const pageElement = grid.closest<HTMLElement>(".bloom-page");
        if (pageElement) layOutGridIfNeeded(grid, pageElement);
        addMenuToGrid(grid);
    });
    positionAllHosts();
    installWindowListeners();
}

/** Take down the button of every grid that is no longer in the document. */
function forgetHostsOfGridsThatAreGone(): void {
    for (let i = hosts.length - 1; i >= 0; i--) {
        if (hosts[i].grid.isConnected) continue;
        unmountRoot(hosts[i].host);
        hosts[i].host.remove();
        hosts.splice(i, 1);
    }
}

/**
 * Give one grid its button, unless it has one already.
 *
 * A grid on a canvas gets no button: it is a canvas element, and the same two commands are in
 * the Calendar section of the menu the user opens on any canvas element. The button is for a
 * grid that sits directly on a page, as a Wall Calendar's grids do, which has no such menu.
 */
function addMenuToGrid(grid: HTMLElement): void {
    if (grid.closest(".bloom-canvas-element")) return;
    if (hosts.some((entry) => entry.grid === grid)) return;
    const host = grid.ownerDocument.createElement("div");
    host.className = "calendar-grid-menu-host";
    // The host covers the whole grid, so that the button can sit at its top right corner
    // however the grid is sized. It must therefore pass every pointer event through to the
    // grid underneath; the button itself takes its own back.
    host.style.position = "absolute";
    host.style.pointerEvents = "none";
    host.style.zIndex = "1000";
    grid.ownerDocument.body.appendChild(host);
    hosts.push({ grid, host });
    renderRoot(<CalendarGridMenu grid={grid} />, host);
}

/**
 * Put each button over its grid.
 *
 * The host is not inside the page, so it does not get the page's own scaling; it is given the
 * same transform the scaling container has, from its top left corner, and a size in the page's
 * own units, so that the corner it is anchored to stays the grid's corner at any zoom.
 */
function positionAllHosts(): void {
    const scalingContainer = document.getElementById("page-scaling-container");
    const scale = Point.getScalingFactor() || 1;
    hosts.forEach((entry) => {
        const rect = entry.grid.getBoundingClientRect();
        entry.host.style.transform = scalingContainer?.style.transform ?? "";
        entry.host.style.transformOrigin = "top left";
        entry.host.style.left = `${rect.left + window.scrollX}px`;
        entry.host.style.top = `${rect.top + window.scrollY}px`;
        entry.host.style.width = `${rect.width / scale}px`;
        entry.host.style.height = `${rect.height / scale}px`;
    });
}

/** Follow the grids when the window changes shape or anything on the page scrolls. */
function installWindowListeners(): void {
    if (windowListenersInstalled) return;
    windowListenersInstalled = true;
    window.addEventListener("resize", positionAllHosts);
    // Capture, because the thing that scrolls is usually a div inside the page rather than the
    // window, and a scroll event does not bubble.
    window.addEventListener("scroll", positionAllHosts, true);
}

const CalendarGridMenu: React.FunctionComponent<{ grid: HTMLElement }> = (
    props,
) => {
    const [pointerIsOnGrid, setPointerIsOnGrid] = useState(false);
    const [pointerIsOnButton, setPointerIsOnButton] = useState(false);
    const [menuIsOpen, setMenuIsOpen] = useState(false);
    const [month, setMonth] = useState(getMonthOfGrid(props.grid));
    const [year, setYear] = useState(getYearOfGrid(props.grid));
    const [firstDayOfWeek, setFirstDayOfWeek] = useState(
        getFirstDayOfWeekOfGrid(props.grid),
    );
    const [showNeighborDays, setShowNeighborDays] = useState(
        getGridShowsNeighborDays(props.grid),
    );
    // The names of Bloom's own user interface language, which does not change while Bloom runs.
    const monthNames = getMonthNamesToOffer();
    const weekdayNames = getWeekdayNamesToOffer();
    const buttonRef = useRef<HTMLButtonElement | null>(null);
    const buttonTooltip = useL10n(
        "Calendar grid options",
        "EditTab.CalendarGrid.ButtonTooltip",
    );

    useEffect(() => {
        const onEnter = () => {
            // The grid may have been moved or resized since we last placed the button, which
            // nothing tells us about; the moment the button is about to be shown is the one
            // moment it has to be in the right place.
            positionAllHosts();
            setPointerIsOnGrid(true);
        };
        const onLeave = () => setPointerIsOnGrid(false);
        props.grid.addEventListener("pointerenter", onEnter);
        props.grid.addEventListener("pointerleave", onLeave);
        return () => {
            props.grid.removeEventListener("pointerenter", onEnter);
            props.grid.removeEventListener("pointerleave", onLeave);
        };
    }, [props.grid]);

    const isVisible = pointerIsOnGrid || pointerIsOnButton || menuIsOpen;

    const chooseMonth = (newMonth: number): void => {
        setMenuIsOpen(false);
        setMonthOfGrid(props.grid, newMonth);
        setMonth(newMonth);
    };

    const chooseYear = (newYear: number): void => {
        setMenuIsOpen(false);
        setYearOfGrid(props.grid, newYear);
        setYear(newYear);
    };

    const chooseFirstDayOfWeek = (newDay: number): void => {
        setMenuIsOpen(false);
        setFirstDayOfWeekOfGrid(props.grid, newDay);
        setFirstDayOfWeek(newDay);
    };

    const toggleNeighborDays = (): void => {
        setMenuIsOpen(false);
        const wanted = !showNeighborDays;
        setGridShowsNeighborDays(props.grid, wanted);
        setShowNeighborDays(wanted);
    };

    const openMenu = (): void => {
        setMenuIsOpen(true);
    };

    return (
        <ThemeProvider theme={lightTheme}>
            <button
                ref={buttonRef}
                title={buttonTooltip}
                onClick={openMenu}
                onPointerEnter={() => setPointerIsOnButton(true)}
                onPointerLeave={() => setPointerIsOnButton(false)}
                css={css`
                    position: absolute;
                    top: 4px;
                    right: 4px;
                    width: 24px;
                    height: 24px;
                    padding: 2px;
                    border: 1px solid ${kBloomBlue};
                    border-radius: 50%;
                    background-color: white;
                    color: ${kBloomBlue};
                    cursor: pointer;
                    pointer-events: auto;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    visibility: ${isVisible ? "visible" : "hidden"};
                `}
            >
                <CalendarGlyph />
            </button>
            <Menu
                anchorEl={buttonRef.current}
                open={menuIsOpen}
                onClose={() => setMenuIsOpen(false)}
            >
                <LocalizableNestedMenuItem
                    english="First Day of Week"
                    l10nId="EditTab.CalendarGrid.FirstDayOfWeek"
                >
                    {weekdayNames.map((name, day) => (
                        // The name is already in the user interface language, so the row
                        // carries no l10nId and is shown as it is.
                        <LocalizableSelectableMenuItem
                            key={day}
                            english={name}
                            l10nId={null}
                            selected={day === firstDayOfWeek}
                            onClick={() => chooseFirstDayOfWeek(day)}
                        />
                    ))}
                </LocalizableNestedMenuItem>
                {/* The row is labelled with the month the grid is already for, rather than the
                    word "Month", so the user can see what the grid says without opening the
                    submenu. The name is already in the user interface language, so the row
                    carries no l10nId and is shown as it is. */}
                <LocalizableNestedMenuItem
                    english={monthNames[month]}
                    l10nId={null}
                >
                    {Array.from({ length: kMonthCount }, (unused, index) => (
                        // The name is already in the user interface language, so the row
                        // carries no l10nId and is shown as it is.
                        <LocalizableSelectableMenuItem
                            key={index}
                            english={monthNames[index]}
                            l10nId={null}
                            selected={index === month}
                            onClick={() => chooseMonth(index)}
                        />
                    ))}
                </LocalizableNestedMenuItem>
                {/* Likewise the year the grid is already for. An unconfigured Wall Calendar has
                    no year yet, and there the row keeps the word, because there is nothing to
                    show in its place. A year is a number, so it is never localized. */}
                <LocalizableNestedMenuItem
                    english={year === undefined ? "Year" : String(year)}
                    l10nId={
                        year === undefined ? "EditTab.CalendarGrid.Year" : null
                    }
                >
                    {getYearsToOffer(props.grid).map((offeredYear) => (
                        // A year is a number, not a word of the user interface, so it is
                        // shown as it is and never localized.
                        <LocalizableSelectableMenuItem
                            key={offeredYear}
                            english={String(offeredYear)}
                            l10nId={null}
                            selected={offeredYear === year}
                            onClick={() => chooseYear(offeredYear)}
                        />
                    ))}
                </LocalizableNestedMenuItem>
                <LocalizableSelectableMenuItem
                    english="Show Neighboring Month Dates"
                    l10nId="EditTab.CalendarGrid.ShowNeighboringMonthDates"
                    selected={showNeighborDays}
                    onClick={toggleNeighborDays}
                />
            </Menu>
        </ThemeProvider>
    );
};

/** A month page: a block with a title bar above a few rows of days. */
const CalendarGlyph: React.FunctionComponent = () => (
    <svg
        width="16"
        height="16"
        viewBox="0 0 16 16"
        fill="none"
        xmlns="http://www.w3.org/2000/svg"
    >
        <rect
            x="1.5"
            y="2.5"
            width="13"
            height="12"
            rx="1.5"
            stroke="currentColor"
            strokeWidth="1.4"
        />
        <line
            x1="1.5"
            y1="6.2"
            x2="14.5"
            y2="6.2"
            stroke="currentColor"
            strokeWidth="1.4"
        />
        <line
            x1="5"
            y1="1"
            x2="5"
            y2="3.5"
            stroke="currentColor"
            strokeWidth="1.4"
        />
        <line
            x1="11"
            y1="1"
            x2="11"
            y2="3.5"
            stroke="currentColor"
            strokeWidth="1.4"
        />
        <line
            x1="1.5"
            y1="10.3"
            x2="14.5"
            y2="10.3"
            stroke="currentColor"
            strokeWidth="1"
        />
        <line
            x1="6"
            y1="6.2"
            x2="6"
            y2="14.5"
            stroke="currentColor"
            strokeWidth="1"
        />
        <line
            x1="10"
            y1="6.2"
            x2="10"
            y2="14.5"
            stroke="currentColor"
            strokeWidth="1"
        />
    </svg>
);
