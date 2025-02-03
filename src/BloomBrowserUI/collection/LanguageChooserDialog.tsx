/** @jsxImportSource @emotion/react */
import { css } from "@emotion/react";
import {
    LanguageChooser,
    IOrthography,
    defaultSearchResultModifier,
    parseLangtagFromLangChooser,
    defaultRegionForLangTag,
    defaultDisplayName
} from "@ethnolib/language-chooser-react-mui";
import * as React from "react";

import { WireUpForWinforms } from "../utils/WireUpWinform";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "../react_components/BloomDialog/BloomDialogPlumbing";
import { postData } from "../utils/bloomApi";
import {
    BloomDialog,
    DialogBottomButtons
} from "../react_components/BloomDialog/BloomDialog";
import ReactDOM = require("react-dom");
import { AppBar } from "@mui/material";
import { kBloomLightGray } from "../utils/colorUtils";
import {
    DialogCancelButton,
    DialogOkButton
} from "../react_components/BloomDialog/commonDialogComponents";
import { H1 } from "../react_components/l10nComponents";

export interface ILanguageData {
    // Should be kept in sync with the LanguageChangeEventArgs class
    LanguageTag: string | null;
    DefaultName: string | null;
    DesiredName: string | null;
    Country?: string | null;
}

export function getLanguageData(
    languageTag: string | undefined,
    selection: IOrthography | undefined
): ILanguageData {
    return {
        LanguageTag: languageTag || null,
        DefaultName: selection?.language
            ? defaultDisplayName(selection.language)
            : null,
        DesiredName: selection?.customDetails?.displayName || null,
        Country: languageTag ? defaultRegionForLangTag(languageTag)?.name : null
    };
}

export const LanguageChooserDialog: React.FunctionComponent<{
    initialLanguageTag?: string;
    initialCustomName?: string;
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);

    show = showDialog;

    const initialSelection:
        | IOrthography
        | undefined = parseLangtagFromLangChooser(
        props.initialLanguageTag || ""
    );
    const [pendingSelection, setPendingSelection] = React.useState(
        initialSelection || ({} as IOrthography)
    );
    const [pendingLanguageTag, setPendingLanguageTag] = React.useState(
        props.initialLanguageTag || ""
    );
    function onSelectionChange(
        orthographyInfo: IOrthography | undefined,
        languageTag: string | undefined
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
        postData(
            "settings/changeLanguage",
            getLanguageData(languageTag, languageSelection)
        );
        closeDialog();
    }

    return (
        <BloomDialog
            {...propsForBloomDialog}
            onCancel={closeDialog}
            onClose={closeDialog}
            css={css`
                padding: 0;
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

export function showLanguageChooserDialog(initialLanguageTag?: string) {
    try {
        ReactDOM.render(
            <LanguageChooserDialog initialLanguageTag={initialLanguageTag} />,
            getModalContainer()
        );
    } catch (error) {
        console.error(error);
    }
    show();
}

function getModalContainer(): HTMLElement {
    let modalDialogContainer = document.getElementById(
        "LanguageChooserDialogContainer"
    );
    if (modalDialogContainer) {
        modalDialogContainer.remove();
    }
    modalDialogContainer = document.createElement("div");
    modalDialogContainer.id = "LanguageChooserDialogContainer";
    document.body.appendChild(modalDialogContainer);
    return modalDialogContainer;
}
