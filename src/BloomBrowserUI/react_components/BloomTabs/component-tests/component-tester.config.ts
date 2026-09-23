import type { IBloomComponentConfig } from "../../component-tester/componentTypes";

type IBloomTabsHarnessProps = Record<string, never>;

const config: IBloomComponentConfig<IBloomTabsHarnessProps> = {
    defaultProps: {},
    modulePath: "../BloomTabs/component-tests/BloomTabsTestHarness",
    exportName: "BloomTabsTestHarness",
};

export default config;
