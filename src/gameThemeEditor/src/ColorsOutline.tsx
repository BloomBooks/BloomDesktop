/** @jsxImportSource @emotion/react */
import { css } from "@emotion/react";
import * as React from "react";
import { useEffect, useMemo, useState } from "react";
import { buildHierarchyTree } from "./themeModel";
import type { HierarchyNode } from "./themeModel";
import { VariableRow } from "./VariableRow";
import { bloom } from "./bloomTheme";

// The "Colors" outline: the variable hierarchy as an indented, expand/collapse tree.
// Resolved colors come from the live page (passed in via resolvedValues); whether a
// variable is explicitly part of the theme comes from isVariableOverridden.
// A small circular-arrow "reset" glyph, inline so the editor stays dependency-free.
const ResetIcon: React.FunctionComponent = () => (
    <svg
        width="13"
        height="13"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2.2"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
    >
        <path d="M3 12a9 9 0 1 0 3-6.7L3 8" />
        <path d="M3 3v5h5" />
    </svg>
);

export const ColorsOutline: React.FunctionComponent<{
    resolvedValues: Record<string, string>;
    isVariableOverridden: (name: string) => boolean;
    onColorChange: (name: string, hex: string) => void;
    onReset: (name: string) => void;
    /** Reset every variable back to the theme as it was loaded (clears all overrides). */
    onResetAll: () => void;
    /** Variables used by the preview/contrast item the user clicked; highlighted and revealed. */
    highlightedVars: string[];
    /** Palette offered by each row's color picker (black, white, the theme's colors). */
    swatches: string[];
}> = (props) => {
    const hierarchy = useMemo(() => buildHierarchyTree(), []);
    const highlightSet = useMemo(
        () => new Set(props.highlightedVars),
        [props.highlightedVars],
    );

    // A node is "customized" if it or any descendant is explicitly overridden; we expand
    // such branches by default so the user sees what differs from the defaults.
    const isNodeCustomized = React.useCallback(
        (node: HierarchyNode): boolean =>
            props.isVariableOverridden(node.name) ||
            node.children.some(isNodeCustomized),
        [props],
    );

    const [expanded, setExpanded] = useState<Record<string, boolean>>({});
    useEffect(() => {
        // True if any highlighted variable lies below this node (so it must be expanded to reveal).
        const hasHighlightedDescendant = (node: HierarchyNode): boolean =>
            node.children.some(
                (c) => highlightSet.has(c.name) || hasHighlightedDescendant(c),
            );
        const next: Record<string, boolean> = {};
        const walk = (nodes: HierarchyNode[]) => {
            nodes.forEach((node) => {
                if (node.children.length > 0) {
                    // When an item is selected, expand exactly the branches that reveal its
                    // variables and collapse the rest; otherwise fall back to the default.
                    next[node.name] = highlightSet.size
                        ? hasHighlightedDescendant(node)
                        : node.children.some(isNodeCustomized);
                    walk(node.children);
                }
            });
        };
        walk(hierarchy);
        setExpanded(next);
        // Recompute when the hierarchy or the highlighted set changes. Manual toggles in between
        // persist (we don't depend on isNodeCustomized to avoid fighting the user's clicks).
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [hierarchy, props.highlightedVars]);

    const toggle = (name: string) =>
        setExpanded((prev) => ({ ...prev, [name]: !prev[name] }));

    const renderNodes = (nodes: HierarchyNode[]): React.ReactNode =>
        nodes.map((node) => (
            <React.Fragment key={node.name}>
                <VariableRow
                    node={node}
                    value={props.resolvedValues[node.name] || "#000000"}
                    isOverridden={props.isVariableOverridden(node.name)}
                    isExpanded={!!expanded[node.name]}
                    hasChildren={node.children.length > 0}
                    isHighlighted={highlightSet.has(node.name)}
                    swatches={props.swatches}
                    onToggleExpand={() => toggle(node.name)}
                    onColorChange={(hex) => props.onColorChange(node.name, hex)}
                    onReset={() => props.onReset(node.name)}
                />
                {node.children.length > 0 &&
                    expanded[node.name] &&
                    renderNodes(node.children)}
            </React.Fragment>
        ));

    return (
        <div
            css={css`
                border: 1px solid #e0e0e0;
                border-radius: 6px;
                overflow: hidden;
                display: flex;
                flex-direction: column;
                min-height: 0;
            `}
        >
            <div
                css={css`
                    background: #f3f3f3;
                    border-bottom: 1px solid #e0e0e0;
                    padding: 4px 6px 4px 10px;
                    font-size: 13px;
                    font-weight: 600;
                    flex-shrink: 0;
                    display: flex;
                    align-items: center;
                    justify-content: space-between;
                `}
            >
                <span>Colors</span>
                <button
                    type="button"
                    onClick={props.onResetAll}
                    title="Reset all colors to the theme as loaded"
                    css={css`
                        display: inline-flex;
                        align-items: center;
                        gap: 4px;
                        border: none;
                        background: transparent;
                        color: ${bloom.blueText};
                        cursor: pointer;
                        font-size: 12px;
                        font-weight: 500;
                        padding: 3px 6px;
                        border-radius: 4px;
                        &:hover {
                            background: rgba(29, 148, 164, 0.1);
                        }
                    `}
                >
                    <ResetIcon />
                    Reset
                </button>
            </div>
            <div
                css={css`
                    overflow-y: auto;
                    padding: 8px 10px;
                `}
            >
                {renderNodes(hierarchy)}
            </div>
        </div>
    );
};
