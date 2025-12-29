import { css } from "@emotion/react";
import * as React from "react";
import { useEffect, useRef, useState } from "react";
import { ColorResult, RGBColor } from "react-color";
import BloomSketchPicker from "./bloomSketchPicker";
import ColorSwatch, { IColorInfo } from "./colorSwatch";
import tinycolor from "tinycolor2";
import { HexColorInput } from "./hexColorInput";
import { useL10n } from "../l10nHooks";
import IconButton from "@mui/material/IconButton";
import Typography from "@mui/material/Typography";
import ColorizeIcon from "@mui/icons-material/Colorize";
import { getColorInfoFromSpecialNameOrColorString } from "./bloomPalette";

// We are combining parts of the 'react-color' component set with our own list of swatches.
// The reason for using our own swatches is so we can support swatches with gradients and alpha.
interface IColorPickerProps {
    transparency?: boolean;
    noGradientSwatches?: boolean;
    onChange: (color: IColorInfo) => void;
    currentColor: IColorInfo;
    swatchColors: IColorInfo[];
    includeDefault?: boolean;
    onDefaultClick?: () => void;
    onEyedropperActiveChange?: (active: boolean) => void;
    eyedropperBackdropSelector?: string;
    //defaultColor?: IColorInfo;  will eventually need this
}

type EyeDropperResult = { sRGBHex: string };
type EyeDropper = { open: () => Promise<EyeDropperResult> };
type EyeDropperConstructor = { new (): EyeDropper };

const getEyeDropperConstructor = (): EyeDropperConstructor | undefined => {
    let iframeWindow:
        | (Window & { EyeDropper?: EyeDropperConstructor })
        | null
        | undefined;
    try {
        const iframe = parent.window.document.getElementById(
            "page",
        ) as HTMLIFrameElement | null;
        iframeWindow = iframe?.contentWindow as
            | (Window & { EyeDropper?: EyeDropperConstructor })
            | null;
    } catch {
        iframeWindow = undefined;
    }
    const topWindow = window as Window & { EyeDropper?: EyeDropperConstructor };
    return iframeWindow?.EyeDropper ?? topWindow.EyeDropper;
};

const kEyedropperBackdropStyleId = "bloom-eyedropper-backdrop-style";
const defaultEyedropperBackdropSelector = ".MuiBackdrop-root";

const setEyedropperBackdropTransparent = (
    selector: string | undefined,
    enabled: boolean,
): void => {
    const resolvedSelector = selector ?? defaultEyedropperBackdropSelector;
    if (!resolvedSelector) {
        return;
    }

    const existing = document.getElementById(
        kEyedropperBackdropStyleId,
    ) as HTMLStyleElement | null;

    if (enabled) {
        if (existing && existing.textContent?.includes(resolvedSelector)) {
            return;
        }
        const style = existing ?? document.createElement("style");
        style.id = kEyedropperBackdropStyleId;
        style.textContent = `
            ${resolvedSelector} {
                background-color: transparent !important;
            }
        `;
        if (!existing) {
            document.head.appendChild(style);
        }
    } else if (existing) {
        existing.remove();
    }
};

const setPageScalingDisabled = (disabled: boolean): (() => void) => {
    if (!disabled) {
        return () => {};
    }

    // Bloom applies page zoom using a transform on this element (see editViewFrame.ts setZoom()).
    // WebView2's EyeDropper sampling can be offset when the page content is transformed.
    const iframe = parent.window.document.getElementById(
        "page",
    ) as HTMLIFrameElement | null;
    const iframeDoc = iframe?.contentWindow?.document;
    const container = iframeDoc?.getElementById(
        "page-scaling-container",
    ) as HTMLElement | null;

    if (!container) {
        return () => {};
    }

    const previousTransform = container.style.transform;
    const previousWidth = container.style.width;
    const previousTransformOrigin = container.style.transformOrigin;

    container.style.transform = "";
    container.style.width = "";
    container.style.transformOrigin = "";

    return () => {
        container.style.transform = previousTransform;
        container.style.width = previousWidth;
        container.style.transformOrigin = previousTransformOrigin;
    };
};

export const ColorPicker: React.FunctionComponent<IColorPickerProps> = (
    props,
) => {
    const [eyedropperActive, setEyedropperActive] = useState(false);
    const mountedRef = useRef(true);
    const backdropSelector =
        props.eyedropperBackdropSelector ?? defaultEyedropperBackdropSelector;
    const hasNativeEyedropper = !!getEyeDropperConstructor();

    // Use a content-based key so we detect when the color content changes,
    // even if the object reference is the same (e.g., eyedropper mutations).
    const currentColorKey =
        props.currentColor.colors.join("|") + "|" + props.currentColor.opacity;

    // Track mount state so we don't update state after unmount, and to ensure any temporary
    // backdrop overrides are removed if the component unmounts while the eyedropper is active.
    useEffect(() => {
        mountedRef.current = true;
        return () => {
            mountedRef.current = false;
            setEyedropperBackdropTransparent(backdropSelector, false);
        };
    }, [backdropSelector]);

    const defaultStyleLabel = useL10n(
        "Default for style",
        "EditTab.DirectFormatting.labelForDefaultColor",
    );

    const changeColor = (swatchColor: IColorInfo) => {
        const clonedColor: IColorInfo = {
            ...swatchColor,
            colors: [...swatchColor.colors],
        };
        props.onChange(clonedColor);
    };

    // Handler for when the user clicks on a swatch at the bottom of the picker.
    const handleSwatchClick = (swatchColor: IColorInfo) => () => {
        changeColor(swatchColor);
    };

    // Handler for when the user clicks/drags in the BloomSketchPicker (Saturation, Hue and Alpha).
    const handlePickerChange = (color: ColorResult) => {
        const newColor = getColorInfoFromColorResult(color, "");
        changeColor(newColor);
    };

    // Handler for when the user changes the hex code value (including pasting).
    const handleHexCodeChange = (hexColor: string) => {
        const newColor = {
            colors: [hexColor],
            opacity: props.currentColor.opacity, // Don't change opacity
        };
        changeColor(newColor);
    };

    const getColorInfoFromColorResult = (
        color: ColorResult,
        customName: string,
    ): IColorInfo => {
        // A color that comes from a react-color component (not from clicking on a swatch),
        // cannot be a gradient.
        let opacity = color.rgb.a;
        // ColorResult (from react-color) CAN have undefined alpha in its RGBColor, so we just
        // check here and assume the color is opaque if the alpha channel is undefined.
        if (opacity === undefined) {
            opacity = 1.0;
        }
        let colorString = color.hex;
        if (opacity === 0.0) {
            colorString = "transparent";
        }
        return {
            name: customName,
            colors: [colorString],
            opacity: opacity,
        };
    };

    const getRgbaOfCurrentColor = (): RGBColor => {
        const rgbColor = tinycolor(props.currentColor.colors[0]).toRgb();
        rgbColor.a = props.currentColor.opacity;
        return rgbColor;
    };

    const handleEyedropperClick = async (): Promise<void> => {
        if (eyedropperActive) {
            return;
        }

        const constructor = getEyeDropperConstructor();
        if (!constructor) {
            return;
        }

        setEyedropperActive(true);
        props.onEyedropperActiveChange?.(true);
        setEyedropperBackdropTransparent(backdropSelector, true);
        const restorePageScaling = setPageScalingDisabled(true);
        try {
            const result = await new constructor().open();
            if (result?.sRGBHex) {
                changeColor(
                    getColorInfoFromSpecialNameOrColorString(result.sRGBHex),
                );
            }
        } catch {
            // The user can cancel (e.g. Escape), which rejects the promise.
        } finally {
            restorePageScaling();
            setEyedropperBackdropTransparent(backdropSelector, false);
            if (mountedRef.current) {
                setEyedropperActive(false);
                props.onEyedropperActiveChange?.(false);
            }
        }
    };

    const getColorSwatches = () => (
        <React.Fragment>
            {props.swatchColors
                .filter((colorInfo) => {
                    if (props.noGradientSwatches) {
                        return colorInfo.colors.length === 1;
                    } else {
                        return true;
                    }
                })
                .filter((colorInfo) => {
                    return !props.transparency ? colorInfo.opacity === 1 : true;
                })
                .map((colorInfo: IColorInfo, i: number) => (
                    <ColorSwatch
                        colors={colorInfo.colors}
                        name={colorInfo.name}
                        key={i}
                        onClick={handleSwatchClick(colorInfo)}
                        opacity={colorInfo.opacity}
                    />
                ))}
        </React.Fragment>
    );

    return (
        <div
            className="custom-color-picker"
            css={css`
                display: flex;
                align-items: center;
                flex-direction: column;
                overflow-x: hidden;
            `}
        >
            <BloomSketchPicker
                key={currentColorKey}
                noAlphaSlider={!props.transparency}
                // if the current color choice happens to be a gradient, this will be 'white'.
                color={getRgbaOfCurrentColor()}
                onChange={handlePickerChange}
                currentOpacity={props.currentColor.opacity}
            />
            <div
                css={css`
                    height: 26px;
                    width: 225px;
                    margin-top: 16px;
                    display: flex;
                    flex-direction: row;
                    justify-content: space-between;
                    align-items: center;
                    align-self: center;
                `}
            >
                {hasNativeEyedropper && (
                    <IconButton
                        size="medium"
                        title="Sample Color"
                        onClick={handleEyedropperClick}
                        disabled={eyedropperActive}
                        css={css`
                            padding: 2px;
                        `}
                    >
                        <ColorizeIcon
                            fontSize="medium"
                            css={css`
                                color: #000;
                            `}
                        />
                    </IconButton>
                )}
                <HexColorInput
                    initial={props.currentColor}
                    onChangeComplete={handleHexCodeChange}
                />
                <ColorSwatch
                    colors={props.currentColor.colors}
                    opacity={props.currentColor.opacity}
                    width={48}
                    height={26}
                />
            </div>
            <div
                css={css`
                    margin-top: 20px;
                    display: flex;
                    flex: 2;
                    flex-direction: column;
                    padding: 0 0 0 8px;
                    max-width: 209px; // 225px less margin and padding of 8px each
                `}
                className="swatch-section"
            >
                {props.includeDefault && (
                    <div
                        css={css`
                            display: flex;
                            flex-direction: row;
                            margin-left: 8px;
                        `}
                        onClick={() => {
                            if (props.onDefaultClick) props.onDefaultClick();
                        }}
                    >
                        {/* Temporary substitution until we know the default style color. */}
                        <div
                            css={css`
                                width: 20px;
                                height: 20px;
                                border: 1px solid black;
                                box-sizing: border-box;
                                background: linear-gradient(
                                    to top left,
                                    rgba(255, 255, 255, 1) 0%,
                                    rgba(255, 255, 255, 1) calc(50% - 0.8px),
                                    rgba(0, 0, 0, 1) 50%,
                                    rgba(255, 255, 255, 1) calc(50% + 0.8px),
                                    rgba(255, 255, 255, 1) 100%
                                );
                            `}
                        />
                        {/* <ColorSwatch
                            colors={props.defaultColor.colors}
                            opacity={props.defaultColor.opacity}
                            onClick={() => {
                                if (props.onDefaultClick)
                                    props.onDefaultClick();
                            }}
                        /> */}
                        <Typography
                            css={css`
                                margin-left: 6px !important;
                            `}
                        >
                            {defaultStyleLabel}
                        </Typography>
                    </div>
                )}
                <div
                    css={css`
                        display: flex;
                        flex-direction: row;
                        flex-wrap: wrap;
                    `}
                    className="swatch-row"
                >
                    {getColorSwatches()}
                </div>
            </div>
        </div>
    );
};

export default ColorPicker;
