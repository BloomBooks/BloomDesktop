/** @jsxImportSource @emotion/react */
import { css } from "@emotion/react";
import "./styles.css";
import {
    LanguageChooser,
    IOrthography
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
    DialogTitle
} from "../react_components/BloomDialog/BloomDialog";
import ReactDOM = require("react-dom");

function languageInfo(selectedLanguage: IOrthography): LanguageInfo {}

export const LanguageChooserDialog: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
    initialSelection?: IOrthography;
    languageNumberOrSL?: string;
}> = props => {
    if (props.initialSelection == undefined) {
        props.initialSelection = {} as IOrthography;
    }

    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);

    show = showDialog;

    const dialogTitle = "Choose Language";

    // TODO might need something to disable stuff while open?

    const [languageSelection, setLanguageSelection] = React.useState(
        props.initialSelection
    );

    function handleOk() {
        // TODO
        postData("settings/changeLanguage", {
            languageSelection,
            languageNumberOrSL: props.languageNumberOrSL
        });
        closeDialog();
    }
    return (
        <BloomDialog {...propsForBloomDialog}>
            <DialogTitle
                title={dialogTitle}
                css={css`
                    padding-bottom: 0;
                    margin-bottom: 0;
                `}
            />
            <LanguageChooser
                searchResultModifier={defaultSearchResultModifier}
                initialState={languageSelection}
                onClose={handleClose}
            />
        </BloomDialog>
    );
};

WireUpForWinforms(LanguageChooserDialog);

let show: () => void = () => {
    window.alert("LanguageChooserDialog is not set up yet.");
};

export function showLanguageChooserDialog(initialSelection?: IOrthography) {
    try {
        ReactDOM.render(
            <LanguageChooserDialog initialSelection={initialSelection} />,
            getModalContainer()
        );
    } catch (error) {
        console.error(error);
    }
    show();
}
