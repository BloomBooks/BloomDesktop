import type { IBloomComponentConfig } from "../../component-tester/componentTypes";

type IToolboxRootTestHarnessProps = Record<string, never>;

const config: IBloomComponentConfig<IToolboxRootTestHarnessProps> = {
    defaultProps: {},
    modulePath: "../ToolboxRootTestHarness/ToolboxRootTestHarness",
    exportName: "ToolboxRootTestHarness",
};

export default config;
