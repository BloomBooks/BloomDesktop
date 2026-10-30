import { css } from "@emotion/react";
import {
    LanguageChooser,
    IOrthography,
    defaultSearchResultModifier,
    parseLangtagFromLangChooser,
} from "@ethnolib/language-chooser-react-mui";
import * as React from "react";
import { getLanguageData, ILanguageData } from "./languageData";

import { get } from "../utils/bloomApi";
import {
    BloomDialog,
    DialogBottomButtons,
} from "../react_components/BloomDialog/BloomDialog";
import { AppBar } from "@mui/material";
import { kBloomLightGray } from "../utils/colorUtils";
import {
    DialogCancelButton,
    DialogOkButton,
} from "../react_components/BloomDialog/commonDialogComponents";
import { H1 } from "../react_components/l10nComponents";

// The ethnolib language chooser in a dialog. It shows while its owner renders it: the owner takes
// the language chosen, and stops rendering it when it closes.
export const LanguageChooserDialog: React.FunctionComponent<{
    initialLanguageTag?: string;
    initialCustomName?: string;
    onChosen: (language: ILanguageData) => void;
    // Called when the dialog closes, by OK or Cancel.
    onClose: () => void;
}> = (props) => {
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
                <DialogCancelButton />
            </DialogBottomButtons>
        </div>
    );

    function onOk(languageSelection: IOrthography, languageTag: string) {
        props.onChosen(getLanguageData(languageTag, languageSelection));
        props.onClose();
    }

    const [uiLanguage, setUiLanguage] = React.useState("en");
    React.useEffect(() => {
        get("currentUiLanguage", (result) => {
            setUiLanguage(result.data);
        });
    }, []);

    return (
        <BloomDialog
            open={true}
            onClose={props.onClose}
            onCancel={props.onClose}
            maxWidth={false}
            css={css`
                padding: 0;
                .MuiDialog-paper {
                    width: 1000px;
                    height: 580px;
                }
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
