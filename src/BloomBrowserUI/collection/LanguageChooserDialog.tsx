/** @jsxImportSource @emotion/react */
import { css } from "@emotion/react";
import {
    LanguageChooser,
    IOrthography,
    defaultSearchResultModifier,
    parseLangtagFromLangChooser,
    defaultRegionForLangTag
} from "@ethnolib/language-chooser-react-mui";
import * as React from "react";

import { WireUpForWinforms } from "../utils/WireUpWinform";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "../react_components/BloomDialog/BloomDialogPlumbing";
import { postData } from "../utils/bloomApi";
import { BloomDialog } from "../react_components/BloomDialog/BloomDialog";
import ReactDOM = require("react-dom");
import { AppBar, Toolbar, Typography } from "@mui/material";
import { kBloomLightGray } from "../utils/colorUtils";
import BloomButton from "../react_components/bloomButton";

export interface ILanguageDataForBloom {
    LanguageTag: string | null;
    DefaultName: string | null;
    DesiredName: string | null;
    Country?: string | null;
}

export function formatLanguageDataForBloom(
    languageTag: string | undefined,
    languageSelection: IOrthography | undefined
): ILanguageDataForBloom {
    return {
        LanguageTag: languageTag || null,
        DefaultName:
            // TODO when published from language-chooser-react-mui, we should use
            // defaultDisplayName(languageSelection)
            languageSelection?.language.autonym ||
            languageSelection?.language.exonym ||
            null,
        DesiredName: languageSelection?.customDetails?.displayName || null,
        Country: defaultRegionForLangTag(languageTag).name
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
            <BloomButton
                css={css`
                    margin-left: auto;
                    margin-right: 10px;
                    min-width: 100px;
                `}
                variant="contained"
                enabled={pendingSelection.language !== undefined}
                onClick={() => {
                    onOk(pendingSelection, pendingLanguageTag);
                }}
                l10nKey="Common.OK"
            >
                OK
            </BloomButton>
            <BloomButton
                css={css`
                    min-width: 100px;
                `}
                variant="outlined"
                color="primary"
                onClick={closeDialog}
                enabled={true}
                l10nKey="Common.Cancel"
            >
                Cancel
            </BloomButton>
        </div>
    );

    function onOk(languageSelection: IOrthography, languageTag: string) {
        postData(
            "settings/changeLanguage",
            formatLanguageDataForBloom(languageTag, languageSelection)
        );
        closeDialog();
    }

    return (
        <BloomDialog
            {...propsForBloomDialog}
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
                <Toolbar
                    disableGutters
                    variant="dense"
                    css={css`
                        padding-top: 5px;
                        padding-left: 15px;
                    `}
                >
                    <Typography
                        component="div"
                        css={css`
                            color: black;
                            font-size: 1.25rem;
                            font-weight: 600;
                            line-height: 1.6;
                            letter-spacing: 0.0075em;
                        `}
                    >
                        Choose Language
                    </Typography>
                </Toolbar>
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
