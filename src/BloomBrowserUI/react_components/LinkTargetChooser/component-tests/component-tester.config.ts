import type { IBloomComponentConfig } from "../../component-tester/componentTypes";
import type { LinkTargetInfo } from "../LinkTargetChooser";

const config: IBloomComponentConfig<{
    currentURL: string;
    onSetUrl?: (info: LinkTargetInfo) => void;
}> = {
    defaultProps: {
        currentURL: "",
        onSetUrl: undefined,
    },
    modulePath: "../LinkTargetChooser/LinkTargetChooserDialog",
    exportName: "LinkTargetChooserDialog",
};

export default config;
