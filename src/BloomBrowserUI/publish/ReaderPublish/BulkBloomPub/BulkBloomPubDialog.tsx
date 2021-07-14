/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { useState } from "react";

import { BloomApi } from "../../../utils/bloomApi";
import BloomButton from "../../../react_components/bloomButton";
import { Div, P } from "../../../react_components/l10nComponents";
import { kDialogPadding } from "../../../bloomMaterialUITheme";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "../../../react_components/BloomDialog/BloomDialog";
import {
    DialogCancelButton,
    DialogControlGroup,
    DialogFolderChooser,
    ErrorBox
} from "../../../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../../../react_components/l10nHooks";
import { Checkbox } from "../../../react_components/checkbox";
import { MuiCheckbox } from "../../../react_components/muiCheckBox";
import TextField from "@material-ui/core/TextField";
import { WhatsThisBlock } from "../../../react_components/helpLink";

export let showBulkBloomPubDialog: () => void;

export const BulkBloomPubDialog: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);

    showBulkBloomPubDialog = showDialog;

    return (
        <BloomDialog {...propsForBloomDialog}>
            <DialogTitle title={"Make BloomPubs From Collection"}>
                <img
                    css={css`
                        height: 16px;
                        margin-left: 20px;
                    `}
                    src="../../../images/bloom-enterprise-badge.svg"
                />
            </DialogTitle>
            <DialogMiddle>
                <WhatsThisBlock helpId="todo">
                    <MuiCheckbox
                        label="Include a .bloomshelf file"
                        checked={false}
                        l10nKey="Publish.BulkBloomPub.IncludeBloomShelf"
                        onCheckChanged={() => {}}
                    ></MuiCheckbox>
                    <div
                        css={css`
                            margin-left: 28px;
                        `}
                    >
                        <div
                            css={css`
                                font-size: 10px;
                                color: gray;
                            `}
                        >
                            This file will cause these book to be grouped under
                            a single bookshelf in Bloom Reader. Collection’s
                            bookshelf is set to “Edolo Grade 1 Term 2”.
                        </div>
                        <div
                            css={css`
                                margin-top: 15px;
                            `}
                        >
                            <div
                                css={css`
                                    border: solid 1px black;
                                    width: 225px;
                                    height: 20px;
                                    background-color: #ffe000;
                                    padding-left: 5px;
                                    padding-top: 3px;
                                `}
                            >
                                Edolo Grade 1 Term 2
                            </div>
                            <BloomButton
                                l10nKey={"Common.ChooseColor"}
                                enabled={true}
                                hasText={true}
                                variant={"text"}
                            >
                                Choose Color
                            </BloomButton>
                        </div>
                    </div>
                </WhatsThisBlock>
                <WhatsThisBlock helpId="todo">
                    <TextField
                        label={"Distribution Tag"}
                        defaultValue={"Blah"}
                        margin="normal"
                        variant="outlined"
                        css={css`
                            margin-top: 0 !important;
                        `}
                        // fullWidth={f.type == "bigEditableText"}
                        // multiline={f.type == "bigEditableText"}
                        // onBlur={(
                        //     event: React.FocusEvent<HTMLTextAreaElement>
                        // ) => {
                        //     this.props.metadata[f.key].value =
                        //         event.currentTarget.value;
                        // }}
                    ></TextField>
                </WhatsThisBlock>
                <WhatsThisBlock helpId="todo">
                    <MuiCheckbox
                        label="Zip up into a single .bloomBundle file"
                        checked={false}
                        l10nKey="Publish.BulkBloomPub.MakeBloomBundle"
                        onCheckChanged={() => {}}
                    ></MuiCheckbox>
                </WhatsThisBlock>
            </DialogMiddle>
            <DialogBottomButtons>
                <BloomButton
                    l10nKey="Publish.BulkBloomPub.Make"
                    hasText={true}
                    enabled={true}
                    variant={"contained"}
                    onClick={() => {}}
                >
                    Make
                </BloomButton>
                <DialogCancelButton onClick={closeDialog} />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
