/** @jsxImportSource @emotion/react */
import { css } from "@emotion/react";
import {
    LanguageChooser,
    IOrthography,
    defaultSearchResultModifier
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

// function languageInfo(selectedLanguageOrthography: IOrthography) {
//     return {
//         LanguageTag: createTag(
//             selectedLanguageOrthography.language.languageCode
//         ),
//         ThreeLetterTag: selectedLanguageOrthography.language.iso639_3,
//         IsMacroLanguage: selectedLanguageOrthography.language.isMacrolanguage,
//         // TODO add exonym and autonym back in here. Maybe rename to "other names"?
//         Names: selectedLanguageOrthography.language.names,
//         Countries: selectedLanguageOrthography.language.regionNames.split(", "),
//         PrimaryCountry: selectedLanguageOrthography.customDetails.region,
//         // TODO make sure this is empty if no custom name chosen
//         DesiredName: selectedLanguageOrthography.customDetails.displayName
//     };
// }

export const LanguageChooserDialog: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
    initialLanguageTag?: string;
    initialCustomName?: string;
}> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);

    show = showDialog;

    function handleClose(
        languageSelection: IOrthography | undefined,
        languageTag: string | undefined
    ) {
        // if user clicked cancel, languageTag will be undefined
        if (languageTag) {
            console.log("posting", languageTag);

            postData("settings/changeLanguage", {
                LanguageTag: languageTag,
                DefaultName:
                    // TODO when published from language-chooser-react-mui, we should use
                    // defaultDisplayName(languageSelection)
                    languageSelection?.language.autonym ||
                    languageSelection?.language.exonym,
                DesiredName: languageSelection?.customDetails?.displayName
            });
        }

        closeDialog();
    }
    return (
        <BloomDialog
            {...propsForBloomDialog}
            css={css`
                padding: 0;
            `}
        >
            <LanguageChooser
                searchResultModifier={defaultSearchResultModifier}
                initialSearchString={props.initialLanguageTag?.split("-")[0]}
                initialSelectionLanguageTag={props.initialLanguageTag}
                initialCustomDisplayName={props.initialCustomName}
                onClose={handleClose}
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
