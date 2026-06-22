/** @jsxImportSource @emotion/react */
import { css } from "@emotion/react";
import * as React from "react";
import { contrastRatio, compositeOver } from "./contrastUtils";

// Each check compares a foreground color against the background it actually sits on.
// "text-*" rows use WCAG text thresholds (large text gets the relaxed limits); "graphic"
// rows use the non-text contrast threshold (WCAG 1.4.11, 3:1) for an element being
// distinguishable from the page behind it.
type CheckKind = "text-large" | "text-normal" | "graphic";

// For "graphic" checks we draw a little sample resembling the actual object (the fg color)
// sitting on its background (bg). A bar = the header band; a button = a button/draggable chip;
// a box = a checkbox. Outlined shapes show only an outline (e.g. a button whose fill equals the
// page, or an empty checkbox), which is what the user actually sees on the page.
type SampleShape = "bar" | "button" | "box";

interface Check {
    label: string;
    fg: string; // CSS variable name
    bg: string; // CSS variable name
    kind: CheckKind;
    shape?: SampleShape; // graphic checks only
    outlined?: boolean; // graphic checks only: draw an outline rather than a fill
    dashed?: boolean; // graphic outlined checks: draw a dashed outline (e.g. the drop target)
    // Text checks: the sample glyph drawn in the foreground color. Defaults to "Aa"; e.g. the
    // selected checkbox shows its check mark instead.
    sampleText?: string;
    // Graphic button checks: the element's border color (a CSS variable name), drawn together
    // with the fill (fg). When set, the object is "visible on the page" if EITHER the border or
    // the fill has enough contrast with the page: a clear border separates the button from the
    // page, and if the border is too faint the fill must do the separating instead. So we don't
    // flag a borderless-but-boldly-filled button (or a faint-fill-but-clearly-outlined one).
    border?: string;
    // First check of a visual group: each group is drawn in its own rounded box (with a gap
    // before it), so a group's related rows read as one block. The first check overall also
    // begins a group without needing this flag.
    startsGroup?: boolean;
}

const PAGE_BG = "--game-page-bg-color";

// One flat list of checks, ordered so that rows about the same thing sit together: each
// object's "visible on the page" check (its fill/border vs the page) is immediately followed
// by the matching "is its text legible" check (its text vs its own background). The page-wide
// text rows lead; the drop Target (which has no text) trails its draggable.
const checks: Check[] = [
    {
        label: "Text on page",
        fg: "--game-text-color",
        bg: PAGE_BG,
        kind: "text-large",
    },
    {
        label: "Page number",
        fg: "--game-page-number-color",
        bg: PAGE_BG,
        kind: "text-normal",
    },

    {
        label: "Header band",
        fg: "--game-header-bg-color",
        bg: PAGE_BG,
        kind: "graphic",
        shape: "bar",
        startsGroup: true,
    },
    {
        label: "Header text",
        fg: "--game-header-color",
        bg: "--game-header-bg-color",
        kind: "text-large",
    },

    {
        // The three button states are the same on-page element: one shared border plus a fill
        // that differs by state. Each is checked as border-plus-fill against the page (see Check.border).
        label: "Normal button",
        fg: "--game-button-bg-color",
        border: "--game-button-outline-color",
        bg: PAGE_BG,
        kind: "graphic",
        shape: "button",
        startsGroup: true,
    },
    {
        label: "Normal button text",
        fg: "--game-button-text-color",
        bg: "--game-button-bg-color",
        kind: "text-large",
    },

    {
        label: "'Correct' button",
        fg: "--game-button-correct-bg-color",
        border: "--game-button-outline-color",
        bg: PAGE_BG,
        kind: "graphic",
        shape: "button",
        startsGroup: true,
    },
    {
        label: "'Correct' button text",
        fg: "--game-button-correct-color",
        bg: "--game-button-correct-bg-color",
        kind: "text-large",
    },

    {
        label: "'Incorrect' button",
        fg: "--game-button-wrong-bg-color",
        border: "--game-button-outline-color",
        bg: PAGE_BG,
        kind: "graphic",
        shape: "button",
        startsGroup: true,
    },
    {
        label: "'Incorrect' button text",
        fg: "--game-button-wrong-color",
        bg: "--game-button-wrong-bg-color",
        kind: "text-large",
    },

    {
        label: "Draggable",
        fg: "--game-draggable-bg-color",
        bg: PAGE_BG,
        kind: "graphic",
        shape: "button",
        startsGroup: true,
    },
    {
        label: "Draggable text",
        fg: "--game-draggable-color",
        bg: "--game-draggable-bg-color",
        kind: "text-large",
    },
    {
        // The dashed "target" outline a draggable is dropped onto.
        label: "Target",
        fg: "--game-draggable-target-outline-color",
        bg: PAGE_BG,
        kind: "graphic",
        shape: "button",
        outlined: true,
        dashed: true,
    },

    {
        label: "Checkbox label",
        fg: "--game-checkbox-text-color",
        bg: PAGE_BG,
        kind: "text-normal",
        startsGroup: true,
    },
    {
        label: "Checkbox outline",
        fg: "--game-checkbox-outline-color",
        bg: PAGE_BG,
        kind: "graphic",
        shape: "box",
        outlined: true,
    },

    {
        label: "Selected Checkbox fill",
        fg: "--game-selected-checkbox-bg-color",
        bg: PAGE_BG,
        kind: "graphic",
        shape: "box",
    },
    {
        label: "Checkmark",
        fg: "--game-selected-checkbox-color",
        bg: "--game-selected-checkbox-bg-color",
        kind: "text-large",
        sampleText: "✓",
    },
];

// AA pass limit by kind. Large text and graphics share the relaxed limit; normal
// text needs the stricter one.
const limits: Record<CheckKind, number> = {
    "text-large": 3,
    "text-normal": 4.5,
    graphic: 3,
};

// Fixed width (px) of the contrast verdict column, so the verdict lines up across every
// group box and the column header even though each group is its own table.
const VERDICT_WIDTH = 110;

// Split the flat check list into visual groups: a new group begins at each check flagged
// startsGroup (and at the very first check). Each group is drawn in its own bordered box.
const groups: Check[][] = [];
for (const check of checks) {
    if (check.startsGroup || groups.length === 0) groups.push([check]);
    else groups[groups.length - 1].push(check);
}

// The contrast verdict for a row: a green check when it passes AA, or a bold red
// "Low Contrast" callout when it doesn't.
const Verdict: React.FunctionComponent<{ pass: boolean }> = (props) =>
    props.pass ? (
        <span
            css={css`
                color: #1a9f55;
                font-weight: 700;
            `}
        >
            ✓
        </span>
    ) : (
        <span
            css={css`
                color: #cc2b2b;
                font-weight: 700;
                white-space: nowrap;
            `}
        >
            Low Contrast
        </span>
    );

// A small "stage" showing the comparison background (bg) with, on it, either sample text in the
// foreground color (text checks) or a shape resembling the object (graphic checks). This makes
// the comparison concrete: e.g. a button's fill, or a checkbox outline, against the page color.
const Sample: React.FunctionComponent<{
    check: Check;
    fg: string;
    bg: string;
    // Resolved border color for button/box checks that carry a Check.border; drawn around the fill.
    border?: string;
}> = (props) => {
    const stage = css`
        flex: 0 0 auto;
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 76px;
        height: 32px;
        border-radius: 4px;
        border: 1px solid #e0e0e0;
        background-color: ${props.bg};
        overflow: hidden;
    `;
    let inner: React.ReactNode;
    if (props.check.kind !== "graphic") {
        inner = (
            <span
                css={css`
                    color: ${props.fg};
                    font-weight: 600;
                    font-size: 15px;
                `}
            >
                {props.check.sampleText ?? "Aa"}
            </span>
        );
    } else if (props.check.shape === "bar") {
        inner = (
            <span
                css={css`
                    width: 56px;
                    height: 12px;
                    border-radius: 2px;
                    background-color: ${props.fg};
                `}
            />
        );
    } else if (props.check.shape === "box") {
        inner = (
            <span
                css={css`
                    width: 16px;
                    height: 16px;
                    border-radius: 3px;
                    ${props.check.outlined
                        ? `border: 2px ${props.check.dashed ? "dashed" : "solid"} ${props.fg};`
                        : `background-color: ${props.fg};`}
                `}
            />
        );
    } else if (props.border) {
        // A real button: its fill plus its border, both as they sit on the page.
        inner = (
            <span
                css={css`
                    width: 50px;
                    height: 20px;
                    border-radius: 4px;
                    background-color: ${props.fg};
                    border: 2px solid ${props.border};
                `}
            />
        );
    } else {
        // button / draggable chip / drop target
        inner = (
            <span
                css={css`
                    width: 50px;
                    height: 20px;
                    border-radius: 4px;
                    ${props.check.outlined
                        ? `border: 2px ${props.check.dashed ? "dashed" : "solid"} ${props.fg};`
                        : `background-color: ${props.fg};`}
                `}
            />
        );
    }
    return <span css={stage}>{inner}</span>;
};

const Row: React.FunctionComponent<{
    check: Check;
    resolvedValues: Record<string, string>;
    onSelectVars: (vars: string[]) => void;
}> = (props) => {
    const fg = props.resolvedValues[props.check.fg] || "#000000";
    const bg = props.resolvedValues[props.check.bg] || "#ffffff";
    const aaLimit = limits[props.check.kind];
    // Measure contrast against what the eye actually sees: flatten any transparency onto the
    // background first, so a nearly-invisible (low-alpha) color resolves close to its background
    // and fails — e.g. a faint drop-target outline no longer scores as good contrast.
    const ratio = contrastRatio(compositeOver(fg, bg), bg);
    // A button check carries a border color too: it's visible if EITHER the border or the fill
    // separates it from the page, so we pass on the better of the two contrasts.
    const borderColor = props.check.border
        ? props.resolvedValues[props.check.border] || "#000000"
        : undefined;
    const borderRatio =
        borderColor !== undefined
            ? contrastRatio(compositeOver(borderColor, bg), bg)
            : 0;
    const pass =
        borderColor !== undefined
            ? ratio >= aaLimit || borderRatio >= aaLimit
            : ratio >= aaLimit;
    const title =
        borderColor !== undefined
            ? `Border contrast ${borderRatio.toFixed(2)}:1, fill contrast ${ratio.toFixed(2)}:1`
            : `Contrast ${ratio.toFixed(2)}:1`;
    // Clicking a row reveals, in the Colors outline, the colors it compares (fill, border, page).
    const selectVars = props.check.border
        ? [props.check.fg, props.check.border, props.check.bg]
        : [props.check.fg, props.check.bg];
    const cellCss = css`
        padding: 5px 8px;
        text-align: center;
        width: ${VERDICT_WIDTH}px;
    `;
    return (
        <tr
            title={title}
            onClick={() => props.onSelectVars(selectVars)}
            css={css`
                cursor: pointer;
                &:hover {
                    outline: 2px solid #f3aa18;
                    outline-offset: -2px;
                }
            `}
        >
            <td
                css={css`
                    padding: 5px 8px;
                    white-space: nowrap;
                `}
            >
                <span
                    css={css`
                        display: inline-flex;
                        align-items: center;
                        gap: 8px;
                    `}
                >
                    <Sample
                        check={props.check}
                        fg={fg}
                        bg={bg}
                        border={borderColor}
                    />
                    <span
                        css={css`
                            color: #333;
                            font-weight: 500;
                        `}
                    >
                        {props.check.label}
                    </span>
                </span>
            </td>
            <td css={cellCss}>
                <Verdict pass={pass} />
            </td>
        </tr>
    );
};

// The WCAG contrast table, driven by the live resolved colors. Clicking a row reveals the
// two colors it compares in the Colors outline.
export const ContrastChecker: React.FunctionComponent<{
    resolvedValues: Record<string, string>;
    onSelectVars: (vars: string[]) => void;
}> = (props) => {
    return (
        // Each related group in its own rounded box, with a gap between groups.
        <div
            css={css`
                display: flex;
                flex-direction: column;
                gap: 8px;
            `}
        >
            {groups.map((group) => (
                <div
                    key={group[0].label}
                    css={css`
                        border: 1px solid #e0e0e0;
                        border-radius: 6px;
                        overflow: hidden;
                    `}
                >
                    <table
                        css={css`
                            width: 100%;
                            border-collapse: collapse;
                            font-size: 13px;
                        `}
                    >
                        <tbody>
                            {group.map((c) => (
                                <Row
                                    key={c.label}
                                    check={c}
                                    resolvedValues={props.resolvedValues}
                                    onSelectVars={props.onSelectVars}
                                />
                            ))}
                        </tbody>
                    </table>
                </div>
            ))}
        </div>
    );
};
