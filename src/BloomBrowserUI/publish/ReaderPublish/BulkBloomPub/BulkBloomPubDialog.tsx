/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import BloomButton from "../../../react_components/bloomButton";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "../../../react_components/BloomDialog/BloomDialog";
import { DialogCancelButton } from "../../../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../../../react_components/l10nHooks";
import { MuiCheckbox } from "../../../react_components/muiCheckBox";
import TextField from "@material-ui/core/TextField";
import { WhatsThisBlock } from "../../../react_components/helpLink";
import { ColorChooser } from "../../../react_components/colorChooser";
import { ConditionallyEnabledBlock } from "../../../react_components/ConditionallyEnabledBlock";
import { BloomApi } from "../../../utils/bloomApi";

export let showBulkBloomPubDialog: () => void;

export const BulkBloomPubDialog: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);
    const [bookshelfColor, setBookshelfColor] = React.useState("lightgreen");
    const bookShelfColorCallback = React.useCallback(
        (color: string) => setBookshelfColor(color),
        []
    );
    const [includeBookShelf, setIncludeBookshelf] = React.useState(true);
    const [makeBloomBundle, setMakeBloomBundle] = React.useState(false);

    showBulkBloomPubDialog = showDialog;
    const kBlockSeparation = "30px";

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
                <WhatsThisBlock url="https://docs.bloomlibrary.org/todo-something-about-bloom-bookshelves">
                    <MuiCheckbox
                        label="Include a .bloomshelf file"
                        checked={includeBookShelf}
                        l10nKey="Publish.BulkBloomPub.IncludeBloomShelf"
                        onCheckChanged={() =>
                            setIncludeBookshelf(!includeBookShelf)
                        }
                    ></MuiCheckbox>
                    <ConditionallyEnabledBlock enable={includeBookShelf}>
                        <div
                            css={css`
                                margin-left: 28px;
                            `}
                        >
                            <div
                                css={css`
                                    font-size: 10px;
                                    color: gray;
                                    margin-top: -9px;
                                `}
                            >
                                This file will cause these book to be grouped
                                under a single bookshelf in Bloom Reader. This
                                collection’s bookshelf is set to “Edolo Grade 1
                                Term 2”.
                            </div>
                            <div
                                css={css`
                                    margin-top: 10px;
                                    display: flex;
                                `}
                            >
                                <div
                                    css={css`
                                        border: solid 1px black;
                                        width: 225px;
                                        height: 20px;
                                        background-color: ${bookshelfColor};
                                        padding-left: 5px;
                                        padding-top: 3px;
                                    `}
                                >
                                    Edolo Grade 1 Term 2
                                </div>
                                {/* <BloomButton
                                l10nKey={"Common.ChooseColor"}
                                enabled={true}
                                hasText={true}
                                variant={"text"}
                                onClick={() =>
                                    showSimpleColorDialog(
                                        bookShelfColorCallback
                                    )
                                }
                            >
                                Choose Color
                            </BloomButton> */}

                                {/* TODO: for some reason, you can't enter custom colors with enter (but tab works).
                            After hitting Enter in the color chooser, we get "#" as the color, which appears as white. */}
                                <ColorChooser
                                    menuLeft={true}
                                    disabled={false}
                                    color={bookshelfColor}
                                    onColorChanged={colorChoice => {
                                        console.log("Color = " + colorChoice);
                                        setBookshelfColor(colorChoice);
                                    }}
                                    css={css`
                                        .cc-menu-arrow {
                                            margin-top: 8px;
                                        }
                                        .cc-pulldown-wrapper {
                                            left: -127px;
                                            border: solid 1px lightgray;
                                            padding: 10px;
                                        }
                                    `}
                                />
                            </div>
                        </div>
                    </ConditionallyEnabledBlock>
                </WhatsThisBlock>
                <WhatsThisBlock
                    url="https://docs.bloomlibrary.org/todo-something-about-distrubution-tags"
                    css={css`
                        margin-top: ${kBlockSeparation};
                    `}
                >
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
                <WhatsThisBlock
                    url="https://docs.bloomlibrary.org/todo-something-about-bloom-bundles"
                    css={css`
                        margin-top: ${kBlockSeparation};
                    `}
                >
                    <MuiCheckbox
                        label="Zip up into a single .bloomBundle file"
                        checked={makeBloomBundle}
                        l10nKey="Publish.BulkBloomPub.MakeBloomBundle"
                        onCheckChanged={checked => {
                            setMakeBloomBundle(!!checked);
                        }}
                    ></MuiCheckbox>
                </WhatsThisBlock>
            </DialogMiddle>
            <DialogBottomButtons>
                <BloomButton
                    l10nKey="Publish.BulkBloomPub.Make"
                    hasText={true}
                    enabled={true}
                    variant={"contained"}
                    onClick={() => {
                        BloomApi.postData(
                            "publish/android/file/bulkSaveBloomPubs",
                            {
                                includeBookshelfFile: includeBookShelf,
                                bookshelfColor: bookshelfColor,
                                includeBloomBundle: true,
                                distributionTag: "foobar"
                            }
                        );
                    }}
                >
                    Make
                </BloomButton>
                <DialogCancelButton onClick={closeDialog} />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
