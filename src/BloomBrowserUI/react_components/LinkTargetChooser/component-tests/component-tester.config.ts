import type { IBloomComponentConfig } from "../../component-tester/componentTypes";
import type { LinkTargetInfo } from "../LinkTargetChooser";

const config: IBloomComponentConfig<{
    open: boolean;
    currentURL: string;
    onClose?: () => void;
    onSelect?: (info: LinkTargetInfo) => void;
}> = {
    defaultProps: {
        open: true,
        currentURL: "",
        onClose: undefined,
        onSelect: undefined,
    },
    modulePath: "../LinkTargetChooser/LinkTargetChooserDialog",
    exportName: "LinkTargetChooserDialog",
};

export default config;
