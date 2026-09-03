import * as React from "react";
import { ToolboxRoot } from "../../bookEdit/toolbox/ToolboxRoot";
import {
    offerTool,
    setActiveTool,
    withdrawTool,
} from "../../bookEdit/toolbox/toolboxState";
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
import { kMotionToolId, kSettingsToolId } from "../../bookEdit/toolbox/toolIds";
import { useMountEffect } from "../../utils/useMountEffect";

// A stand-alone host for ToolboxRoot, so that Playwright can drive the real toolbox UI
// (see component-tests/toolbox-root-react.uitest.ts).
//
// ToolboxRoot does not decide which tools to offer; in the real toolbox that is
// toolbox.ts's job (it asks the server which tools the book has enabled, records each in
// the toolbox state store, and finally makes the tool the book was last using the current
// one). This harness stands in for exactly that, driving the real store, so everything
// under test — the sections, their order, their headers, which one is expanded, and each
// tool's own panel — is production code driven the production way.

// The part of the toolbox state store that the Playwright tests need to drive.
interface IToolboxStoreForTests {
    offerTool(toolId: string): void;
    withdrawTool(toolId: string): void;
    setActiveTool(toolId: string): void;
}

declare global {
    interface Window {
        // Test-only hook. toolbox.ts drives the store by importing toolboxState.ts, but
        // our Playwright tests run inside the page, where they can't import a module, so
        // this harness hands them the mutators they need.
        toolboxStoreForTests?: IToolboxStoreForTests;
    }
}

// ToolboxRoot only builds a section for a tool that is in the master tool list, and tools
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

// The sections the toolbox starts with, i.e. what toolbox.ts would add after asking the
// server which tools this book has enabled. Canvas is deliberately left out so that a test
// can add it later, the way ticking its checkbox in the "More..." section does.
// Impairment Visualizer earns its place by being the one tool id whose label and l10n key
// take no "Tool" suffix, and Settings ("More...") is the section that always sorts last.
// (toolIds.ts has no constant for the impairment visualizer, since no production code
// needs to name it.)
const initiallyOfferedToolIds = [
    "impairmentVisualizer",
    kMotionToolId,
    kSettingsToolId,
];

// The tool this "book" was last using, which toolbox.ts makes current once the sections
// exist. Deliberately not the first section, so that a test can tell that it was restored
// rather than just defaulted to.
const restoredCurrentToolId = kMotionToolId;

export const ToolboxRootTestHarness: React.FunctionComponent = () => {
    // Publishing the test hook and populating the toolbox are side effects that have
    // nothing to do with rendering, and they only need to happen once, so a mount effect is
    // the right home for them.
    useMountEffect(() => {
        window.toolboxStoreForTests = {
            offerTool,
            withdrawTool,
            setActiveTool,
        };

        // Stand in for ToolBox.initialize(). Like the real thing, just tell the store;
        // the store exists from the moment its module loads, so there is nothing to wait
        // for.
        initiallyOfferedToolIds.forEach((toolId) => offerTool(toolId));
        setActiveTool(restoredCurrentToolId);

        return () => {
            window.toolboxStoreForTests = undefined;
        };
    });

    return <ToolboxRoot />;
};
