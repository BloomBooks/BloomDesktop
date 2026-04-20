import * as React from "react";
import { ProgressDialog } from "../react_components/Progress/ProgressDialog";
import { post, postDataWithConfig } from "../utils/bloomApi";
import { useL10n } from "../react_components/l10nHooks";
import { ShowEditViewDialog } from "./workspaceRoot";
import WebSocketManager from "../utils/WebSocketManager";

const ProcessVideoProgressDialog: React.FunctionComponent<{
    sourcePath: string;
    onImported: (importedPath?: string) => void;
}> = (props) => {
    const [isOpen, setIsOpen] = React.useState(true);
    const importStarted = React.useRef(false);
    const importFinished = React.useRef(false);

    const finishImport = (importedPath?: string) => {
        if (importFinished.current) {
            return;
        }

        importFinished.current = true;
        setIsOpen(false);
        props.onImported(importedPath);
    };
    const dialogTitle = useL10n(
        "Processing",
        "EditTab.Toolbox.SignLanguage.Processing",
    );

    return (
        <ProgressDialog
            title={dialogTitle}
            determinate={true}
            linearProgress={true}
            noMessages={true}
            showCancelButton={true}
            onCancel={() => {
                // This cancels choosing the video as well as processing it, so the API name
                // is appropriate for covering both effects.
                post("signLanguage/cancelImportVideo");
            }}
            open={isOpen}
            onClose={() => finishImport()}
            onReadyToReceive={() => {
                if (importStarted.current) {
                    return;
                }

                importStarted.current = true;
                const processingListener = (e: {
                    id: string;
                    success?: boolean;
                    importedPath?: string;
                }) => {
                    if (e.id !== "processVideo-results") {
                        return;
                    }

                    WebSocketManager.removeListener(
                        "signLanguage",
                        processingListener,
                    );
                    if (!e.success || !e.importedPath) {
                        finishImport();
                        return;
                    }

                    finishImport(e.importedPath);
                };

                WebSocketManager.addListener(
                    "signLanguage",
                    processingListener,
                );
                postDataWithConfig(
                    "signLanguage/processVideo",
                    "",
                    { params: { sourcePath: props.sourcePath } },
                    undefined,
                    () => {
                        WebSocketManager.removeListener(
                            "signLanguage",
                            processingListener,
                        );
                        finishImport();
                    },
                );
            }}
        />
    );
};

// This function is passed along by the workspace root to launch the dialog
// in the root document rather than in the toolbox iframe.
export function processVideoAndShowProgressDialog(
    sourcePath: string,
    onImported: (importedPath?: string) => void,
) {
    ShowEditViewDialog(
        <ProcessVideoProgressDialog
            sourcePath={sourcePath}
            onImported={onImported}
        />,
    );
}
