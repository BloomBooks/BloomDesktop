import * as React from "react";
import { ToolboxRoot } from "../../bookEdit/toolbox/ToolboxRoot";
import { ToolBox, getMasterToolList } from "../../bookEdit/toolbox/toolbox";
import { DecodableReaderTool } from "../../bookEdit/toolbox/readers/decodableReader/decodableReaderTool";
import { LeveledReaderTool } from "../../bookEdit/toolbox/readers/leveledReader/leveledReaderTool";
import { MusicToolAdaptor } from "../../bookEdit/toolbox/music/musicToolControls";
import { ImpairmentVisualizerAdaptor } from "../../bookEdit/toolbox/impairmentVisualizer/impairmentVisualizer";
import { MotionTool } from "../../bookEdit/toolbox/motion/motionTool";
import TalkingBookTool from "../../bookEdit/toolbox/talkingBook/talkingBookTool";
import { SignLanguageTool } from "../../bookEdit/toolbox/signLanguage/signLanguageTool";
import { ImageDescriptionAdapter } from "../../bookEdit/toolbox/imageDescription/imageDescription";
import { CanvasTool } from "../../bookEdit/toolbox/canvas/canvasTool";
import { GameTool } from "../../bookEdit/toolbox/games/GameTool";
import { SettingsTool } from "../../bookEdit/toolbox/settings/settingsTool";

// ToolboxRoot only renders a section for a tool that is in the master tool list, and tools
// put themselves there by being registered. In the running app that happens as a side effect
// of loading toolboxBootstrap. We deliberately do NOT import that module here: besides
// registering tools it also renders its own toolbox root on $(document).ready and assigns
// window.toolboxBundle, which would both duplicate the root this harness renders and
// overwrite the toolboxBundle stub some tests install. So we register the same set of tools
// ourselves. Keep this list in sync with toolboxBootstrap.ts.
// The guard keys off the shared master list rather than a module-local flag on purpose.
// This file is a valid React-Refresh boundary (its only export is a component), so editing
// it during `pnpm dev` re-executes this module without reloading the page. A module-local
// flag would reset to false while masterToolList — which lives in toolbox.ts and is not
// invalidated — kept its entries, and ToolBox.registerTool is a bare push with no dedupe,
// so we would end up with 11 duplicate tools and duplicate accordion sections.
function registerToolsOnce() {
    if (getMasterToolList().length > 0) return;
    ToolBox.registerTool(new DecodableReaderTool());
    ToolBox.registerTool(new LeveledReaderTool());
    ToolBox.registerTool(new MusicToolAdaptor());
    ToolBox.registerTool(new ImpairmentVisualizerAdaptor());
    ToolBox.registerTool(new MotionTool());
    ToolBox.registerTool(new TalkingBookTool());
    ToolBox.registerTool(new SignLanguageTool());
    ToolBox.registerTool(new ImageDescriptionAdapter());
    ToolBox.registerTool(new CanvasTool());
    ToolBox.registerTool(new GameTool());
    ToolBox.registerTool(new SettingsTool());
}

registerToolsOnce();

export const ToolboxRootTestHarness: React.FunctionComponent = () => {
    return <ToolboxRoot />;
};
