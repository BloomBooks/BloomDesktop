/** @jsxImportSource @emotion/react */
import { css } from "@emotion/react";
import * as React from "react";
import type { HierarchyNode } from "./themeModel";
import { ColorSwatch } from "./ColorSwatch";
import { bloom } from "./bloomTheme";

const INDENT = 22; // px per level

// One row of the colors outline: indentation + hierarchy connectors, an optional
// expand/collapse chevron, the color swatch, the friendly label, and either an
// "Inherited" tag (derived value) or a "Reset" button (explicitly overridden).
export const VariableRow: React.FunctionComponent<{
    node: HierarchyNode;
    value: string;
    isOverridden: boolean;
    isExpanded: boolean;
    hasChildren: boolean;
    /** Highlighted because the preview/contrast item the user clicked uses this variable. */
    isHighlighted: boolean;
    /** Palette offered by the color picker (black, white, the theme's colors). */
    swatches: string[];
    onToggleExpand: () => void;
    onColorChange: (hex: string) => void;
    onReset: () => void;
}> = (props) => {
    const showConnector = props.node.level > 0;
    return (
        <div
            data-var={props.node.name}
            css={css`
                position: relative;
                display: flex;
                align-items: center;
                gap: 6px;
                padding: 3px 0;
                margin-left: ${props.node.level * INDENT}px;
                ${props.isHighlighted
                    ? "background: #fff3a0; border-radius: 3px;"
                    : ""}
            `}
        >
            {showConnector && (
                <React.Fragment>
                    {/* vertical line up to the parent row */}
                    <span
                        css={css`
                            position: absolute;
                            left: -${INDENT / 2}px;
                            top: 0;
                            bottom: 50%;
                            width: 1px;
                            background: #d0d0d0;
                        `}
                    />
                    {/* horizontal line to this row */}
                    <span
                        css={css`
                            position: absolute;
                            left: -${INDENT / 2}px;
                            top: 50%;
                            width: ${INDENT / 2 - 3}px;
                            height: 1px;
                            background: #d0d0d0;
                        `}
                    />
                </React.Fragment>
            )}

            {props.hasChildren ? (
                <button
                    type="button"
                    onClick={props.onToggleExpand}
                    aria-label={props.isExpanded ? "Collapse" : "Expand"}
                    css={css`
                        width: 18px;
                        height: 18px;
                        border: none;
                        background: transparent;
                        cursor: pointer;
                        color: #555;
                        font-size: 11px;
                        line-height: 1;
                        padding: 0;
                        flex-shrink: 0;
                    `}
                >
                    {props.isExpanded ? "▾" : "▸"}
                </button>
            ) : (
                <span
                    css={css`
                        width: 18px;
                        flex-shrink: 0;
                    `}
                />
            )}

            <ColorSwatch
                value={props.value}
                isInherited={!props.isOverridden}
                swatches={props.swatches}
                onChange={props.onColorChange}
                title={props.node.displayName}
            />

            <span
                css={css`
                    flex: 1;
                    font-size: 13px;
                    white-space: nowrap;
                    overflow: hidden;
                    text-overflow: ellipsis;
                    ${props.isOverridden ? "font-weight: 600;" : ""}
                `}
            >
                {props.node.displayName}
            </span>

            {!props.isOverridden ? (
                <span
                    css={css`
                        font-size: 11px;
                        color: #999;
                        flex-shrink: 0;
                    `}
                >
                    Inherited
                </span>
            ) : props.node.level > 0 ? (
                <button
                    type="button"
                    onClick={props.onReset}
                    css={css`
                        border: none;
                        background: transparent;
                        color: ${bloom.blueText};
                        cursor: pointer;
                        font-size: 12px;
                        padding: 2px 4px;
                        flex-shrink: 0;
                        &:hover {
                            text-decoration: underline;
                        }
                    `}
                >
                    Reset
                </button>
            ) : (
                // root variable: nothing to reset to; keep the row width stable
                <span
                    css={css`
                        width: 40px;
                        flex-shrink: 0;
                    `}
                />
            )}
        </div>
    );
};
