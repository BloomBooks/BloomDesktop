import { getWorkspaceBundleExports } from "./workspaceFrames";
import WebSocketManager, {
    IBloomWebSocketEvent,
} from "../../utils/WebSocketManager";
import { post } from "../../utils/bloomApi";
import { currentPageHistory } from "../pageHistory/pageHistoryAccess";

/// Opens a file-chooser dialog for a video file, then (if the user selects one)
/// copies and re-encodes the video into the book folder, showing a progress dialog.
/// Calls onImported with the book-relative path on success, or undefined on cancel/failure.
/// What onImported changes on the page for an imported video is one undo step, which
/// descriptionL10nKey names.
export function chooseAndProcessVideo(
    onImported: (importedPath?: string) => void,
    descriptionL10nKey = "EditTab.Toolbox.SignLanguage.ImportVideo",
) {
    const applyImport = (importedPath?: string) => {
        if (!importedPath) {
            onImported();
            return;
        }
        // C# copies the video under a new name and deletes no file that a page names, so undo
        // can put back the video that was there before.
        const transaction =
            currentPageHistory()?.beginTransaction(descriptionL10nKey);
        try {
            onImported(importedPath);
        } catch (error) {
            transaction?.cancel();
            throw error;
        }
        transaction?.end();
    };

    const chooseVideoListener = (
        e: IBloomWebSocketEvent & { success?: boolean; path?: string },
    ) => {
        if (e.id !== "chooseVideo-results") {
            return;
        }

        WebSocketManager.removeListener("signLanguage", chooseVideoListener);
        if (!e.success || !e.path) {
            onImported();
            return;
        }

        getWorkspaceBundleExports().processVideoAndShowProgressDialog(
            e.path,
            applyImport,
        );
    };

    WebSocketManager.addListener("signLanguage", chooseVideoListener);
    WebSocketManager.notifyReady("signLanguage", () => {
        post("signLanguage/chooseVideo", undefined, () => {
            WebSocketManager.removeListener(
                "signLanguage",
                chooseVideoListener,
            );
            onImported();
        });
    });
}
