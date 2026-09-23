import type { IBloomComponentConfig } from "../../component-tester/componentTypes";
import type { IProgressBoxProps } from "../progressBox";

const config: IBloomComponentConfig<IProgressBoxProps> = {
    defaultProps: {
        preloadedProgressEvents: [
            {
                id: "message",
                clientContext: "progress-test",
                message: "ProgressBox component test message",
                progressKind: "Progress",
            },
        ],
    },
    modulePath: "../Progress/progressBox",
    exportName: "TestableProgressBox",
};

export default config;
