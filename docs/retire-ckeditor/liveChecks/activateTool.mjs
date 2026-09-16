// Activate a toolbox tool by id in the running Bloom, e.g. `node activateTool.mjs talkingBook`.
// Uses the toolbox bundle's own ToolBox.activateToolFromId, so it does exactly what a click on the
// accordion header does (including newPageReady for the new tool).
import { connect, mainPage } from "./cdp.mjs";
const toolId = process.argv[2];
if (!toolId)
    throw new Error(
        "usage: node activateTool.mjs <toolId>  (e.g. talkingBook, decodableReader)",
    );
const { browser, pages } = await connect();
const p = mainPage(pages);
const tb = p.frames().find((f) => f.name() === "toolbox");
const result = await tb.evaluate((id) => {
    const toolbox = window.toolboxBundle.getTheOneToolbox();
    toolbox.activateToolFromId(id);
    return toolbox.getCurrentTool()?.id();
}, toolId);
await p.waitForTimeout(1500);
const active = await tb.evaluate(() =>
    window.toolboxBundle.getTheOneToolbox().getCurrentTool()?.id(),
);
console.log(
    "requested:",
    toolId,
    "current tool now:",
    active,
    "(immediately after call:",
    result + ")",
);
await browser.close();
