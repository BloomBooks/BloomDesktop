// The one list of the tools that belong in the Edit tab's toolbox.
//
// ToolboxRoot renders a section only for a tool that is in the master tool list, and a tool gets
// there by being registered. In the running app that used to happen as a side effect of loading
// toolboxBootstrap.ts, which also renders its own toolbox root and assigns window.toolboxBundle.
// A test harness cannot afford those side effects, so it duplicated the list with a "keep in
// sync" comment. This module is the list, and nothing else: importing it registers nothing, and
// both toolboxBootstrap.ts and react_components/ToolboxRootTestHarness call the function below.
// (AUTOMATION-DEBT.md: "Toolbox tool registration is a side effect of toolboxBootstrap".)

import { ToolBox, getMasterToolList } from "./toolbox";
import { DecodableReaderTool } from "./readers/decodableReader/decodableReaderTool";
import { LeveledReaderTool } from "./readers/leveledReader/leveledReaderTool";
import { MusicToolAdaptor } from "./music/musicToolControls";
import { ImpairmentVisualizerAdaptor } from "./impairmentVisualizer/impairmentVisualizer";
import { MotionTool } from "./motion/motionTool";
import TalkingBookTool from "./talkingBook/talkingBookTool";
import { SignLanguageTool } from "./signLanguage/signLanguageTool";
import { ImageDescriptionAdapter } from "./imageDescription/imageDescription";
import { CanvasTool } from "./canvas/canvasTool";
import { GameTool } from "./games/GameTool";
import { SettingsTool } from "./settings/settingsTool";

/**
 * Make the one instance of each toolbox class and register it with the master toolbox. The
 * imports above also serve to ensure that each tool's code is part of the bundle.
 *
 * Calling this twice registers nothing the second time. ToolBox.registerTool is a bare push with
 * no check for duplicates, and the guard keys off the shared master list rather than a flag in
 * this module: a caller that is a React-Refresh boundary re-executes its own module during
 * `pnpm dev` while masterToolList, which lives in toolbox.ts, keeps its entries. A flag here
 * would reset and we would get eleven duplicate tools and duplicate accordion sections.
 */
export function registerAllToolboxTools(): void {
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
