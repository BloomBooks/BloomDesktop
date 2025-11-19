import type { IBloomComponentConfig } from "../../component-tester/componentTypes";
import type { LinkTargetInfo } from "../LinkTargetChooser";

const config: IBloomComponentConfig<{
    open: boolean;
    currentURL: string;
    onCancel?: () => void;
    onSelect?: (info: LinkTargetInfo) => void;
}> = {
    defaultProps: {
        open: true,
        currentURL: "",
        onCancel: undefined,
        onSelect: undefined,
    },
    modulePath: "../LinkTargetChooser/LinkTargetChooserDialog",
    exportName: "LinkTargetChooserDialog",
};

export default config;
