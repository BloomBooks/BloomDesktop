// taken out of audioRecording.ts to avoid the need for other
// files to import that big file just to use a little bit of
// code.

import { getToolboxBundleExports } from "../../js/workspaceFrames";

const kTalkingBookToolId = "talkingBook";

export function showTalkingBookTool() {
    getToolboxBundleExports()
        ?.getTheOneToolbox()
        .activateToolFromId(kTalkingBookToolId);
}
