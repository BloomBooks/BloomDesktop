import { css } from "@emotion/react";
import {
    LanguageChooser,
    IOrthography,
    defaultSearchResultModifier,
    parseLangtagFromLangChooser,
} from "@ethnolib/language-chooser-react-mui";
import * as React from "react";
import { getLanguageData, ILanguageData } from "./languageData";

import { WireUpForWinforms } from "../utils/WireUpWinform";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog,
} from "../react_components/BloomDialog/BloomDialogPlumbing";
import { get, postData } from "../utils/bloomApi";
import {
    BloomDialog,
    DialogBottomButtons,
} from "../react_components/BloomDialog/BloomDialog";
import { renderRootSync } from "../utils/reactRender";
import { AppBar } from "@mui/material";
import { kBloomLightGray } from "../utils/colorUtils";
import {
    DialogCancelButton,
    DialogOkButton,
} from "../react_components/BloomDialog/commonDialogComponents";
import { H1 } from "../react_components/l10nComponents";

export const LanguageChooserDialog: React.FunctionComponent<{
    initialLanguageTag?: string;
    initialCustomName?: string;
    dialogEnvironment?: IBloomDialogEnvironmentParams;
    // Given when the chooser is opened from another React dialog (Collection Settings), which
    // takes the language chosen here. Without it, the choice is posted to the WinForms dialog.
    onChosen?: (language: ILanguageData) => void;
    // Called when the dialog closes, by OK or Cancel, so its owner can stop rendering it.
    onClose?: () => void;
}> = (props) => {
    const {
        showDialog,
        closeDialog: closeThisDialog,
        propsForBloomDialog,
    } = useSetupBloomDialog(props.dialogEnvironment);

    show = showDialog;

    function closeDialog() {
        closeThisDialog();
        props.onClose?.();
    }

    const initialSelection: IOrthography | undefined =
        parseLangtagFromLangChooser(props.initialLanguageTag || "");
    const [pendingSelection, setPendingSelection] = React.useState(
        initialSelection || ({} as IOrthography),
    );
    const [pendingLanguageTag, setPendingLanguageTag] = React.useState(
        props.initialLanguageTag || "",
    );
    function onSelectionChange(
        orthographyInfo: IOrthography | undefined,
        languageTag: string | undefined,
    ) {
        setPendingSelection(orthographyInfo || ({} as IOrthography));
        setPendingLanguageTag(languageTag || "");
    }
    const dialogActionButtons = (
        <div
            id="dialog-action-buttons-container"
            css={css`
                width: 100%;
                display: flex;
                justify-content: flex-end;
                padding-top: 15px;
                padding-bottom: 5px;
            `}
        >
            <DialogBottomButtons>
                <DialogOkButton
                    onClick={() => {
                        onOk(pendingSelection, pendingLanguageTag);
                    }}
                    default={true}
                    enabled={pendingSelection.language !== undefined}
                />
                <DialogCancelButton onClick_DEPRECATED={closeDialog} />
            </DialogBottomButtons>
        </div>
    );

    function onOk(languageSelection: IOrthography, languageTag: string) {
        const language = getLanguageData(languageTag, languageSelection);
        if (props.onChosen) props.onChosen(language);
        else postData("settings/changeLanguage", language);
        closeDialog();
    }

    const [uiLanguage, setUiLanguage] = React.useState("en");
    React.useEffect(() => {
        get("currentUiLanguage", (result) => {
            setUiLanguage(result.data);
        });
    }, []);

    return (
        <BloomDialog
            {...propsForBloomDialog}
            onClose={closeDialog}
            // Without a WinForms frame to size it, give it the size that frame has.
            maxWidth={false}
            css={css`
                padding: 0;
                ${propsForBloomDialog.dialogFrameProvidedExternally
                    ? ""
                    : `.MuiDialog-paper {
                        width: 1000px;
                        height: 580px;
                    }`}
            `}
        >
            <AppBar
                position="static"
                css={css`
                    background-color: white;
                    box-shadow: none;
                    border-bottom: 2px solid ${kBloomLightGray};
                    flex-grow: 0;
                `}
            >
                <H1
                    l10nKey="CollectionSettingsDialog.LanguageTab.ChooseLanguage"
                    css={css`
                        color: black;
                        font-size: 1.25rem;
                        font-weight: 600;
                        line-height: 1.6;
                        letter-spacing: 0.0075em;
                        margin: 10px 15px 5px 15px;
                    `}
                >
                    Choose Language
                </H1>
            </AppBar>
            <LanguageChooser
                uiLanguage={uiLanguage}
                searchResultModifier={defaultSearchResultModifier}
                initialSearchString={props.initialLanguageTag?.split("-")[0]}
                initialSelectionLanguageTag={props.initialLanguageTag}
                initialCustomDisplayName={props.initialCustomName}
                onSelectionChange={onSelectionChange}
                actionButtons={dialogActionButtons}
            />
        </BloomDialog>
    );
};

WireUpForWinforms(LanguageChooserDialog);

let show: () => void = () => {
    window.alert("LanguageChooserDialog is not set up yet.");
};

export function showLanguageChooserDialog(
    initialLanguageTag?: string,
    initialCustomName?: string,
) {
    try {
        renderRootSync(
            <LanguageChooserDialog
                initialLanguageTag={initialLanguageTag}
                initialCustomName={initialCustomName}
            />,
            getModalContainer(),
        );
    } catch (error) {
        console.error(error);
    }
    show();
}

function getModalContainer(): HTMLElement {
    let modalDialogContainer = document.getElementById(
        "LanguageChooserDialogContainer",
    );
    if (modalDialogContainer) {
        modalDialogContainer.remove();
    }
    modalDialogContainer = document.createElement("div");
    modalDialogContainer.id = "LanguageChooserDialogContainer";
    document.body.appendChild(modalDialogContainer);
    return modalDialogContainer;
}
