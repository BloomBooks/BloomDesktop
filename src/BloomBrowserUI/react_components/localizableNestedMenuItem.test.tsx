import * as React from "react";
import { act } from "react";
import { MenuItem } from "@mui/material";
import { createTheme, ThemeProvider } from "@mui/material/styles";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../utils/reactRender";
import { LocalizableNestedMenuItem } from "./localizableMenuItem";

// A row with a submenu, in a menu that stays mounted while it is shut (keepMounted), as the canvas
// element's "..." menu does. The row is told whether that menu is open through parentMenuOpen.

vi.mock("./l10nHooks", () => ({
    useL10n: (englishText: string) => englishText,
}));

vi.mock("./featureStatus", () => ({
    openBloomSubscriptionSettings: vi.fn(),
    useGetFeatureStatus: () => undefined,
    useGetFeatureAvailabilityMessage: () => undefined,
}));

vi.mock("../utils/bloomApi", () => ({
    getBoolean: vi.fn(),
    postBoolean: vi.fn(),
}));

const kRowId = "Test.Layer";
const kSubmenuCommandId = "Test.Layer.SendToBack";

// mui-nested-menu-item styles the row from the theme, as Bloom's own theme provider supplies it.
const theme = createTheme();

let container: HTMLDivElement;

/** Render the row, as the menu holding it does on every render, saying whether that menu is open. */
function renderRow(parentMenuOpen: boolean): void {
    act(() => {
        renderRoot(
            <ThemeProvider theme={theme}>
                <LocalizableNestedMenuItem
                    english="Layer"
                    l10nId={kRowId}
                    parentMenuOpen={parentMenuOpen}
                >
                    <MenuItem data-testid={kSubmenuCommandId}>
                        Send to Back
                    </MenuItem>
                </LocalizableNestedMenuItem>
            </ThemeProvider>,
            container,
        );
    });
}

/** The row's own menu item. */
function row(): HTMLElement {
    const found = document.querySelector<HTMLElement>(
        `[data-testid="${kRowId}"]`,
    );
    if (!found) throw new Error("The row was not rendered.");
    return found;
}

/** Whether the row's submenu is showing: its command is in the document only while it is. */
function submenuIsShowing(): boolean {
    return (
        document.querySelector(`[data-testid="${kSubmenuCommandId}"]`) !== null
    );
}

/** Bring the pointer onto the row from outside the menu, which opens the row's submenu. */
function pointerEntersRow(): void {
    act(() => {
        row().dispatchEvent(
            new MouseEvent("mouseover", {
                bubbles: true,
                relatedTarget: document.body,
            }),
        );
    });
}

describe("LocalizableNestedMenuItem in a menu that stays mounted while shut", () => {
    beforeEach(() => {
        container = document.createElement("div");
        document.body.appendChild(container);
    });

    afterEach(() => {
        act(() => {
            unmountRoot(container);
        });
        document.body.innerHTML = "";
    });

    it("opens its submenu when the pointer comes onto the row", () => {
        renderRow(true);
        expect(submenuIsShowing()).toBe(false);
        pointerEntersRow();
        expect(submenuIsShowing()).toBe(true);
    });

    // The menu can shut while the pointer is still on the submenu, and nothing tells the row that
    // the pointer left: the canvas element menu shuts on an Escape that Bloom stops before the
    // submenu hears it. The 2026-11-03 nightly failed on this (canvas-layer-order.spec.ts).
    it("does not open its submenu again when the menu opens again", () => {
        renderRow(true);
        pointerEntersRow();
        // Sanity check: the submenu was open when the menu shut.
        expect(submenuIsShowing()).toBe(true);

        renderRow(false);
        expect(submenuIsShowing()).toBe(false);

        renderRow(true);
        expect(
            submenuIsShowing(),
            "The submenu opened again with the menu, though the pointer had not come back onto the row.",
        ).toBe(false);

        // And the row still opens it when the pointer does come onto it.
        pointerEntersRow();
        expect(submenuIsShowing()).toBe(true);
    });
});
