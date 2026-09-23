import { css } from "@emotion/react";
import { kBloomBlue } from "../../../utils/colorUtils";

// The reader tools (Decodable + Leveled) share one accent, brighter than
// kBloomLightBlue, so the section headers, links, and the button/sort outlines
// all read clearly (AA, ~5.4:1) against the dark toolbox panel. kBloomBlue
// (#1d94a4) is used as a *fill* — the accordion header bar and the
// selected-sort background — with white on top. (BL-16585)
export const kReaderAccent = "#23b4c7";
// Muted text in the reader tools ("of N", the Max column, the This Page/This
// Book sub-labels, the within/over legend). (BL-16585)
export const kReaderMuted = "#999999";

// Section header styling shared across the reader tools: an uppercase accent
// label with a full-width underline rule. This is the look of "Word Counts"
// et al. in the Leveled Reader; the Decodable Reader's "Letters in this stage"
// / "Sample words in this stage" headers reuse it so the two tools match.
// (BL-16585)
export const readerSectionHeaderCss = css`
    color: ${kReaderAccent};
    text-transform: uppercase;
    letter-spacing: 0.08em;
    border-bottom: 1px solid rgba(255, 255, 255, 0.15);
    padding-bottom: 5px;
`;

// "Contained" button styling for the primary Set Up Stages/Levels action: white
// text on a kBloomBlue fill (the same fill Bloom uses for the accordion header
// bar and the selected sort button), so the setup button reads as the main call
// to action and stands apart from the quieter text actions. (BL-16585)
export const readerContainedButtonCss = css`
    && {
        text-transform: uppercase;
        color: white;
        font-weight: normal;
        font-size: 11px;
        border: 1px solid ${kBloomBlue};
        border-radius: 4px;
        padding: 9px 18px;
        line-height: 1.2;
        background-color: ${kBloomBlue};
    }
    // Keep the fill on hover/active (only the press nudge changes), so the solid
    // look is stable rather than flashing to the black used by the text buttons.
    &:hover {
        background-color: ${kBloomBlue};
    }
    &:active {
        background-color: ${kBloomBlue};
        transform: translateY(2px);
    }
`;

// "Text" button styling for the secondary actions in the Actions section (Copy
// Book Stats, Generate Report): the same quiet uppercase accent label as the
// outline button but with no border, so the outlined/contained Set Up button
// stays visually dominant. Any icon uses currentColor so it takes the accent.
// (BL-16585)
export const readerTextButtonCss = css`
    && {
        text-transform: uppercase;
        color: ${kReaderAccent};
        font-weight: normal;
        font-size: 11px;
        padding: 9px 8px;
        line-height: 1.2;
    }
    &:hover {
        background-color: black;
    }
    &:active {
        background-color: black;
        transform: translateY(2px);
    }
`;
