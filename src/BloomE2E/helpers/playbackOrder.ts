// The Talking Book tool's playback order: the "Show playback order buttons" switch in its Advanced
// section, and the number with - and + buttons that Bloom then puts at the top of each text box
// group on the page (audioRecording.ts showPlaybackOrderUi, react_components/playbackOrderControls).
//
// The number is the group's place in the order the page's recordings play in. Bloom saves it as the
// group's tabindex; a page whose groups have none plays them in document order.

import { expect, type Locator, type Page } from "@playwright/test";
import { getUiString } from "./api";
import { editablePageFrame } from "./bookMaking";
import { openAdvancedSection } from "./talkingBook";
import { toolboxFrame } from "./toolbox";

/** The two switches at the bottom of the Talking Book tool's Advanced section. */
export type TalkingBookSwitch =
    | "Show Playback Order"
    | "Show Image Descriptions";

/**
 * The check box behind one of the Talking Book tool's switches. The switches have no id; each is
 * the check box in the label that shows its localized name.
 */
async function talkingBookSwitch(
    page: Page,
    which: TalkingBookSwitch,
): Promise<Locator> {
    const name =
        which === "Show Playback Order"
            ? await getUiString(
                  page,
                  "EditTab.Toolbox.TalkingBookTool.ShowPlaybackOrder",
                  "Show playback order buttons",
              )
            : await getUiString(
                  page,
                  "EditTab.Toolbox.TalkingBookTool.ShowImageDescriptions",
                  "Show image descriptions",
              );
    return toolboxFrame(page)
        .locator("label")
        .filter({ hasText: name })
        .locator('input[type="checkbox"]');
}

/** Whether one of the Talking Book tool's switches is on. Opens the Advanced section first. */
export async function isTalkingBookSwitchOn(
    page: Page,
    which: TalkingBookSwitch,
): Promise<boolean> {
    await openAdvancedSection(page);
    return (await talkingBookSwitch(page, which)).isChecked();
}

/**
 * Turn the "Show playback order buttons" switch on, the way a person clicks it, and wait until
 * Bloom has put its number at the top of `expectedGroups` groups on the page.
 */
export async function showPlaybackOrderButtons(
    page: Page,
    expectedGroups: number,
): Promise<void> {
    await openAdvancedSection(page);
    const input = await talkingBookSwitch(page, "Show Playback Order");
    if (await input.isChecked())
        throw new Error(
            '"Show playback order buttons" was already on, so turning it on would turn it off.',
        );
    await input.click();
    await expect(
        input,
        'Clicking "Show playback order buttons" did not turn it on.',
    ).toBeChecked({ timeout: 30000 });
    await expect(
        editablePageFrame(page).locator(
            ".bloom-playbackOrderControlsContainer .playbackOrderNumber",
        ),
        `After "Show playback order buttons" was turned on, the page should show the playback ` +
            `order of its ${expectedGroups} text box groups.`,
    ).toHaveCount(expectedGroups, { timeout: 30000 });
}

/** The playback order controls Bloom put at the top of the group that `groupSelector` picks. */
function playbackOrderControls(page: Page, groupSelector: string): Locator {
    return editablePageFrame(page)
        .locator(groupSelector)
        .locator(":scope > .bloom-playbackOrderControlsContainer");
}

/**
 * The playback order number shown at the top of the group that `groupSelector` picks, while the
 * playback order buttons are showing. Throws when the group shows none.
 */
export async function getPlaybackOrderNumber(
    page: Page,
    groupSelector: string,
): Promise<number> {
    const shown = playbackOrderControls(page, groupSelector).locator(
        ".playbackOrderNumber",
    );
    await expect(
        shown,
        `The group "${groupSelector}" shows no playback order number.`,
    ).toHaveCount(1, { timeout: 30000 });
    await expect(shown).toBeVisible();
    const text = (await shown.innerText()).trim();
    const value = Number(text);
    if (!Number.isInteger(value))
        throw new Error(
            `The group "${groupSelector}" shows "${text}" as its playback order, not a number.`,
        );
    return value;
}

/**
 * Click the + (later) or - (earlier) button beside the playback order number of the group that
 * `groupSelector` picks, the way a person does.
 */
export async function clickPlaybackOrderButton(
    page: Page,
    groupSelector: string,
    which: "later" | "earlier",
): Promise<void> {
    // The controls are the - button, the number, then the + button.
    const buttons = playbackOrderControls(page, groupSelector).locator(
        "button.playbackOrderButton",
    );
    await expect(
        buttons,
        `The group "${groupSelector}" should show a - and a + playback order button.`,
    ).toHaveCount(2, { timeout: 30000 });
    const button = which === "later" ? buttons.nth(1) : buttons.nth(0);
    await expect(
        button,
        `The playback order "${which === "later" ? "+" : "-"}" button of "${groupSelector}" is disabled.`,
    ).toBeEnabled();
    await button.click();
}

/**
 * `groupSelectors` sorted by where the one element each picks in the Edit tab's page comes in the
 * page's document: the order a page plays in when no playback order was set. Throws when a
 * selector does not pick exactly one element.
 */
export async function getGroupsInDocumentOrder(
    page: Page,
    groupSelectors: string[],
): Promise<string[]> {
    return editablePageFrame(page).evaluate((selectors) => {
        const found = selectors.map((selector) => {
            const matches = document.querySelectorAll(selector);
            if (matches.length !== 1)
                throw new Error(
                    `"${selector}" picks ${matches.length} elements of the page, not 1.`,
                );
            return { selector, element: matches[0] };
        });
        return found
            .sort((a, b) =>
                a.element.compareDocumentPosition(b.element) &
                Node.DOCUMENT_POSITION_FOLLOWING
                    ? -1
                    : 1,
            )
            .map((item) => item.selector);
    }, groupSelectors);
}
