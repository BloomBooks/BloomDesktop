import * as React from "react";
import { ToolboxRoot } from "../../bookEdit/toolbox/ToolboxRoot";
import { registerAllToolboxTools } from "../../bookEdit/toolbox/registerAllToolboxTools";

// ToolboxRoot only renders a section for a tool that is in the master tool list, and tools
// put themselves there by being registered. In the running app that happens when
// toolboxBootstrap.ts calls registerAllToolboxTools. We deliberately do NOT import
// toolboxBootstrap here: besides registering tools it also renders its own toolbox root on
// $(document).ready and assigns window.toolboxBundle, which would both duplicate the root this
// harness renders and overwrite the toolboxBundle stub some tests install. So we call the shared
// registration function, which is side-effect-free to import and safe to call twice.
registerAllToolboxTools();

export const ToolboxRootTestHarness: React.FunctionComponent = () => {
    return <ToolboxRoot />;
};
