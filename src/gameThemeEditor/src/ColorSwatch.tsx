/** @jsxImportSource @emotion/react */
import { css } from "@emotion/react";
import * as React from "react";
import { useRef, useState } from "react";
import { checkerboardBackground } from "./colorUtils";
import { ColorPicker } from "./ColorPicker";

// A friendly color well: a swatch showing the current color that opens our own ColorPicker
// popover when clicked. A translucent color is shown over a checkerboard so its transparency is
// visible. When the color is inherited (not explicitly set in the theme) we draw a faint
// diagonal line across the swatch. No CSS variable names or raw CSS are shown.
export const ColorSwatch: React.FunctionComponent<{
    /** the current effective color (any CSS color string). */
    value: string;
    /** whether the value is inherited (derived) rather than explicitly set. */
    isInherited: boolean;
    /** palette offered by the picker (black, white, the theme's colors), as "#rrggbb". */
    swatches: string[];
    onChange: (hex: string) => void;
    /** swatch edge length in px (default 24). */
    size?: number;
    title?: string;
}> = (props) => {
    const size = props.size ?? 24;
    const buttonRef = useRef<HTMLButtonElement>(null);
    const [anchorRect, setAnchorRect] = useState<DOMRect | null>(null);

    const openPicker = () => {
        if (buttonRef.current)
            setAnchorRect(buttonRef.current.getBoundingClientRect());
    };

    return (
        <React.Fragment>
            <button
                ref={buttonRef}
                type="button"
                title={props.title}
                onClick={openPicker}
                css={css`
                    position: relative;
                    display: inline-block;
                    width: ${size}px;
                    height: ${size}px;
                    padding: 0;
                    border-radius: 4px;
                    border: 1px solid rgba(0, 0, 0, 0.35);
                    overflow: hidden;
                    cursor: pointer;
                    flex-shrink: 0;
                    ${props.isInherited ? "opacity: 0.8;" : ""}
                    ${checkerboardBackground}
                `}
            >
                {/* The actual color over the checkerboard, so transparency shows through. */}
                <span
                    style={{ backgroundColor: props.value }}
                    css={css`
                        position: absolute;
                        inset: 0;
                    `}
                />
                {props.isInherited && (
                    <span
                        css={css`
                            position: absolute;
                            inset: 0;
                            display: flex;
                            align-items: center;
                            justify-content: center;
                            pointer-events: none;
                        `}
                    >
                        <span
                            css={css`
                                width: 100%;
                                height: 1px;
                                background: rgba(255, 255, 255, 0.85);
                                transform: rotate(45deg);
                                box-shadow: 0 0 0 0.5px rgba(0, 0, 0, 0.25);
                            `}
                        />
                    </span>
                )}
            </button>
            {anchorRect && buttonRef.current && (
                <ColorPicker
                    value={props.value}
                    swatches={props.swatches}
                    anchorRect={anchorRect}
                    ownerDocument={buttonRef.current.ownerDocument}
                    onChange={props.onChange}
                    onClose={() => setAnchorRect(null)}
                />
            )}
        </React.Fragment>
    );
};
