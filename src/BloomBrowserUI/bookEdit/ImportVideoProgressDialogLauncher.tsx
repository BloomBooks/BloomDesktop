import * as React from "react";
import { ProgressDialog } from "../react_components/Progress/ProgressDialog";
import { post } from "../utils/bloomApi";

const ImportVideoProgressDialogLauncher: React.FunctionComponent<{
    onImported: (importedPath?: string) => void;
}> = (props) => {
    const [isOpen, setIsOpen] = React.useState(true);
    const importStarted = React.useRef(false);

    const finishImport = (importedPath?: string) => {
        setIsOpen(false);
        props.onImported(importedPath);
    };

    return (
        <ProgressDialog
            title="Importing Video"
            determinate={true}
            linearProgress={true}
            size="small"
            showCancelButton={true}
            onCancel={() => {
                post("signLanguage/cancelImportVideo", () => {
                    finishImport();
                });
            }}
            open={isOpen}
            onClose={() => finishImport()}
            onReadyToReceive={() => {
                if (importStarted.current) {
                    return;
                }

                importStarted.current = true;
                post(
                    "signLanguage/importVideo",
                    (result) => {
                        finishImport(result.data);
                    },
                    () => {
                        finishImport();
                    },
                );
            }}
        />
    );
};

export function importVideoAndShowProgressDialog(
    onImported: (importedPath?: string) => void,
    showEditViewDialog: (
        dialog: React.FunctionComponentElement<unknown>,
    ) => void,
) {
    showEditViewDialog(
        <ImportVideoProgressDialogLauncher onImported={onImported} />,
    );
}
