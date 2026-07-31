import { css } from "@emotion/react";
import { ThemeProvider } from "@mui/material/styles";
import { useRef, useState } from "react";
import * as React from "react";
import {
    getToolboxBundleExports,
    getWorkspaceBundleExports,
} from "../../../js/workspaceFrames";
import { get, postBoolean } from "../../../../utils/bloomApi";
import {
    BloomDialog,
    DialogBottomLeftButtons,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
} from "../../../../react_components/BloomDialog/BloomDialog";
import {
    DialogCancelButton,
    DialogHelpButton,
    DialogOkButton,
} from "../../../../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../../../../react_components/l10nHooks";
import type { ReaderSettings } from "../ReaderSettings";
import { ReaderStage } from "../ReaderSettings";
import { getTheOneReaderToolsModel } from "../readerToolsModel";
import { DecodableStagesSetup } from "./DecodableStagesSetup";
import {
    cloneReaderSettings,
    prepareSettingsForSave,
} from "./decodableStagesUtils";
import { kBloomBlue } from "../../../../utils/colorUtils";
import { lightTheme } from "../../../../bloomMaterialUITheme";

let closeDialog: () => void = () => {};

const DecodableReaderSetupDialogLauncher: React.FunctionComponent<{
    editingForbiddenMessage?: string;
}> = (props) => {
    const model = getTheOneReaderToolsModel();

    const [open, setOpen] = useState(true);
    const [settings, setSettings] = useState<ReaderSettings>(() => {
        const source = model.synphony?.source;
        if (!source) {
            throw new Error(
                "The reader settings must load before opening setup.",
            );
        }

        const copy = cloneReaderSettings(source);
        if (copy.stages.length === 0) {
            copy.stages.push(new ReaderStage("1"));
        }
        return copy;
    });
    const initialSettings = useRef(settings);

    const close = () => {
        setOpen(false);
        closeDialog = () => {};
        postBoolean("editView/setModalState", false);
    };

    closeDialog = close;

    const save = () => {
        const toolboxBundle = getToolboxBundleExports();
        if (!toolboxBundle) {
            throw new Error(
                "The reader settings must load before they can be saved.",
            );
        }
        const settingsToSave = prepareSettingsForSave(settings);
        void toolboxBundle
            .beginSaveChangedSettings(
                settingsToSave,
                initialSettings.current.moreWords,
                initialSettings.current.letters,
                initialSettings.current.useAllowedWords,
            )
            .then(close);
    };

    const title = useL10n(
        "Set up Decodable Reader Tool",
        "ReaderSetup.SetUpDecodableReaderTool",
    );

    return (
        <ThemeProvider theme={lightTheme}>
            <BloomDialog
                open={open}
                onClose={close}
                onCancel={close}
                maxWidth={false}
                css={css`
                    .MuiPaper-root {
                        border-radius: 8px;
                        box-shadow: 0 12px 32px rgb(0 0 0 / 24%);
                    }
                    .MuiButton-containedPrimary {
                        background-color: ${kBloomBlue};
                    }
                    .MuiButton-outlinedPrimary {
                        border-color: ${kBloomBlue};
                        color: ${kBloomBlue};
                    }
                `}
            >
                <DialogTitle title={title} />
                <DialogMiddle
                    css={css`
                        height: min(600px, calc(100vh - 190px));
                        width: min(1000px, calc(100vw - 64px));
                        margin-left: -24px;
                        margin-right: -24px;
                        padding: 20px 24px;
                        box-sizing: border-box;
                        background-color: #f4f5f5;
                        border-top: 1px solid #e5e5e5;
                    `}
                >
                    {props.editingForbiddenMessage ? (
                        <div>{props.editingForbiddenMessage}</div>
                    ) : (
                        <DecodableStagesSetup
                            settings={settings}
                            setSettings={setSettings}
                            fontName={model.fontName}
                            maxAllowedWords={model.maxAllowedWords}
                        />
                    )}
                </DialogMiddle>
                <DialogBottomButtons
                    css={css`
                        margin-left: -24px;
                        margin-right: -24px;
                        margin-bottom: -10px;
                        padding: 14px 24px;
                        width: calc(100% + 48px);
                        box-sizing: border-box;
                        background-color: white;
                        border-top: 1px solid #e5e5e5;
                    `}
                >
                    {!props.editingForbiddenMessage && (
                        <DialogBottomLeftButtons>
                            <DialogHelpButton helpId="Tasks/Edit_tasks/Decodable_Reader_Tool/Decodable_Stages_tab.htm" />
                        </DialogBottomLeftButtons>
                    )}
                    {!props.editingForbiddenMessage && (
                        <DialogOkButton default={true} onClick={save} />
                    )}
                    <DialogCancelButton />
                </DialogBottomButtons>
            </BloomDialog>
        </ThemeProvider>
    );
};

/** Shows the React-hosted decodable reader setup dialog. */
export const showDecodableReaderSetupDialog = (): void => {
    get("readers/io/readerSettingsEditForbidden", (result) => {
        postBoolean("editView/setModalState", true);
        getWorkspaceBundleExports().ShowEditViewDialog(
            <DecodableReaderSetupDialogLauncher
                editingForbiddenMessage={result.data || undefined}
            />,
        );
    });
};

/** Closes the React-hosted decodable reader setup dialog. */
export const closeDecodableReaderSetupDialog = (): void => {
    closeDialog();
};
