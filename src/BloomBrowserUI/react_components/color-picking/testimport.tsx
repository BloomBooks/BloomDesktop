import { css } from "@emotion/react";
import * as React from "react";
import { Checkboard } from "react-color/lib/components/common";
import tinycolor from "tinycolor2";

export function add(a: number, b: number): number {
    return a + b;
}

export interface IColorInfo {
    // Usually Hex colors
    // We use an array here, so we can support gradients (top to bottom).
    colors: string[];
    name?: string;
    opacity: number;
}

export function shortName(input: string[]): void {
    const fake = `#${tinycolor(undefined).toHex()}`;
    input.push("fake");
}
