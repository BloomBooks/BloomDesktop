import { getWorkspaceBundleExports } from "./workspaceFrames";
import WebSocketManager, {
    IBloomWebSocketEvent,
} from "../../utils/WebSocketManager";
import { post } from "../../utils/bloomApi";

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
