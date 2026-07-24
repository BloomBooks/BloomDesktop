import * as React from "react";
import { ToolboxRoot } from "../../bookEdit/toolbox/ToolboxRoot";
import {
    getToolboxReactAdapter,
    IToolboxReactAdapter,
} from "../../bookEdit/toolbox/toolboxReactAdapter";
import { useMountEffect } from "../../utils/useMountEffect";

declare global {
    interface Window {
        // Test-only hook. The legacy toolbox code gets the adapter by importing
        // getToolboxReactAdapter(), but our Playwright tests run inside the page, where
        // they can't import a module, so this harness hands them the accessor. It is the
        // accessor rather than the adapter itself because ToolboxRoot doesn't register an
        // adapter until it has mounted.
        getToolboxReactAdapterForTests?: () => IToolboxReactAdapter | undefined;
    }
}

export const ToolboxRootTestHarness: React.FunctionComponent = () => {
    // Publishing the test hook is a side effect that has nothing to do with rendering,
    // and it only needs to happen once, so a mount effect is the right home for it.
    useMountEffect(() => {
        window.getToolboxReactAdapterForTests = getToolboxReactAdapter;
        return () => {
            window.getToolboxReactAdapterForTests = undefined;
        };
    });

    return <ToolboxRoot />;
};
