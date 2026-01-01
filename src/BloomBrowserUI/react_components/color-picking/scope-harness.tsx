// This file is used by `yarn scope` to open the component in a browser. It does so in a way that works with skills/scope/skill.md.

import * as React from "react";
import { ColorPicker } from "./colorPicker";
import { IColorInfo } from "./colorSwatch";

export const white: React.FC = () => {
    return (
        <ColorPicker
            currentColor={{ colors: ["#FFFFFF"], opacity: 1 }}
            swatchColors={swatches}
            onChange={() => {}}
        />
    );
};

export const red: React.FC = () => {
    return (
        <ColorPicker
            currentColor={{ colors: ["#FF0000"], opacity: 1 }}
            swatchColors={swatches}
            onChange={() => {}}
        />
    );
};

const swatches: IColorInfo[] = [
    { colors: ["#E48C84"], opacity: 1 },
    { colors: ["#B58B4F"], opacity: 1 },
    { colors: ["#7E5A3C"], opacity: 1 },
    { colors: ["#F0E5D8"], opacity: 1 },
    { colors: ["#D9A6A0"], opacity: 1 },
    { colors: ["#8C6A5A"], opacity: 1 },
    { colors: ["#6D7A7B"], opacity: 1 },
    { colors: ["#F0D36E"], opacity: 1 },
    { colors: ["#85B2C2"], opacity: 1 },
];

export default white;
