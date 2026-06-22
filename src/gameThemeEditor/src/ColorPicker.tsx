/** @jsxImportSource @emotion/react */
import { css } from "@emotion/react";
import * as React from "react";
import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import {
    checkerboardBackground,
    colorToHsva,
    hsvToHex,
    hsvaToColor,
    toCanonicalColor,
    type Hsva,
} from "./colorUtils";

// A self-contained color picker popover modeled on Bloom's: a saturation/value square, a hue
// slider, a transparency (alpha) slider, a row with an eyedropper + hex field + current-color
// preview, and the theme's swatches below. Clicking a swatch yields that exact color (unlike the
// screen-sampling eyedropper). Translucent colors are shown over a checkerboard.
//
// Rendered in a portal on the editor's document body and positioned (fixed) near the anchor so
// the Colors outline's scroll area can't clip it. No external dependencies.

const POP_WIDTH = 232;
const SQUARE_HEIGHT = 150;

type EyeDropperResult = { sRGBHex: string };
type EyeDropperCtor = { new (): { open: () => Promise<EyeDropperResult> } };

// Normalized [0,1] position of a pointer within an element, clamped to its bounds.
const fractionWithin = (
    el: HTMLElement,
    clientX: number,
    clientY: number,
): { fx: number; fy: number } => {
    const r = el.getBoundingClientRect();
    const clamp01 = (n: number) => Math.min(1, Math.max(0, n));
    return {
        fx: clamp01((clientX - r.left) / r.width),
        fy: clamp01((clientY - r.top) / r.height),
    };
};

export const ColorPicker: React.FunctionComponent<{
    /** the current effective color (any CSS color string). */
    value: string;
    /** palette to show, as canonical "#rrggbb"/"#rrggbbaa" strings (see buildThemeSwatches). */
    swatches: string[];
    /** the swatch button we open near, in viewport coordinates. */
    anchorRect: DOMRect;
    /** the document to portal into (the one the editor is mounted in). */
    ownerDocument: Document;
    onChange: (color: string) => void;
    onClose: () => void;
}> = (props) => {
    const valueColor = toCanonicalColor(props.value);
    const [hsva, setHsva] = useState<Hsva>(() => colorToHsva(valueColor));
    const [hexText, setHexText] = useState(valueColor);
    const popRef = useRef<HTMLDivElement>(null);
    const squareRef = useRef<HTMLDivElement>(null);
    const hueRef = useRef<HTMLDivElement>(null);
    const alphaRef = useRef<HTMLDivElement>(null);

    // Resync the controls when the color changes from OUTSIDE the picker (e.g. a swatch click or
    // a reset). Our own edits emit the same color we'd compute here, so they don't resync (which
    // would lose hue when dragging to/through a gray, or alpha when value/saturation is extreme).
    useEffect(() => {
        setHexText(valueColor);
        setHsva((prev) =>
            hsvaToColor(prev).toLowerCase() === valueColor.toLowerCase()
                ? prev
                : colorToHsva(valueColor),
        );
    }, [valueColor]);

    // Dismiss on an outside click or Escape (capture phase, so we see the click first).
    useEffect(() => {
        const doc = props.ownerDocument;
        const onDown = (e: MouseEvent) => {
            if (popRef.current && !popRef.current.contains(e.target as Node))
                props.onClose();
        };
        const onKey = (e: KeyboardEvent) => {
            if (e.key === "Escape") props.onClose();
        };
        doc.addEventListener("mousedown", onDown, true);
        doc.addEventListener("keydown", onKey, true);
        return () => {
            doc.removeEventListener("mousedown", onDown, true);
            doc.removeEventListener("keydown", onKey, true);
        };
    }, [props]);

    // Set the color from a new HSVA (updates the controls and the live theme).
    const applyHsva = (next: Hsva) => {
        setHsva(next);
        props.onChange(hsvaToColor(next));
    };

    const onSquarePointer = (e: React.PointerEvent<HTMLDivElement>) => {
        if (!squareRef.current) return;
        const { fx, fy } = fractionWithin(
            squareRef.current,
            e.clientX,
            e.clientY,
        );
        applyHsva({ ...hsva, s: fx, v: 1 - fy });
    };
    const onHuePointer = (e: React.PointerEvent<HTMLDivElement>) => {
        if (!hueRef.current) return;
        const { fx } = fractionWithin(hueRef.current, e.clientX, e.clientY);
        applyHsva({ ...hsva, h: fx * 360 });
    };
    const onAlphaPointer = (e: React.PointerEvent<HTMLDivElement>) => {
        if (!alphaRef.current) return;
        const { fx } = fractionWithin(alphaRef.current, e.clientX, e.clientY);
        applyHsva({ ...hsva, a: fx });
    };

    const commitHex = (text: string) => props.onChange(toCanonicalColor(text));

    const eyeDropperCtor = (
        props.ownerDocument.defaultView as unknown as {
            EyeDropper?: EyeDropperCtor;
        }
    )?.EyeDropper;
    const useEyedropper = async () => {
        if (!eyeDropperCtor) return;
        try {
            const result = await new eyeDropperCtor().open();
            if (result?.sRGBHex) props.onChange(result.sRGBHex);
        } catch {
            // The user can cancel (Escape), which rejects the promise.
        }
    };

    // Position near the anchor, nudged to stay within the viewport.
    const win = props.ownerDocument.defaultView;
    const vw = win?.innerWidth ?? 1024;
    const vh = win?.innerHeight ?? 768;
    const EST_HEIGHT = 360;
    const left = Math.max(
        8,
        Math.min(props.anchorRect.left, vw - POP_WIDTH - 8),
    );
    const below = props.anchorRect.bottom + 6;
    const top =
        below + EST_HEIGHT > vh
            ? Math.max(8, props.anchorRect.top - EST_HEIGHT - 6)
            : below;

    const pureHue = hsvToHex({ h: hsva.h, s: 1, v: 1 });
    const opaqueCurrent = hsvToHex(hsva); // current color ignoring alpha, for the alpha gradient

    const popover = (
        <div
            ref={popRef}
            style={{ top, left, width: POP_WIDTH }}
            css={css`
                position: fixed;
                z-index: 7000;
                background: white;
                border: 1px solid #b0b0b0;
                border-radius: 6px;
                box-shadow: 0 6px 20px rgba(0, 0, 0, 0.25);
                padding: 8px;
                box-sizing: border-box;
                user-select: none;
            `}
        >
            {/* Saturation (x) / value (y) square over the current hue. */}
            <div
                ref={squareRef}
                onPointerDown={(e) => {
                    e.currentTarget.setPointerCapture(e.pointerId);
                    onSquarePointer(e);
                }}
                onPointerMove={(e) => {
                    if (e.buttons === 1) onSquarePointer(e);
                }}
                css={css`
                    position: relative;
                    height: ${SQUARE_HEIGHT}px;
                    border-radius: 3px;
                    cursor: crosshair;
                    background:
                        linear-gradient(to top, #000, rgba(0, 0, 0, 0)),
                        linear-gradient(to right, #fff, rgba(255, 255, 255, 0)),
                        ${pureHue};
                `}
            >
                <span
                    style={{
                        left: `${hsva.s * 100}%`,
                        top: `${(1 - hsva.v) * 100}%`,
                    }}
                    css={css`
                        position: absolute;
                        width: 12px;
                        height: 12px;
                        margin: -6px 0 0 -6px;
                        border-radius: 50%;
                        border: 2px solid white;
                        box-shadow: 0 0 0 1px rgba(0, 0, 0, 0.5);
                        pointer-events: none;
                    `}
                />
            </div>
            {/* Hue slider. */}
            <div
                ref={hueRef}
                onPointerDown={(e) => {
                    e.currentTarget.setPointerCapture(e.pointerId);
                    onHuePointer(e);
                }}
                onPointerMove={(e) => {
                    if (e.buttons === 1) onHuePointer(e);
                }}
                css={css`
                    position: relative;
                    height: 14px;
                    margin-top: 10px;
                    border-radius: 7px;
                    cursor: ew-resize;
                    background: linear-gradient(
                        to right,
                        #f00 0%,
                        #ff0 17%,
                        #0f0 33%,
                        #0ff 50%,
                        #00f 67%,
                        #f0f 83%,
                        #f00 100%
                    );
                `}
            >
                <span
                    style={{ left: `${(hsva.h / 360) * 100}%` }}
                    css={sliderThumb}
                />
            </div>
            {/* Transparency (alpha) slider: transparent -> opaque current color, over a checkerboard. */}
            <div
                ref={alphaRef}
                onPointerDown={(e) => {
                    e.currentTarget.setPointerCapture(e.pointerId);
                    onAlphaPointer(e);
                }}
                onPointerMove={(e) => {
                    if (e.buttons === 1) onAlphaPointer(e);
                }}
                css={css`
                    position: relative;
                    height: 14px;
                    margin-top: 10px;
                    border-radius: 7px;
                    cursor: ew-resize;
                    ${checkerboardBackground}
                `}
            >
                <span
                    style={{
                        backgroundImage: `linear-gradient(to right, rgba(0,0,0,0), ${opaqueCurrent})`,
                    }}
                    css={css`
                        position: absolute;
                        inset: 0;
                        border-radius: 7px;
                    `}
                />
                <span style={{ left: `${hsva.a * 100}%` }} css={sliderThumb} />
            </div>
            {/* Eyedropper + hex field + current-color preview. */}
            <div
                css={css`
                    display: flex;
                    align-items: center;
                    gap: 8px;
                    margin-top: 12px;
                `}
            >
                {eyeDropperCtor && (
                    <button
                        type="button"
                        title="Sample a color from the screen"
                        onClick={useEyedropper}
                        css={css`
                            flex-shrink: 0;
                            display: inline-flex;
                            border: none;
                            background: transparent;
                            cursor: pointer;
                            padding: 2px;
                            color: #000;
                        `}
                    >
                        <svg
                            width="18"
                            height="18"
                            viewBox="0 0 24 24"
                            fill="none"
                            stroke="currentColor"
                            strokeWidth="2"
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            aria-hidden="true"
                        >
                            <path d="M2 22l1-4 11-11 3 3L6 21l-4 1z" />
                            <path d="M15 6l3-3a2.1 2.1 0 0 1 3 3l-3 3" />
                        </svg>
                    </button>
                )}
                <input
                    type="text"
                    value={hexText}
                    spellCheck={false}
                    onChange={(e) => setHexText(e.target.value)}
                    onBlur={() => commitHex(hexText)}
                    onKeyDown={(e) => {
                        if (e.key === "Enter") commitHex(hexText);
                    }}
                    css={css`
                        flex: 1;
                        min-width: 0;
                        font-size: 13px;
                        font-family: monospace;
                        text-align: center;
                        padding: 5px 6px;
                        border: 1px solid #c0c0c0;
                        border-radius: 4px;
                    `}
                />
                <span
                    title={valueColor}
                    css={css`
                        position: relative;
                        flex-shrink: 0;
                        width: 44px;
                        height: 26px;
                        border-radius: 4px;
                        border: 1px solid rgba(0, 0, 0, 0.35);
                        overflow: hidden;
                        ${checkerboardBackground}
                    `}
                >
                    <span
                        style={{ backgroundColor: valueColor }}
                        css={css`
                            position: absolute;
                            inset: 0;
                        `}
                    />
                </span>
            </div>
            {/* The theme's swatches (black, white, and the theme's colors). */}
            <div
                css={css`
                    display: flex;
                    flex-wrap: wrap;
                    gap: 5px;
                    margin-top: 12px;
                `}
            >
                {props.swatches.map((swatch) => (
                    <button
                        key={swatch}
                        type="button"
                        title={swatch}
                        onClick={() => props.onChange(swatch)}
                        css={css`
                            position: relative;
                            width: 22px;
                            height: 22px;
                            border-radius: 4px;
                            border: 1px solid rgba(0, 0, 0, 0.35);
                            cursor: pointer;
                            padding: 0;
                            overflow: hidden;
                            ${checkerboardBackground}
                            ${valueColor === swatch
                                ? "outline: 2px solid #1d94a4; outline-offset: 1px;"
                                : ""}
                        `}
                    >
                        <span
                            style={{ backgroundColor: swatch }}
                            css={css`
                                position: absolute;
                                inset: 0;
                            `}
                        />
                    </button>
                ))}
            </div>
        </div>
    );

    return createPortal(popover, props.ownerDocument.body);
};

const sliderThumb = css`
    position: absolute;
    top: 50%;
    width: 6px;
    height: 18px;
    margin: -9px 0 0 -3px;
    border-radius: 3px;
    background: white;
    border: 1px solid #b0b0b0;
    pointer-events: none;
`;
