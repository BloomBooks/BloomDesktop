import * as React from "react";
import { ToolboxRoot } from "../../bookEdit/toolbox/ToolboxRoot";
import { ToolBox } from "../../bookEdit/toolbox/toolbox";
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
let toolsRegistered = false;
function registerToolsOnce() {
    if (toolsRegistered) return;
    toolsRegistered = true;
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
