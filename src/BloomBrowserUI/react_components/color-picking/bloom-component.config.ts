import type { IBloomComponentConfig } from "../component-tester/componentTypes";
import type { IClickableColorSwatch } from "./colorSwatch";

const config: IBloomComponentConfig<IClickableColorSwatch> = {
    defaultProps: {
        colors: ["#ff0000", "#00ff00"],
        opacity: 1,
        width: 100,
        height: 100,
    },
    modulePath: "../color-picking/colorSwatch",
    exportName: "ColorSwatch",
};

export default config;
