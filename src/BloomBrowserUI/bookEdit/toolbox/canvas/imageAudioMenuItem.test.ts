import { describe, expect, test } from "vitest";

import { buildCanvasElementControlRegistryContext } from "./buildCanvasElementControlRegistryContext";
import { makeChooseAudioMenuItemForImage } from "./canvasControlRegistry";
import { IControlMenuCommandRow } from "./canvasControlTypes";

// Tests for the labels on the image "play when touched" menu and its submenu.
//
// The row that plays the currently-chosen sound must be labelled with that sound's
// file name, exactly as the parent row is. It regressed once: the row carried
// l10nId "ARecording" as well as the file name in englishLabel, and because the
// localization lookup wins over englishLabel, every image sound was displayed as
// the localized string "A Recording". "A Recording" is the right label for the
// *text* case, where a talking-book recording has no file name to show.
//
// So the assertion that matters below is about the ABSENCE of an l10nId on that
// row -- an l10nId there is silently authoritative over the label beside it.

// Build a page with one image canvas element, optionally with a sound attached.
function makeImageCanvasElement(dataSound?: string): HTMLElement {
    const page = document.createElement("div");
    page.className = "bloom-page";

    const canvasElement = document.createElement("div");
    canvasElement.className = "bloom-canvas-element";
    if (dataSound) {
        canvasElement.setAttribute("data-sound", dataSound);
    }

    const container = document.createElement("div");
    container.className = "bloom-imageContainer";
    container.appendChild(document.createElement("img"));
    canvasElement.appendChild(container);

    page.appendChild(canvasElement);
    document.body.appendChild(page);
    return canvasElement;
}

// The audio menu item for an image element, built from a real registry context.
function makeAudioMenuItem(dataSound?: string): IControlMenuCommandRow {
    const ctx = buildCanvasElementControlRegistryContext(
        makeImageCanvasElement(dataSound),
    );
    return makeChooseAudioMenuItemForImage(ctx, { closeMenu: () => {} });
}

function getSubMenuItem(
    parent: IControlMenuCommandRow,
    id: string,
): IControlMenuCommandRow {
    const found = parent.subMenuItems?.find((item) => item.id === id);
    if (!found) {
        throw new Error(
            `No submenu item with id "${id}". Found: ${parent.subMenuItems
                ?.map((item) => item.id)
                .join(", ")}`,
        );
    }
    return found as IControlMenuCommandRow;
}

describe("image audio menu labels", () => {
    test("setup: an element with a sound really is seen as having one", () => {
        // If the context ever stops reading data-sound, the assertions below would be
        // exercising the no-sound path and would pass for the wrong reason.
        const withSound = buildCanvasElementControlRegistryContext(
            makeImageCanvasElement("bird.mp3"),
        );
        expect(withSound.hasCurrentImageSound).toBe(true);
        expect(withSound.currentImageSoundLabel).toBe("bird");

        const withoutSound = buildCanvasElementControlRegistryContext(
            makeImageCanvasElement(),
        );
        expect(withoutSound.hasCurrentImageSound).toBe(false);
    });

    test("the play row shows the sound's file name, not a localized string", () => {
        const playRow = getSubMenuItem(
            makeAudioMenuItem("bird.mp3"),
            "playCurrentAudio",
        );

        // The regression: an l10nId here overrides englishLabel, so the file name
        // would never be displayed.
        expect(playRow.l10nId).toBeUndefined();
        expect(playRow.englishLabel).toBe("bird");
    });

    test("the play row is marked as the sound currently in effect", () => {
        const playRow = getSubMenuItem(
            makeAudioMenuItem("bird.mp3"),
            "playCurrentAudio",
        );

        expect(playRow.icon).toBeTruthy();
    });

    test("the play row only appears when a sound is attached", () => {
        const visible = getSubMenuItem(
            makeAudioMenuItem("bird.mp3"),
            "playCurrentAudio",
        ).availability?.visible;
        if (typeof visible !== "function") {
            throw new Error("Expected the play row's visibility to be a rule.");
        }

        const withSound = buildCanvasElementControlRegistryContext(
            makeImageCanvasElement("bird.mp3"),
        );
        const withoutSound = buildCanvasElementControlRegistryContext(
            makeImageCanvasElement(),
        );
        expect(visible(withSound)).toBe(true);
        expect(visible(withoutSound)).toBe(false);
    });

    test("the parent row shows the file name too, and 'None' when there is no sound", () => {
        const withSound = makeAudioMenuItem("bird.mp3");
        expect(withSound.l10nId).toBeUndefined();
        expect(withSound.englishLabel).toBe("bird");

        const withoutSound = makeAudioMenuItem();
        expect(withoutSound.l10nId).toBe("EditTab.Toolbox.DragActivity.None");
        expect(withoutSound.englishLabel).toBe("None");
    });

    test("a sound whose name contains a '%' is shown as-is", () => {
        // Sound file names reach this menu unencoded on purpose (BL-16669); a name
        // that looks like a URL escape must not be decoded on its way to the label.
        const playRow = getSubMenuItem(
            makeAudioMenuItem("clap%41.mp3"),
            "playCurrentAudio",
        );

        expect(playRow.englishLabel).toBe("clap%41");
    });
});
