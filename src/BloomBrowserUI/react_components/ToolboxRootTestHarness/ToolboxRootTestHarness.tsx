import * as React from "react";
import { ToolboxRoot } from "../../bookEdit/toolbox/ToolboxRoot";
import {
    getToolboxReactAdapter,
    IToolboxReactAdapter,
    whenToolboxReactAdapterReady,
} from "../../bookEdit/toolbox/toolboxReactAdapter";
import { ToolBox } from "../../bookEdit/toolbox/toolbox";
import ToolboxToolReactAdaptor from "../../bookEdit/toolbox/toolboxToolReactAdaptor";
import { ImpairmentVisualizerAdaptor } from "../../bookEdit/toolbox/impairmentVisualizer/impairmentVisualizer";
import { SettingsTool } from "../../bookEdit/toolbox/settings/settingsTool";
import { kCanvasToolId, kMotionToolId } from "../../bookEdit/toolbox/toolIds";
import { useMountEffect } from "../../utils/useMountEffect";

// A stand-alone host for ToolboxRoot, so that Playwright can drive the real toolbox UI
// (see component-tests/toolbox-root-react.uitest.ts).
//
// ToolboxRoot does not decide which tools to offer; in the real toolbox that is
// toolbox.ts's job (it asks the server which tools the book has enabled, then calls the
// adapter's addTool() for each, and finally makes the tool the book was last using the
// current one). This harness stands in for exactly that: it registers a small set of tools
// with the real ToolBox.registerTool(), then populates the toolbox through the real
// adapter. Everything under test — the sections, their order, their headers, and which one
// is expanded — is therefore production code driven the production way.

declare global {
    interface Window {
        // Test-only hook. toolbox.ts gets the adapter by importing
        // getToolboxReactAdapter(), but our Playwright tests run inside the page, where
        // they can't import a module, so this harness hands them the accessor. It is the
        // accessor rather than the adapter itself because ToolboxRoot doesn't register an
        // adapter until it has mounted.
        getToolboxReactAdapterForTests?: () => IToolboxReactAdapter | undefined;
    }
}

/**
 * Stands in for one of the real subscription-requiring tools (Canvas, Motion). Their own
 * implementations would drag the canvas and audio-recording engines into this harness and
 * expect a page to be open for editing, but everything the toolbox itself asks of a tool is
 * cheap to supply. What the toolbox uses is real: the id is a real tool id (so the header
 * label, its l10n key, and the sort order all come from toolIds.ts), and the icon path and
 * feature name are the ones the real tool returns.
 */
class StandInTool extends ToolboxToolReactAdaptor {
    public constructor(
        private toolId: string,
        private toolIconPath: string,
        featureName: string,
    ) {
        super();
        this.featureName = featureName;
    }

    public id(): string {
        return this.toolId;
    }

    public iconPath(): string {
        return this.toolIconPath;
    }

    public renderPanel(): JSX.Element {
        return <div>{`Stand-in body for the ${this.toolId} tool.`}</div>;
    }
}

// The tools this harness offers. The two real ones are the cheapest real tools to host
// outside the real toolbox: neither needs CkEditor, the audio engine, or a page being
// edited in order to render its section. Impairment Visualizer also exercises the one tool
// id whose label and l10n key take no "Tool" suffix, and the Settings ("More...") tool is
// the section that always sorts last.
const impairmentVisualizerTool = new ImpairmentVisualizerAdaptor();
const settingsTool = new SettingsTool();
const motionTool = new StandInTool(
    kMotionToolId,
    "/bloom/bookEdit/toolbox/motion/motion.svg",
    kMotionToolId,
);
const canvasTool = new StandInTool(
    kCanvasToolId,
    "/bloom/bookEdit/toolbox/canvas/Canvas%20Icon.svg",
    kCanvasToolId,
);

// Registering at module scope, as toolboxBootstrap.ts does, so the tools are known before
// anything asks the toolbox to offer one.
[impairmentVisualizerTool, settingsTool, motionTool, canvasTool].forEach(
    (tool) => ToolBox.registerTool(tool),
);

// The sections the toolbox starts with, i.e. what toolbox.ts would add after asking the
// server which tools this book has enabled. Canvas is deliberately left out so that a test
// can add it later, the way ticking its checkbox in the "More..." section does.
const initiallyOfferedToolIds = [
    impairmentVisualizerTool.id(),
    motionTool.id(),
    settingsTool.id(),
];

// The tool this "book" was last using, which toolbox.ts makes current once the sections
// exist. Deliberately not the first section, so that a test can tell that it was restored
// rather than just defaulted to.
const restoredCurrentToolId = motionTool.id();

export const ToolboxRootTestHarness: React.FunctionComponent = () => {
    // Publishing the test hook and populating the toolbox are side effects that have
    // nothing to do with rendering, and they only need to happen once, so a mount effect is
    // the right home for them.
    useMountEffect(() => {
        window.getToolboxReactAdapterForTests = getToolboxReactAdapter;

        // Stand in for ToolBox.initialize(). Like the real thing, wait for ToolboxRoot to
        // publish its adapter rather than assuming it has already mounted.
        whenToolboxReactAdapterReady((adapter) => {
            initiallyOfferedToolIds.forEach((toolId) =>
                adapter.addTool(toolId),
            );
            adapter.setActiveToolByToolId(restoredCurrentToolId);
        });

        return () => {
            window.getToolboxReactAdapterForTests = undefined;
        };
    });

    return <ToolboxRoot />;
};
