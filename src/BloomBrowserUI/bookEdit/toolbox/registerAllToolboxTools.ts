// The one list of the tools that belong in the Edit tab's toolbox.
//
// ToolboxRoot renders a section only for a tool that is in the master tool list, and a tool gets
// there by being registered. In the running app that used to happen as a side effect of loading
// toolboxBootstrap.ts, which also renders its own toolbox root and assigns window.toolboxBundle.
// A test harness cannot afford those side effects, so it duplicated the list with a "keep in
// sync" comment. This module is the list, and nothing else: importing it registers nothing, and
// both toolboxBootstrap.ts and react_components/ToolboxRootTestHarness call the function below.
// (AUTOMATION-DEBT.md: "Toolbox tool registration is a side effect of toolboxBootstrap".)

import { ITool, ToolBox, getMasterToolList } from "./toolbox";
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
 * Calling this twice registers nothing the second time. See registerOnce for why the check is
 * per tool and reads the shared master list.
 */
export function registerAllToolboxTools(): void {
    registerOnce(new DecodableReaderTool());
    registerOnce(new LeveledReaderTool());
    registerOnce(new MusicToolAdaptor());
    registerOnce(new ImpairmentVisualizerAdaptor());
    registerOnce(new MotionTool());
    registerOnce(new TalkingBookTool());
    registerOnce(new SignLanguageTool());
    registerOnce(new ImageDescriptionAdapter());
    registerOnce(new CanvasTool());
    registerOnce(new GameTool());
    registerOnce(new SettingsTool());
}

/**
 * Register one tool, unless the master list already has a tool of that id.
 *
 * ToolBox.registerTool is a bare push with no check for duplicates, so something has to do the
 * check. It reads the shared master list rather than a flag in this module because a caller that
 * is a React-Refresh boundary re-executes its own module during `pnpm dev` while masterToolList,
 * which lives in toolbox.ts, keeps its entries. A flag here would reset and we would get eleven
 * duplicate tools and duplicate accordion sections.
 *
 * The check is per tool, not "is the list empty": a list holding some other tool is not evidence
 * that these eleven are registered, and skipping all of them on that evidence would leave the
 * toolbox missing every section.
 */
function registerOnce(tool: ITool): void {
    if (getMasterToolList().some((registered) => registered.id() === tool.id()))
        return;
    ToolBox.registerTool(tool);
}
