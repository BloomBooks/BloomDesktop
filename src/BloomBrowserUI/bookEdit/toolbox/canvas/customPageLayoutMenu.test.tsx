import { act } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../../../utils/reactRender";
import { CustomPageLayoutMenu } from "./customPageLayoutMenu";

// The menu asks the server whether the CustomXMatterPage feature is available. Returning
// undefined is what the component treats as "no information yet, so not blocked", which is the
// state we want for these tests: both items live and clickable.
vi.mock("../../../react_components/featureStatus", () => ({
    useGetFeatureStatus: () => undefined,
    useGetFeatureAvailabilityMessage: () => "",
}));

// Only the legacy-theme tooltip's link uses this; mocked so these tests don't drag in the
// workspace frames machinery.
vi.mock("../../js/workspaceFrames", () => ({
    getWorkspaceBundleExports: () => ({
        showBookSettingsDialog: () => {},
    }),
}));

let renderedContainer: HTMLDivElement | undefined;

// Renders the menu closed, then clicks its button to open it, since that is the only way to get
// the items into the DOM. Returns the setCustom spy the items should (or should not) call.
function renderAndOpenMenu(isCustom: boolean) {
    const setCustom = vi.fn();
    const container = document.createElement("div");
    document.body.appendChild(container);
    renderedContainer = container;

    // React 18's createRoot renders asynchronously; act() flushes the render and its effects.
    act(() => {
        renderRoot(
            <CustomPageLayoutMenu isCustom={isCustom} setCustom={setCustom} />,
            container,
        );
    });

    const button = container.querySelector("button");
    if (!button) {
        fail(
            "The layout menu did not render its button, so nothing can be clicked.",
        );
    }
    act(() => {
        button.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });

    return setCustom;
}

// The MUI Menu renders into a portal on document.body, not into our container. An item's text is
// whatever the localization lookup produced: the English label in production, but the l10n id
// itself under the localizationManager mock in vitest.setup.ts, so accept either.
function getMenuItem(label: "Standard" | "Custom"): HTMLElement {
    const acceptableTexts = [label, `EditTab.CustomCover.${label}`];
    const items = Array.from(
        document.body.querySelectorAll<HTMLElement>('li[role="menuitem"]'),
    );
    const item = items.find((li) =>
        acceptableTexts.includes(li.textContent?.trim() ?? ""),
    );
    if (!item) {
        fail(
            `Could not find a "${label}" menu item; found [${items
                .map((li) => li.textContent?.trim())
                .join(", ")}]. The menu probably did not open.`,
        );
    }
    return item;
}

// The tick is specifically MUI's CheckIcon, which it labels with data-testid. Matching that
// rather than "any svg in the item" keeps these assertions meaningful if the item ever gains
// another inline icon.
function isTicked(item: HTMLElement): boolean {
    return !!item.querySelector('svg[data-testid="CheckIcon"]');
}

function clickMenuItem(label: "Standard" | "Custom") {
    const item = getMenuItem(label);
    act(() => {
        item.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
}

afterEach(() => {
    if (renderedContainer) {
        unmountRoot(renderedContainer);
        renderedContainer.remove();
        renderedContainer = undefined;
    }
    document.body.innerHTML = "";
    vi.clearAllMocks();
});

describe("CustomPageLayoutMenu selection", () => {
    it("does nothing when the already-selected Custom item is clicked (BL-16725)", () => {
        const setCustom = renderAndOpenMenu(true);
        // Sanity check: we are testing a click on the item that carries the tick.
        expect(isTicked(getMenuItem("Custom"))).toBe(true);
        expect(isTicked(getMenuItem("Standard"))).toBe(false);
        expect(setCustom).not.toHaveBeenCalled();

        clickMenuItem("Custom");

        expect(setCustom).not.toHaveBeenCalled();
    });

    it("does nothing when the already-selected Standard item is clicked", () => {
        const setCustom = renderAndOpenMenu(false);
        expect(isTicked(getMenuItem("Standard"))).toBe(true);
        expect(isTicked(getMenuItem("Custom"))).toBe(false);

        clickMenuItem("Standard");

        expect(setCustom).not.toHaveBeenCalled();
    });

    it("switches to standard when Standard is clicked while on custom", () => {
        const setCustom = renderAndOpenMenu(true);

        clickMenuItem("Standard");

        expect(setCustom).toHaveBeenCalledTimes(1);
        expect(setCustom).toHaveBeenCalledWith("standard", false);
    });

    it("switches to custom when Custom is clicked while on standard", () => {
        const setCustom = renderAndOpenMenu(false);

        clickMenuItem("Custom");

        expect(setCustom).toHaveBeenCalledTimes(1);
        expect(setCustom).toHaveBeenCalledWith("custom", false);
    });
});
