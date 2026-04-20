import * as React from "react";
import { ProgressDialog } from "../react_components/Progress/ProgressDialog";
import { post } from "../utils/bloomApi";
import { useL10n } from "../react_components/l10nHooks";

const ImportVideoProgressDialogLauncher: React.FunctionComponent<{
    onImported: (importedPath?: string) => void;
}> = (props) => {
    const [isOpen, setIsOpen] = React.useState(true);
    const importStarted = React.useRef(false);

    const finishImport = (importedPath?: string) => {
        setIsOpen(false);
        props.onImported(importedPath);
    };
    const dialogTitle = useL10n(
        "Importing Video",
        "EditTab.Toolbox.SignLanguage.ImportingVideo",
    );

    return (
        <ProgressDialog
            title={dialogTitle}
            determinate={true}
            linearProgress={true}
            size="small"
            showCancelButton={true}
            onCancel={() => {
                post("signLanguage/cancelImportVideo");
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
