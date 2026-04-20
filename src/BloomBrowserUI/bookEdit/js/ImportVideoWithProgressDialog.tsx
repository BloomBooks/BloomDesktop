import { getWorkspaceBundleExports } from "./workspaceFrames";

export function importVideoWithProgressDialog(
    onImported: (importedPath?: string) => void,
) {
    getWorkspaceBundleExports().importVideoAndShowProgressDialog(onImported);
}
