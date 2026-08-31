import { getWorkspaceBundleExports } from "./workspaceFrames";
import WebSocketManager, {
    IBloomWebSocketEvent,
} from "../../utils/WebSocketManager";
import { post } from "../../utils/bloomApi";

/// Opens a file-chooser dialog for a video file, then (if the user selects one)
/// copies and re-encodes the video into the book folder, showing a progress dialog.
/// Calls onImported with the book-relative path on success, or undefined on cancel/failure.
export function chooseAndProcessVideo(
    onImported: (importedPath?: string) => void,
) {
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
            onImported,
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
