import { css } from "@emotion/react";
import * as React from "react";
import { useEffect, useMemo, useRef, useState } from "react";
import { ColorResult, RGBColor } from "react-color";
import tinycolor from "tinycolor2";
import { kUiFontStack } from "../../bloomMaterialUITheme";

const kWheelDiameter = 280;
const kMaxSaturation = 1;

// Global drag state - survives component re-renders/re-mounts
const globalDragState = {
    activeSlider: null as string | null,
    activePicker: false,
    activePointerId: undefined as number | undefined,
};

const clampNumber = (value: number, min: number, max: number) =>
    Math.min(max, Math.max(min, value));

const useSliderDragHandlers = (
    label: string,
    sliderRef: React.RefObject<HTMLDivElement>,
    updateFromClientXRef: React.MutableRefObject<(clientX: number) => void>,
): void => {
    // Keep slider drag responsive even when pointer leaves the control.
    useEffect(() => {
        const isThisSliderDragging = () =>
            globalDragState.activeSlider === label;

        const handleMouseDown = (event: MouseEvent) => {
            if (!sliderRef.current) {
                return;
            }
            if (!sliderRef.current.contains(event.target as Node)) {
                return;
            }
            globalDragState.activeSlider = label;
            updateFromClientXRef.current(event.clientX);
        };
        const handleMouseMove = (event: MouseEvent) => {
            if (!isThisSliderDragging()) {
                return;
            }
            updateFromClientXRef.current(event.clientX);
        };
        const handleMouseUp = () => {
            if (isThisSliderDragging()) {
                globalDragState.activeSlider = null;
            }
        };
        const handlePointerDown = (event: PointerEvent) => {
            if (!sliderRef.current) {
                return;
            }
            if (!sliderRef.current.contains(event.target as Node)) {
                return;
            }
            globalDragState.activeSlider = label;
            globalDragState.activePointerId = event.pointerId;
            sliderRef.current.setPointerCapture(event.pointerId);
            updateFromClientXRef.current(event.clientX);
        };
        const handlePointerMove = (event: PointerEvent) => {
            if (!isThisSliderDragging()) {
                return;
            }
            updateFromClientXRef.current(event.clientX);
        };
        const handlePointerUp = () => {
            if (isThisSliderDragging()) {
                if (
                    sliderRef.current &&
                    globalDragState.activePointerId !== undefined
                ) {
                    sliderRef.current.releasePointerCapture(
                        globalDragState.activePointerId,
                    );
                }
                globalDragState.activeSlider = null;
                globalDragState.activePointerId = undefined;
            }
        };

        const slider = sliderRef.current;
        document.addEventListener("mousedown", handleMouseDown, true);
        document.addEventListener("mousemove", handleMouseMove, true);
        document.addEventListener("mouseup", handleMouseUp, true);
        document.addEventListener("pointerdown", handlePointerDown, true);
        document.addEventListener("pointerup", handlePointerUp, true);
        // pointermove must be on the element itself when pointer capture is used
        slider?.addEventListener("pointermove", handlePointerMove);
        return () => {
            document.removeEventListener("mousedown", handleMouseDown, true);
            document.removeEventListener("mousemove", handleMouseMove, true);
            document.removeEventListener("mouseup", handleMouseUp, true);
            document.removeEventListener(
                "pointerdown",
                handlePointerDown,
                true,
            );
            document.removeEventListener("pointerup", handlePointerUp, true);
            slider?.removeEventListener("pointermove", handlePointerMove);
        };
    }, [label]);
};

const usePickerDragHandlers = (
    pickerRef: React.RefObject<HTMLDivElement>,
    updateFromClientPointRef: React.MutableRefObject<
        (clientX: number, clientY: number) => void
    >,
): void => {
    // Ensure the hue/saturation wheel continues updating while dragging.
    useEffect(() => {
        const isPickerDragging = () => globalDragState.activePicker;

        const handleMouseDown = (event: MouseEvent) => {
            if (!pickerRef.current) {
                return;
            }
            if (!pickerRef.current.contains(event.target as Node)) {
                return;
            }
            globalDragState.activePicker = true;
            updateFromClientPointRef.current(event.clientX, event.clientY);
        };
        const handleMouseMove = (event: MouseEvent) => {
            if (!isPickerDragging()) {
                return;
            }
            updateFromClientPointRef.current(event.clientX, event.clientY);
        };
        const handleMouseUp = () => {
            if (isPickerDragging()) {
                globalDragState.activePicker = false;
            }
        };
        const handlePointerDown = (event: PointerEvent) => {
            if (!pickerRef.current) {
                return;
            }
            if (!pickerRef.current.contains(event.target as Node)) {
                return;
            }
            globalDragState.activePicker = true;
            globalDragState.activePointerId = event.pointerId;
            pickerRef.current.setPointerCapture(event.pointerId);
            updateFromClientPointRef.current(event.clientX, event.clientY);
        };
        const handlePointerMove = (event: PointerEvent) => {
            if (!isPickerDragging()) {
                return;
            }
            updateFromClientPointRef.current(event.clientX, event.clientY);
        };
        const handlePointerUp = () => {
            if (isPickerDragging()) {
                if (
                    pickerRef.current &&
                    globalDragState.activePointerId !== undefined
                ) {
                    pickerRef.current.releasePointerCapture(
                        globalDragState.activePointerId,
                    );
                }
                globalDragState.activePicker = false;
                globalDragState.activePointerId = undefined;
            }
        };

        const picker = pickerRef.current;
        document.addEventListener("mousedown", handleMouseDown, true);
        document.addEventListener("mousemove", handleMouseMove, true);
        document.addEventListener("mouseup", handleMouseUp, true);
        document.addEventListener("pointerdown", handlePointerDown, true);
        document.addEventListener("pointerup", handlePointerUp, true);
        // pointermove must be on the element itself when pointer capture is used
        picker?.addEventListener("pointermove", handlePointerMove);
        return () => {
            document.removeEventListener("mousedown", handleMouseDown, true);
            document.removeEventListener("mousemove", handleMouseMove, true);
            document.removeEventListener("mouseup", handleMouseUp, true);
            document.removeEventListener(
                "pointerdown",
                handlePointerDown,
                true,
            );
            document.removeEventListener("pointerup", handlePointerUp, true);
            picker?.removeEventListener("pointermove", handlePointerMove);
        };
    }, []);
};

const interpolateRgb = (
    start: RGBColor,
    end: RGBColor,
    t: number,
): RGBColor => {
    const clampedT = clampNumber(t, 0, 1);
    return {
        r: Math.round(start.r + (end.r - start.r) * clampedT),
        g: Math.round(start.g + (end.g - start.g) * clampedT),
        b: Math.round(start.b + (end.b - start.b) * clampedT),
    };
};

const hsvToRgb = (h: number, s: number, v: number): RGBColor => {
    const normalizedH = ((h % 360) + 360) % 360;
    const clampedS = clampNumber(s, 0, 1);
    const clampedV = clampNumber(v, 0, 1);
    const rgb = tinycolor({ h: normalizedH, s: clampedS, v: clampedV }).toRgb();
    return { r: rgb.r, g: rgb.g, b: rgb.b };
};

const rgbToHsv = (rgb: RGBColor): { h: number; s: number; v: number } => {
    const hsv = tinycolor(rgb).toHsv();
    return {
        h: ((hsv.h % 360) + 360) % 360,
        s: clampNumber(hsv.s, 0, 1),
        v: clampNumber(hsv.v, 0, 1),
    };
};

const buildColorResult = (rgb: RGBColor, opacity: number): ColorResult => {
    const withAlpha = {
        r: rgb.r,
        g: rgb.g,
        b: rgb.b,
        a: opacity,
    };
    const tiny = tinycolor(withAlpha);
    return {
        hex: tiny.toHexString(),
        rgb: withAlpha,
        hsl: tiny.toHsl(),
        hsv: tiny.toHsv(),
    };
};

const getAlphaGradient = (rgb: RGBColor): string =>
    `linear-gradient(to right, rgba(${rgb.r}, ${rgb.g}, ${rgb.b}, 0), rgba(${rgb.r}, ${rgb.g}, ${rgb.b}, 1))`;

const alphaCheckerboardBackground =
    "linear-gradient(45deg, #bdbdbd 25%, transparent 25%, transparent 75%, #bdbdbd 75%, #bdbdbd), " +
    "linear-gradient(45deg, #bdbdbd 25%, transparent 25%, transparent 75%, #bdbdbd 75%, #bdbdbd)";

const OklchSlider: React.FunctionComponent<{
    label: string;
    value: number;
    min: number;
    max: number;
    step: number;
    background: string;
    backgroundSize?: string;
    backgroundPosition?: string;
    backgroundRepeat?: string;
    thumbColor: string;
    onChange: (value: number) => void;
    title?: string;
    testId?: string;
}> = (props) => {
    const sliderRef = useRef<HTMLDivElement>(null);
    const updateFromClientXRef = useRef<(clientX: number) => void>(() => {});

    const updateFromClientX = (clientX: number) => {
        const rect = sliderRef.current?.getBoundingClientRect();
        if (!rect) {
            return;
        }
        const x = clampNumber(clientX - rect.left, 0, rect.width);
        const ratio = rect.width > 0 ? x / rect.width : 0;
        const rawValue = props.min + ratio * (props.max - props.min);
        const stepped =
            props.step > 0
                ? Math.round(rawValue / props.step) * props.step
                : rawValue;
        const clamped = clampNumber(stepped, props.min, props.max);
        props.onChange(clamped);
    };

    updateFromClientXRef.current = updateFromClientX;

    useSliderDragHandlers(props.label, sliderRef, updateFromClientXRef);

    const percent =
        props.max === props.min
            ? 0
            : (props.value - props.min) / (props.max - props.min);

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                gap: 1px;
            `}
        >
            <div
                css={css`
                    font-size: 12px;
                    font-family: ${kUiFontStack};
                    color: #000;
                `}
            >
                {props.label}
            </div>
            <div
                ref={sliderRef}
                data-testid={props.testId}
                title={props.title}
                css={css`
                    position: relative;
                    height: 14px;
                    border-radius: 2px;
                    background: ${props.background};
                    background-size: ${props.backgroundSize ?? "auto"};
                    background-position: ${props.backgroundPosition ?? "0 0"};
                    background-repeat: ${props.backgroundRepeat ?? "repeat"};
                    cursor: ew-resize;
                `}
            >
                <div
                    css={css`
                        position: absolute;
                        left: ${clampNumber(percent, 0, 1) * 100}%;
                        top: 50%;
                        transform: translate(-50%, -50%);
                        pointer-events: none;
                    `}
                >
                    <div
                        css={css`
                            width: 7px;
                            height: 14px;
                            background-color: ${props.thumbColor};
                            border-radius: 2px;
                            box-shadow:
                                0 0 0 1px rgba(0, 0, 0, 0.9),
                                0 0 0 2px rgba(255, 255, 255, 0.9);
                        `}
                    />
                </div>
            </div>
        </div>
    );
};

const BloomSketchPicker: React.FunctionComponent<{
    // Set to 'true' to eliminate alpha slider (e.g. text color)
    noAlphaSlider?: boolean;
    onChange: (color: ColorResult) => void;
    // Needed for tooltip on Alpha slider
    currentOpacity: number;
    color: RGBColor;
    colorKey?: string;
}> = (props) => {
    const [hsv, setHsv] = useState(() => rgbToHsv(props.color));
    const canvasRef = useRef<HTMLCanvasElement>(null);
    const pickerRef = useRef<HTMLDivElement>(null);
    const lastRgbRef = useRef<RGBColor>();
    const updateFromClientPointRef = useRef<
        (clientX: number, clientY: number) => void
    >(() => {});

    // Sync with parent color changes (swatches/hex input) so sliders reflect external updates.
    useEffect(() => {
        const lastRgb = lastRgbRef.current;
        if (
            lastRgb &&
            lastRgb.r === props.color.r &&
            lastRgb.g === props.color.g &&
            lastRgb.b === props.color.b
        ) {
            return;
        }
        setHsv(rgbToHsv(props.color));
    }, [props.color.b, props.color.g, props.color.r, props.colorKey]);

    const updateColor = (
        hValue: number,
        sValue: number,
        vValue: number,
        opacityValue?: number,
    ) => {
        const normalizedH = ((hValue % 360) + 360) % 360;
        const clampedS = clampNumber(sValue, 0, kMaxSaturation);
        const clampedV = clampNumber(vValue, 0, 1);
        const nextOpacity =
            opacityValue === undefined ? props.currentOpacity : opacityValue;
        setHsv({ h: normalizedH, s: clampedS, v: clampedV });
        const rgb = hsvToRgb(normalizedH, clampedS, clampedV);
        lastRgbRef.current = rgb;
        props.onChange(buildColorResult(rgb, nextOpacity));
    };

    const updateFromClientPoint = (clientX: number, clientY: number) => {
        const rect = pickerRef.current?.getBoundingClientRect();
        if (!rect) {
            return;
        }
        const x = clampNumber(clientX - rect.left, 0, rect.width);
        const y = clampNumber(clientY - rect.top, 0, rect.height);
        const centerX = rect.width / 2;
        const centerY = rect.height / 2;
        const dx = x - centerX;
        const dy = y - centerY;
        const distance = Math.sqrt(dx * dx + dy * dy);
        const radius = Math.min(rect.width, rect.height) / 2;
        const chromaRatio =
            radius > 0 ? clampNumber(distance / radius, 0, 1) : 0;
        const hueRadians = Math.atan2(dy, dx);
        const hueDegrees = ((hueRadians * 180) / Math.PI + 360) % 360;
        const nextS = chromaRatio * kMaxSaturation;
        updateColor(hueDegrees, nextS, hsv.v);
    };

    updateFromClientPointRef.current = updateFromClientPoint;

    const handleLightnessChange = (value: number) => {
        const nextV = value / 100;
        updateColor(hsv.h, hsv.s, nextV);
    };

    const handleChromaChange = (value: number) => {
        const nextS = value / 100;
        updateColor(hsv.h, nextS, hsv.v);
    };

    const handleHueChange = (value: number) => {
        const nextH = value;
        updateColor(nextH, hsv.s, hsv.v);
    };

    const handleOpacityChange = (value: number) => {
        const nextOpacity = value / 100;
        updateColor(hsv.h, hsv.s, hsv.v, nextOpacity);
    };

    const hueGradientInfo = useMemo(() => {
        const stopCount = 6;
        const stops: string[] = [];
        const stopRgbs: RGBColor[] = [];
        for (let index = 0; index <= stopCount; index++) {
            const hue = (index / stopCount) * 360;
            const rgb = hsvToRgb(hue, 1, 1);
            stopRgbs.push(rgb);
            const color = tinycolor(rgb).toHexString();
            stops.push(`${color} ${(index / stopCount) * 100}%`);
        }
        return {
            css: `linear-gradient(to right, ${stops.join(", ")})`,
            stopCount,
            stopRgbs,
        };
    }, []);

    const hueGradient = hueGradientInfo.css;

    const lightnessGradientInfo = useMemo(() => {
        const startRgb = hsvToRgb(hsv.h, hsv.s, 0);
        const endRgb = hsvToRgb(hsv.h, hsv.s, 1);
        const start = tinycolor(startRgb).toHexString();
        const end = tinycolor(endRgb).toHexString();
        return {
            css: `linear-gradient(to right, ${start}, ${end})`,
            startRgb,
            endRgb,
        };
    }, [hsv.h, hsv.s]);

    const lightnessGradient = lightnessGradientInfo.css;

    const chromaGradientInfo = useMemo(() => {
        const startRgb = hsvToRgb(hsv.h, 0, hsv.v);
        const endRgb = hsvToRgb(hsv.h, 1, hsv.v);
        const start = tinycolor(startRgb).toHexString();
        const end = tinycolor(endRgb).toHexString();
        return {
            css: `linear-gradient(to right, ${start}, ${end})`,
            startRgb,
            endRgb,
        };
    }, [hsv.h, hsv.v]);

    const chromaGradient = chromaGradientInfo.css;

    const transparencyString =
        ((1 - props.currentOpacity) * 100).toFixed(0) + "%";

    const hueThumbColor = useMemo(() => {
        const normalizedHue = ((hsv.h % 360) + 360) % 360;
        const segmentSize = 360 / hueGradientInfo.stopCount;
        const startIndex = Math.floor(normalizedHue / segmentSize);
        const endIndex = startIndex + 1;
        const t = (normalizedHue - startIndex * segmentSize) / segmentSize;
        const startRgb = hueGradientInfo.stopRgbs[startIndex];
        const endRgb = hueGradientInfo.stopRgbs[endIndex];
        const rgb = interpolateRgb(startRgb, endRgb, t);
        return tinycolor(rgb).toRgbString();
    }, [hueGradientInfo.stopCount, hueGradientInfo.stopRgbs, hsv.h]);

    const lightnessThumbColor = useMemo(() => {
        const rgb = interpolateRgb(
            lightnessGradientInfo.startRgb,
            lightnessGradientInfo.endRgb,
            hsv.v,
        );
        return tinycolor(rgb).toRgbString();
    }, [hsv.v, lightnessGradientInfo.endRgb, lightnessGradientInfo.startRgb]);

    const chromaThumbColor = useMemo(() => {
        const rgb = interpolateRgb(
            chromaGradientInfo.startRgb,
            chromaGradientInfo.endRgb,
            hsv.s,
        );
        return tinycolor(rgb).toRgbString();
    }, [chromaGradientInfo.endRgb, chromaGradientInfo.startRgb, hsv.s]);
    const alphaGradient = getAlphaGradient(props.color);
    const alphaChannel = clampNumber(props.currentOpacity, 0, 1);
    const alphaShade = Math.round(255 * (1 - alphaChannel));
    const alphaThumbColor = tinycolor({
        r: alphaShade,
        g: alphaShade,
        b: alphaShade,
    }).toRgbString();

    // Draw the hue/saturation wheel for the current value.
    useEffect(() => {
        const canvas = canvasRef.current;
        if (!canvas) {
            return;
        }
        const context = canvas.getContext("2d");
        if (!context) {
            return;
        }
        const dpr = window.devicePixelRatio || 1;
        const pixelDiameter = Math.max(1, Math.round(kWheelDiameter * dpr));

        if (canvas.width !== pixelDiameter || canvas.height !== pixelDiameter) {
            canvas.width = pixelDiameter;
            canvas.height = pixelDiameter;
        }

        const image = context.createImageData(pixelDiameter, pixelDiameter);
        const center = (pixelDiameter - 1) / 2;
        const radius = center;

        for (let y = 0; y < pixelDiameter; y++) {
            for (let x = 0; x < pixelDiameter; x++) {
                const dx = x - center;
                const dy = y - center;
                const distance = Math.sqrt(dx * dx + dy * dy);
                if (distance > radius) {
                    continue;
                }
                const saturationRatio = radius > 0 ? distance / radius : 0;
                const hueRadians = Math.atan2(dy, dx);
                const hueDegrees = ((hueRadians * 180) / Math.PI + 360) % 360;
                const sValue = saturationRatio * kMaxSaturation;
                const rgb = hsvToRgb(hueDegrees, sValue, hsv.v);
                const offset = (y * pixelDiameter + x) * 4;
                image.data[offset] = rgb.r;
                image.data[offset + 1] = rgb.g;
                image.data[offset + 2] = rgb.b;
                image.data[offset + 3] = 255;
            }
        }

        context.putImageData(image, 0, 0);
    }, [hsv.v]);

    usePickerDragHandlers(pickerRef, updateFromClientPointRef);

    const wheelRadius = kWheelDiameter / 2;
    const saturationRatio = clampNumber(hsv.s, 0, 1);
    const hueRadians = (hsv.h * Math.PI) / 180;
    const cursorLeft = `${wheelRadius + Math.cos(hueRadians) * wheelRadius * saturationRatio}px`;
    const cursorTop = `${wheelRadius + Math.sin(hueRadians) * wheelRadius * saturationRatio}px`;

    const sliderConfigs = [
        {
            key: "lightness",
            label: "Lightness",
            min: 0,
            max: 100,
            step: 1,
            value: Math.round(hsv.v * 100),
            onChange: handleLightnessChange,
            background: lightnessGradient,
            thumbColor: lightnessThumbColor,
            testId: "oklch-slider-L",
        },
        {
            key: "intensity",
            label: "Intensity (Chroma)",
            min: 0,
            max: 100,
            step: 1,
            value: Math.round(hsv.s * 100),
            onChange: handleChromaChange,
            background: chromaGradient,
            thumbColor: chromaThumbColor,
            testId: "oklch-slider-C",
        },
        {
            key: "transparency",
            label: "Transparency",
            min: 0,
            max: 100,
            step: 1,
            value: Math.round(props.currentOpacity * 100),
            onChange: handleOpacityChange,
            background: `${alphaGradient}, ${alphaCheckerboardBackground}`,
            backgroundSize: "100% 100%, 12px 12px, 12px 12px",
            backgroundPosition: "0 0, 0 0, 6px 6px",
            backgroundRepeat: "no-repeat, repeat, repeat",
            title: transparencyString,
            thumbColor: alphaThumbColor,
            testId: "oklch-slider-A",
            hidden: props.noAlphaSlider === true,
        },
    ];

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                width: ${kWheelDiameter}px;
                padding: 0 2px;
                gap: 10px;
            `}
        >
            <div
                // The change of cursor is here, instead of in a custom cursor, so it shows up over the
                // entire lightness/chroma block, not just where the "dot" is.
                ref={pickerRef}
                data-testid="oklch-2d-picker"
                css={css`
                    position: relative;
                    height: ${kWheelDiameter}px;
                    width: ${kWheelDiameter}px;
                    :hover {
                        cursor: crosshair;
                    }
                `}
            >
                <canvas
                    ref={canvasRef}
                    css={css`
                        display: block;
                        width: ${kWheelDiameter}px;
                        height: ${kWheelDiameter}px;
                    `}
                />
                <div
                    css={css`
                        position: absolute;
                        left: ${cursorLeft};
                        top: ${cursorTop};
                        width: 12px;
                        height: 12px;
                        border-radius: 50%;
                        border: 2px solid white;
                        box-shadow: 0 0 0 1px rgba(0, 0, 0, 0.4);
                        transform: translate(-50%, -50%);
                        pointer-events: none;
                    `}
                />
            </div>
            <div
                css={css`
                    display: flex;
                    flex-direction: column;
                    gap: 10px;
                `}
            >
                {/* <OklchSlider
                    label="Hue"
                    min={0}
                    max={360}
                    step={1}
                    value={hsv.h}
                    onChange={handleHueChange}
                    background={hueGradient}
                    thumbColor={hueThumbColor}
                    testId="oklch-slider-H"
                /> */}
                {sliderConfigs
                    .filter((slider) => !slider.hidden)
                    .map((slider) => (
                        <OklchSlider
                            key={slider.key}
                            label={slider.label}
                            min={slider.min}
                            max={slider.max}
                            step={slider.step}
                            value={slider.value}
                            onChange={slider.onChange}
                            background={slider.background}
                            backgroundSize={slider.backgroundSize}
                            backgroundPosition={slider.backgroundPosition}
                            backgroundRepeat={slider.backgroundRepeat}
                            title={slider.title}
                            thumbColor={slider.thumbColor}
                            testId={slider.testId}
                        />
                    ))}
            </div>
        </div>
    );
};

export default BloomSketchPicker;
