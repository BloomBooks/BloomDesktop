import { act } from "react";
import { afterEach, describe, expect, it } from "vitest";
import { Menu } from "@mui/material";
import { ThemeProvider } from "@mui/material/styles";
import { lightTheme } from "../bloomMaterialUITheme";
import { renderRoot, unmountRoot } from "../utils/reactRender";
import {
    LocalizableMenuItem,
    LocalizableNestedMenuItem,
} from "./localizableMenuItem";

let container: HTMLDivElement | undefined;

afterEach(() => {
    if (container) {
        act(() => {
            unmountRoot(container!);
        });
        container.remove();
        container = undefined;
    }
});

// Renders a keepMounted menu, like the canvas element menu, holding one row with a submenu.
function renderMenu(open: boolean) {
    act(() => {
        renderRoot(
            <ThemeProvider theme={lightTheme}>
                <Menu open={open} keepMounted anchorEl={container}>
                    <LocalizableNestedMenuItem
                        english="Layer"
                        l10nId={null}
                        parentMenuOpen={open}
                    >
                        <LocalizableMenuItem
                            english="Bring Forward"
                            l10nId={null}
                            onClick={() => {}}
                        />
                    </LocalizableNestedMenuItem>
                </Menu>
            </ThemeProvider>,
            container!,
        );
    });
}

// The submenu's Menu is not keepMounted, so its commands exist only while it is open.
function isSubmenuShowing(): boolean {
    return Array.from(document.querySelectorAll("li")).some(
        (li) => li.textContent === "Bring Forward",
    );
}

// The element NestedMenuItem listens on for the pointer entering the row.
function getRowContainer(): HTMLElement {
    const label = Array.from(document.querySelectorAll("li")).find(
        (li) => li.textContent === "Layer",
    );
    if (!label) throw new Error("The Layer row was not rendered");
    return label.parentElement!;
}

describe("LocalizableNestedMenuItem in a keepMounted menu", () => {
    it("does not reopen its submenu when the menu opens again", () => {
        container = document.createElement("div");
        document.body.appendChild(container);
        renderMenu(true);
        expect(isSubmenuShowing()).toBe(false);

        act(() => {
            getRowContainer().dispatchEvent(
                new MouseEvent("mouseover", {
                    bubbles: true,
                    relatedTarget: document.body,
                }),
            );
        });
        expect(isSubmenuShowing()).toBe(true);

        // Choosing a command shuts the menu with the pointer still in the row, so no mouse leave.
        renderMenu(false);
        expect(isSubmenuShowing()).toBe(false);

        renderMenu(true);
        expect(isSubmenuShowing()).toBe(false);
    });
});
