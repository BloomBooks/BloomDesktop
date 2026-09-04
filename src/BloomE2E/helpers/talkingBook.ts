// The Talking Book tool: what it shows on the page while it is open.
//
// While the tool is open it highlights the segment that the next recording would go into. In the
// Edit tab that highlight is a CSS ::highlight() pseudo-element that the tool registers in the page
// frame (audioHighlightManager.ts), and its colors come from two CSS variables the tool sets on
// the page's root element from the style's Highlighting settings in the Format dialog. Reading those
// two things is how a test checks "the current segment is highlighted in the colors I chose".

import { expect, type Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";
import { cssColorToHex } from "./cssValues";

/** The name the tool registers its current-segment highlight under (audioHighlightManager.ts). */
const CURRENT_HIGHLIGHT = "bloom-audio-current";
const BACKGROUND_VAR = "--bloom-audio-current-highlight-background";
const TEXT_VAR = "--bloom-audio-current-highlight-color";

/** The colors the Talking Book tool is highlighting the current segment with, as "#rrggbb". */
export interface IAudioHighlightColors {
    background: string;
    text: string;
}

/**
 * Wait until the Talking Book tool is highlighting a current segment on the page being edited, and
 * return the colors it is using. Throws, naming the tool, if no highlight appears: the tool has to
 * be open (see helpers/toolbox.ts) and the page has to have some text.
 */
export async function getCurrentAudioHighlightColors(
    page: Page,
): Promise<IAudioHighlightColors> {
    const read = () =>
        editablePageFrame(page).evaluate(
            ({ name, backgroundVar, textVar }) => {
                const highlights = (
                    CSS as unknown as { highlights?: Map<string, unknown> }
                ).highlights;
                const style = document.documentElement.style;
                return {
                    registered: !!highlights?.has(name),
                    background: style.getPropertyValue(backgroundVar),
                    text: style.getPropertyValue(textVar),
                };
            },
            {
                name: CURRENT_HIGHLIGHT,
                backgroundVar: BACKGROUND_VAR,
                textVar: TEXT_VAR,
            },
        );
    await expect
        .poll(async () => (await read()).registered, {
            timeout: 30000,
            message:
                "The Talking Book tool never highlighted a current segment on the page. " +
                "It has to be open, and the page has to have text.",
        })
        .toBe(true);
    const { background, text } = await read();
    return {
        background: cssColorToHex(background),
        text: cssColorToHex(text),
    };
}
