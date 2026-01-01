import { css } from "@emotion/react";
import * as React from "react";
import { useEffect, useMemo, useRef, useState } from "react";
import { ColorResult, RGBColor } from "react-color";
import tinycolor from "tinycolor2";
import { kUiFontStack } from "../../bloomMaterialUITheme";

// HSV is good for showing the range of colors, while OKLCH is better for
// perceptual lightness/chroma adjustments.
const kWheelDiameter = 280;
const kMaxSaturation = 1;
const kMaxChroma = 0.4;
const kHueLockChromaThreshold = 0.0001;
const kFixedWheelLightness = 0.7;

type WheelMode =
    | "hsv"
    | "hsv-one-way"
    | "oklch-gamut"
    | "oklch-fixed"
    | "oklch-fixed-lightness";

const wheelModeOptions: Array<{
    value: WheelMode;
    label: string;
    oneWay?: boolean;
    showHue?: boolean;
}> = [
    { value: "hsv", label: "HSV Hue/Saturation" },
    { value: "hsv-one-way", label: "Hybrid", oneWay: true },
    {
        value: "oklch-gamut",
        label: "OKLCH Hue/Chroma (Current Lightness, Gamut Limited)",
        showHue: true,
    },
    {
        value: "oklch-fixed",
        label: "OKLCH Hue/Chroma (Current Lightness, Fixed Chroma)",
        showHue: true,
    },
    {
        value: "oklch-fixed-lightness",
        label: "OKLCH Hue/Chroma (Fixed Lightness 0.7)",
        showHue: true,
    },
];

// Global drag state - survives component re-renders/re-mounts
const globalDragState = {
    activeSlider: null as string | null,
    activePicker: false,
    activePointerId: undefined as number | undefined,
};

const clampNumber = (value: number, min: number, max: number) =>
    Math.min(max, Math.max(min, value));

const getHueDelta = (first: number, second: number): number => {
    const raw = Math.abs(first - second) % 360;
    return raw > 180 ? 360 - raw : raw;
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

const srgbToLinear = (value: number): number => {
    const clamped = clampNumber(value, 0, 1);
    return clamped <= 0.04045
        ? clamped / 12.92
        : Math.pow((clamped + 0.055) / 1.055, 2.4);
};

const linearToSrgb = (value: number): number => {
    const clamped = clampNumber(value, 0, 1);
    return clamped <= 0.0031308
        ? clamped * 12.92
        : 1.055 * Math.pow(clamped, 1 / 2.4) - 0.055;
};

const rgbToOklch = (rgb: RGBColor): { l: number; c: number; h: number } => {
    const r = srgbToLinear(rgb.r / 255);
    const g = srgbToLinear(rgb.g / 255);
    const b = srgbToLinear(rgb.b / 255);

    const l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b;
    const m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b;
    const s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b;

    const lRoot = Math.cbrt(l);
    const mRoot = Math.cbrt(m);
    const sRoot = Math.cbrt(s);

    const lLab =
        0.2104542553 * lRoot + 0.793617785 * mRoot - 0.0040720468 * sRoot;
    const aLab =
        1.9779984951 * lRoot - 2.428592205 * mRoot + 0.4505937099 * sRoot;
    const bLab =
        0.0259040371 * lRoot + 0.7827717662 * mRoot - 0.808675766 * sRoot;

    const cLab = Math.sqrt(aLab * aLab + bLab * bLab);
    const hDegrees = ((Math.atan2(bLab, aLab) * 180) / Math.PI + 360) % 360;

    return {
        l: clampNumber(lLab, 0, 1),
        c: Math.max(0, cLab),
        h: hDegrees,
    };
};

const oklchToLinearRgb = (
    l: number,
    c: number,
    h: number,
): { r: number; g: number; b: number } => {
    const clampedL = clampNumber(l, 0, 1);
    const clampedC = Math.max(0, c);
    const hRadians = (h * Math.PI) / 180;
    const a = clampedC * Math.cos(hRadians);
    const b = clampedC * Math.sin(hRadians);

    const lRoot = clampedL + 0.3963377774 * a + 0.2158037573 * b;
    const mRoot = clampedL - 0.1055613458 * a - 0.0638541728 * b;
    const sRoot = clampedL - 0.0894841775 * a - 1.291485548 * b;

    const lLin = lRoot * lRoot * lRoot;
    const mLin = mRoot * mRoot * mRoot;
    const sLin = sRoot * sRoot * sRoot;

    const rLin =
        4.0767416621 * lLin - 3.3077115913 * mLin + 0.2309699292 * sLin;
    const gLin =
        -1.2684380046 * lLin + 2.6097574011 * mLin - 0.3413193965 * sLin;
    const bLin =
        -0.0041960863 * lLin - 0.7034186147 * mLin + 1.707614701 * sLin;

    return { r: rLin, g: gLin, b: bLin };
};

const oklchToRgb = (l: number, c: number, h: number): RGBColor => {
    const linearRgb = oklchToLinearRgb(l, c, h);
    return {
        r: Math.round(clampNumber(linearToSrgb(linearRgb.r), 0, 1) * 255),
        g: Math.round(clampNumber(linearToSrgb(linearRgb.g), 0, 1) * 255),
        b: Math.round(clampNumber(linearToSrgb(linearRgb.b), 0, 1) * 255),
    };
};

const isLinearRgbInGamut = (rgb: {
    r: number;
    g: number;
    b: number;
}): boolean =>
    rgb.r >= 0 &&
    rgb.r <= 1 &&
    rgb.g >= 0 &&
    rgb.g <= 1 &&
    rgb.b >= 0 &&
    rgb.b <= 1;

const findMaxChromaInGamut = (
    l: number,
    h: number,
    chromaLimit: number,
): number => {
    let low = 0;
    let high = Math.max(0, chromaLimit);
    for (let index = 0; index < 24; index++) {
        const mid = (low + high) / 2;
        const linearRgb = oklchToLinearRgb(l, mid, h);
        if (isLinearRgbInGamut(linearRgb)) {
            low = mid;
        } else {
            high = mid;
        }
    }
    return low;
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

    // Listen at the window capture phase to ensure drag works even if an overlay intercepts events.
    // Use global drag state because component may re-render during drag.
    useEffect(() => {
        const isThisSliderDragging = () =>
            globalDragState.activeSlider === props.label;
        const handlePointerDown = (event: PointerEvent) => {
            if (!sliderRef.current) {
                return;
            }
            if (!sliderRef.current.contains(event.target as Node)) {
                return;
            }
            globalDragState.activeSlider = props.label;
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
        document.addEventListener("pointerdown", handlePointerDown, true);
        document.addEventListener("pointerup", handlePointerUp, true);
        // pointermove must be on the element itself when pointer capture is used
        slider?.addEventListener("pointermove", handlePointerMove);
        return () => {
            document.removeEventListener(
                "pointerdown",
                handlePointerDown,
                true,
            );
            document.removeEventListener("pointerup", handlePointerUp, true);
            slider?.removeEventListener("pointermove", handlePointerMove);
        };
    }, [props.label]);

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
    const [oklch, setOklch] = useState(() => rgbToOklch(props.color));
    const canvasRef = useRef<HTMLCanvasElement>(null);
    const pickerRef = useRef<HTMLDivElement>(null);
    const lastRgbRef = useRef<RGBColor>();
    const [wheelMode, setWheelMode] = useState<WheelMode>("hsv-one-way");
    const wheelModeOption = wheelModeOptions.find(
        (option) => option.value === wheelMode,
    );
    const wheelIsOneWay = wheelModeOption?.oneWay === true;
    const showHue = wheelModeOption?.showHue === true;
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
        setOklch(rgbToOklch(props.color));
    }, [props.color.b, props.color.g, props.color.r, props.colorKey]);

    const currentRgb = useMemo(
        () => oklchToRgb(oklch.l, oklch.c, oklch.h),
        [oklch.c, oklch.h, oklch.l],
    );

    const currentHsv = useMemo(
        () => rgbToHsv(currentRgb),
        [currentRgb.b, currentRgb.g, currentRgb.r],
    );

    const isHsvWheel = wheelMode === "hsv" || wheelMode === "hsv-one-way";

    const wheelStateRef = useRef<{
        hue: number;
        radiusRatio: number;
        hsvValue: number;
        oklchLightness: number;
    }>({
        hue: currentHsv.h,
        radiusRatio: clampNumber(currentHsv.s, 0, 1),
        hsvValue: clampNumber(currentHsv.v, 0, 1),
        oklchLightness: clampNumber(oklch.l, 0, 1),
    });

    const stableOklchHueRef = useRef(oklch.h);
    if (oklch.c > kHueLockChromaThreshold) {
        stableOklchHueRef.current = oklch.h;
    }
    const stableOklchHue = stableOklchHueRef.current;
    const wheelOklchLightness =
        wheelMode === "oklch-fixed-lightness"
            ? kFixedWheelLightness
            : wheelIsOneWay
              ? wheelStateRef.current.oklchLightness
              : oklch.l;
    const wheelHsvValue =
        wheelIsOneWay && isHsvWheel
            ? wheelStateRef.current.hsvValue
            : currentHsv.v;

    // Capture the current color when switching wheel modes so one-way modes have a baseline.
    useEffect(() => {
        wheelStateRef.current = {
            hue: currentHsv.h,
            radiusRatio: clampNumber(currentHsv.s, 0, 1),
            hsvValue: clampNumber(currentHsv.v, 0, 1),
            oklchLightness: clampNumber(oklch.l, 0, 1),
        };
    }, [wheelMode]);

    // One-way modes keep their own wheel snapshot; two-way modes use current state directly.

    const maxChroma = useMemo(
        () => findMaxChromaInGamut(oklch.l, stableOklchHue, kMaxChroma),
        [oklch.l, stableOklchHue],
    );

    const maxChromaByHue = useMemo(() => {
        if (wheelMode !== "oklch-gamut") {
            return undefined;
        }
        const values: number[] = [];
        for (let index = 0; index < 360; index++) {
            values.push(
                findMaxChromaInGamut(wheelOklchLightness, index, kMaxChroma),
            );
        }
        return values;
    }, [wheelMode, wheelOklchLightness]);

    const updateFromOklch = (
        nextOklch: { l: number; c: number; h: number },
        opacityValue?: number,
    ) => {
        const nextOpacity =
            opacityValue === undefined ? props.currentOpacity : opacityValue;
        setOklch(nextOklch);
        const rgb = oklchToRgb(nextOklch.l, nextOklch.c, nextOklch.h);
        lastRgbRef.current = rgb;
        props.onChange(buildColorResult(rgb, nextOpacity));
    };

    const updateFromRgb = (rgb: RGBColor, opacityValue?: number) => {
        const nextOpacity =
            opacityValue === undefined ? props.currentOpacity : opacityValue;
        setOklch(rgbToOklch(rgb));
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
        if (isHsvWheel) {
            const nextS = chromaRatio * kMaxSaturation;
            const rgb = hsvToRgb(hueDegrees, nextS, wheelHsvValue);
            updateFromRgb(rgb);
            wheelStateRef.current = {
                hue: hueDegrees,
                radiusRatio: chromaRatio,
                hsvValue: clampNumber(wheelHsvValue, 0, 1),
                oklchLightness: wheelStateRef.current.oklchLightness,
            };
            return;
        }
        const hueIndex = Math.floor(hueDegrees) % 360;
        const chromaLimit =
            wheelMode === "oklch-gamut"
                ? (maxChromaByHue?.[hueIndex] ?? kMaxChroma)
                : kMaxChroma;
        const nextC = chromaRatio * chromaLimit;
        updateFromOklch({
            l: wheelOklchLightness,
            c: nextC,
            h: hueDegrees,
        });
        wheelStateRef.current = {
            hue: hueDegrees,
            radiusRatio: chromaRatio,
            hsvValue: wheelStateRef.current.hsvValue,
            oklchLightness: wheelOklchLightness,
        };
    };

    updateFromClientPointRef.current = updateFromClientPoint;

    const handleLightnessChange = (value: number) => {
        const nextL = clampNumber(value / 100, 0, 1);
        updateFromOklch({ l: nextL, c: oklch.c, h: stableOklchHue });
    };

    const handleChromaChange = (value: number) => {
        const nextC = clampNumber(value / 100, 0, 1) * kMaxChroma;
        updateFromOklch({ l: oklch.l, c: nextC, h: stableOklchHue });
    };

    const handleHueChange = (value: number) => {
        const nextH = value;
        updateFromOklch({ l: oklch.l, c: oklch.c, h: nextH });
    };

    const handleOpacityChange = (value: number) => {
        const nextOpacity = value / 100;
        updateFromOklch(oklch, nextOpacity);
    };

    const handleWheelModeChange = (
        event: React.ChangeEvent<HTMLSelectElement>,
    ) => {
        setWheelMode(event.target.value as WheelMode);
    };

    const hueGradientInfo = useMemo(() => {
        const stopCount = 6;
        const stops: string[] = [];
        const stopRgbs: RGBColor[] = [];
        for (let index = 0; index <= stopCount; index++) {
            const hue = (index / stopCount) * 360;
            const rgb = oklchToRgb(oklch.l, oklch.c, hue);
            stopRgbs.push(rgb);
            const color = tinycolor(rgb).toHexString();
            stops.push(`${color} ${(index / stopCount) * 100}%`);
        }
        return {
            css: `linear-gradient(to right, ${stops.join(", ")})`,
            stopCount,
            stopRgbs,
        };
    }, [oklch.c, oklch.l]);

    const hueGradient = hueGradientInfo.css;

    const lightnessGradientInfo = useMemo(() => {
        const startRgb = oklchToRgb(0, oklch.c, stableOklchHue);
        const endRgb = oklchToRgb(1, oklch.c, stableOklchHue);
        const start = tinycolor(startRgb).toHexString();
        const end = tinycolor(endRgb).toHexString();
        return {
            css: `linear-gradient(to right, ${start}, ${end})`,
            startRgb,
            endRgb,
        };
    }, [oklch.c, stableOklchHue]);

    const lightnessGradient = lightnessGradientInfo.css;

    const chromaGradientInfo = useMemo(() => {
        const startRgb = oklchToRgb(oklch.l, 0, stableOklchHue);
        const endRgb = oklchToRgb(oklch.l, kMaxChroma, stableOklchHue);
        const start = tinycolor(startRgb).toHexString();
        const end = tinycolor(endRgb).toHexString();
        return {
            css: `linear-gradient(to right, ${start}, ${end})`,
            startRgb,
            endRgb,
        };
    }, [oklch.l, stableOklchHue]);

    const chromaGradient = chromaGradientInfo.css;

    const transparencyString =
        ((1 - props.currentOpacity) * 100).toFixed(0) + "%";

    const hueThumbColor = useMemo(() => {
        const normalizedHue = ((oklch.h % 360) + 360) % 360;
        const segmentSize = 360 / hueGradientInfo.stopCount;
        const startIndex = Math.floor(normalizedHue / segmentSize);
        const endIndex = startIndex + 1;
        const t = (normalizedHue - startIndex * segmentSize) / segmentSize;
        const startRgb = hueGradientInfo.stopRgbs[startIndex];
        const endRgb = hueGradientInfo.stopRgbs[endIndex];
        const rgb = interpolateRgb(startRgb, endRgb, t);
        return tinycolor(rgb).toRgbString();
    }, [hueGradientInfo.stopCount, hueGradientInfo.stopRgbs, oklch.h]);

    const lightnessThumbColor = useMemo(() => {
        const rgb = interpolateRgb(
            lightnessGradientInfo.startRgb,
            lightnessGradientInfo.endRgb,
            oklch.l,
        );
        return tinycolor(rgb).toRgbString();
    }, [lightnessGradientInfo.endRgb, lightnessGradientInfo.startRgb, oklch.l]);

    const chromaThumbColor = useMemo(() => {
        const chromaRatio =
            kMaxChroma === 0 ? 0 : clampNumber(oklch.c / kMaxChroma, 0, 1);
        const rgb = interpolateRgb(
            chromaGradientInfo.startRgb,
            chromaGradientInfo.endRgb,
            chromaRatio,
        );
        return tinycolor(rgb).toRgbString();
    }, [chromaGradientInfo.endRgb, chromaGradientInfo.startRgb, oklch.c]);
    const alphaGradient = `linear-gradient(to right, rgba(${props.color.r}, ${props.color.g}, ${props.color.b}, 0), rgba(${props.color.r}, ${props.color.g}, ${props.color.b}, 1))`;
    const alphaChannel = clampNumber(props.currentOpacity, 0, 1);
    const alphaShade = Math.round(255 * (1 - alphaChannel));
    const alphaThumbColor = tinycolor({
        r: alphaShade,
        g: alphaShade,
        b: alphaShade,
    }).toRgbString();

    // Draw the active wheel for the current color state.
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
                let rgb: RGBColor;
                if (isHsvWheel) {
                    const sValue = saturationRatio * kMaxSaturation;
                    rgb = hsvToRgb(hueDegrees, sValue, wheelHsvValue);
                } else {
                    const hueIndex = Math.floor(hueDegrees) % 360;
                    const chromaLimit =
                        wheelMode === "oklch-gamut"
                            ? (maxChromaByHue?.[hueIndex] ?? kMaxChroma)
                            : kMaxChroma;
                    const cValue = saturationRatio * chromaLimit;
                    rgb = oklchToRgb(wheelOklchLightness, cValue, hueDegrees);
                }
                const offset = (y * pixelDiameter + x) * 4;
                image.data[offset] = rgb.r;
                image.data[offset + 1] = rgb.g;
                image.data[offset + 2] = rgb.b;
                image.data[offset + 3] = 255;
            }
        }

        context.putImageData(image, 0, 0);
    }, [maxChromaByHue, wheelHsvValue, wheelMode, wheelOklchLightness]);

    // Use window capture listeners so the picker responds even if other layers intercept events.
    // Use global drag state because component may re-render during drag.
    useEffect(() => {
        const isPickerDragging = () => globalDragState.activePicker;
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
        document.addEventListener("pointerdown", handlePointerDown, true);
        document.addEventListener("pointerup", handlePointerUp, true);
        // pointermove must be on the element itself when pointer capture is used
        picker?.addEventListener("pointermove", handlePointerMove);
        return () => {
            document.removeEventListener(
                "pointerdown",
                handlePointerDown,
                true,
            );
            document.removeEventListener("pointerup", handlePointerUp, true);
            picker?.removeEventListener("pointermove", handlePointerMove);
        };
    }, []);

    const wheelRadius = kWheelDiameter / 2;
    const wheelChromaLimit = isHsvWheel
        ? 0
        : wheelMode === "oklch-gamut"
          ? maxChroma
          : kMaxChroma;
    const saturationRatio = isHsvWheel
        ? wheelIsOneWay
            ? clampNumber(wheelStateRef.current.radiusRatio, 0, 1)
            : clampNumber(currentHsv.s, 0, 1)
        : clampNumber(
              wheelChromaLimit === 0 ? 0 : oklch.c / wheelChromaLimit,
              0,
              1,
          );
    const hueRadians =
        ((isHsvWheel
            ? wheelIsOneWay
                ? wheelStateRef.current.hue
                : currentHsv.h
            : stableOklchHue) *
            Math.PI) /
        180;
    const wheelCursorVisible =
        !wheelIsOneWay ||
        !isHsvWheel ||
        (getHueDelta(currentHsv.h, wheelStateRef.current.hue) < 0.5 &&
            Math.abs(currentHsv.s - wheelStateRef.current.radiusRatio) < 0.01 &&
            Math.abs(currentHsv.v - wheelStateRef.current.hsvValue) < 0.01);
    const cursorLeft = `${wheelRadius + Math.cos(hueRadians) * wheelRadius * saturationRatio}px`;
    const cursorTop = `${wheelRadius + Math.sin(hueRadians) * wheelRadius * saturationRatio}px`;

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
            <label
                css={css`
                    display: flex;
                    flex-direction: column;
                    gap: 4px;
                    font-size: 12px;
                    font-family: ${kUiFontStack};
                    color: #000;
                `}
            >
                Wheel
                <select
                    value={wheelMode}
                    onChange={handleWheelModeChange}
                    css={css`
                        height: 28px;
                        border-radius: 3px;
                        border: 1px solid rgba(0, 0, 0, 0.35);
                        padding: 2px 6px;
                        font-family: ${kUiFontStack};
                        font-size: 12px;
                    `}
                >
                    {wheelModeOptions.map((option) => (
                        <option key={option.value} value={option.value}>
                            {option.label}
                        </option>
                    ))}
                </select>
            </label>
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
                        opacity: ${wheelCursorVisible ? 1 : 0};
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
                {showHue && (
                    <OklchSlider
                        label="Hue"
                        min={0}
                        max={360}
                        step={1}
                        value={oklch.h}
                        onChange={handleHueChange}
                        background={hueGradient}
                        thumbColor={hueThumbColor}
                        testId="oklch-slider-H"
                    />
                )}
                <OklchSlider
                    label="Lightness"
                    min={0}
                    max={100}
                    step={1}
                    value={Math.round(clampNumber(oklch.l, 0, 1) * 100)}
                    onChange={handleLightnessChange}
                    background={lightnessGradient}
                    thumbColor={lightnessThumbColor}
                    testId="oklch-slider-L"
                />
                <OklchSlider
                    label="Intensity (Chroma)"
                    min={0}
                    max={100}
                    step={1}
                    value={Math.round(
                        clampNumber(
                            kMaxChroma === 0 ? 0 : oklch.c / kMaxChroma,
                            0,
                            1,
                        ) * 100,
                    )}
                    onChange={handleChromaChange}
                    background={chromaGradient}
                    thumbColor={chromaThumbColor}
                    testId="oklch-slider-C"
                />
                {!props.noAlphaSlider && (
                    <OklchSlider
                        label="Transparency"
                        min={0}
                        max={100}
                        step={1}
                        value={Math.round(props.currentOpacity * 100)}
                        onChange={handleOpacityChange}
                        background={`${alphaGradient},
                            linear-gradient(45deg, #bdbdbd 25%, transparent 25%, transparent 75%, #bdbdbd 75%, #bdbdbd),
                            linear-gradient(45deg, #bdbdbd 25%, transparent 25%, transparent 75%, #bdbdbd 75%, #bdbdbd)`}
                        backgroundSize="100% 100%, 12px 12px, 12px 12px"
                        backgroundPosition="0 0, 0 0, 6px 6px"
                        backgroundRepeat="no-repeat, repeat, repeat"
                        title={transparencyString}
                        thumbColor={alphaThumbColor}
                        testId="oklch-slider-A"
                    />
                )}
            </div>
        </div>
    );
};

export default BloomSketchPicker;
