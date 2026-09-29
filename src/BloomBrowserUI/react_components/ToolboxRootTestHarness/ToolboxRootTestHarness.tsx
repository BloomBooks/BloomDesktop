import * as React from "react";
import { ToolboxRoot } from "../../bookEdit/toolbox/ToolboxRoot";
import { registerAllToolboxTools } from "../../bookEdit/toolbox/registerAllToolboxTools";

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

// ToolboxRoot only renders a section for a tool that is in the master tool list, and tools
// put themselves there by being registered. In the running app that happens when
// toolboxBootstrap.ts calls registerAllToolboxTools. We deliberately do NOT import
// toolboxBootstrap here: besides registering tools it also renders its own toolbox root on
// $(document).ready and assigns window.toolboxBundle, which would both duplicate the root this
// harness renders and overwrite the toolboxBundle stub some tests install. So we call the shared
// registration function, which is side-effect-free to import and safe to call twice.
registerAllToolboxTools();

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
