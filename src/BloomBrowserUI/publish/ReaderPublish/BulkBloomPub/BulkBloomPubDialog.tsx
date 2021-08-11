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
import { useGetLabelForCollection } from "../../../contentful/UseContentful";

export let showBulkBloomPubDialog: () => void = () => {
    window.alert("showBulkBloomPubDialog is not set up yet.2");
};

// NB: this must match BulkSaveBloomPubsParams on the c# side
interface IBulkBloomPUBPublishParams {
    includeBookshelfFile: boolean;
    includeBloomBundle: boolean;
    bookshelfColor: string;
    distributionTag: string;
    bookshelfLabel?: string;
}

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
            {propsForBloomDialog.open && (
                <InnerBulkBloomPubDialog
                    showDialog={showDialog}
                    closeDialog={closeDialog}
                />
            )}
        </BloomDialog>
    );
};

// we split this out in order to avoid querying until we open it
export const InnerBulkBloomPubDialog: React.FunctionComponent<{
    showDialog: () => void;
    closeDialog: () => void;
}> = props => {
    // We get the state from the server, but we don't inform it every time the user touches a control.
    // We'll send the new state of the parameters if and when they click the "make" button.
    const [params, setParams] = BloomApi.useApiOneWayState<
        IBulkBloomPUBPublishParams | undefined
    >("publish/android/file/bulkSaveBloomPubsParams", undefined);

    const bookshelfUrlKey = BloomApi.useApiData<any>(
        "settings/bookShelfData",
        ""
    )?.defaultBookshelf;

    // the server doesn't actually know the label for the bookshelf, just its urlKey. So we have to look that up ourselves.
    const bookshelfLabel = useGetLabelForCollection(bookshelfUrlKey, "");
    React.useEffect(() => {
        if (bookshelfLabel) {
            setParams({ ...params!, bookshelfLabel });
        }
    }, [bookshelfLabel]);

    const kBlockSeparation = "30px";

    return (
        <React.Fragment>
            <DialogTitle title={"Make BloomPubs From Collection"}>
                <img
                    css={css`
                        height: 16px;
                        margin-left: 20px;
                    `}
                    src="../../../images/bloom-enterprise-badge.svg"
                />
            </DialogTitle>
            {!!params && (
                <DialogMiddle>
                    <WhatsThisBlock url="https://docs.bloomlibrary.org/todo-something-about-bloom-bookshelves">
                        <MuiCheckbox
                            label="Include a .bloomshelf file"
                            checked={
                                bookshelfUrlKey && params.includeBookshelfFile
                            }
                            disabled={!bookshelfUrlKey}
                            l10nKey="Publish.BulkBloomPub.IncludeBloomShelf"
                            onCheckChanged={() =>
                                setParams({
                                    ...params,
                                    includeBookshelfFile: !params.includeBookshelfFile
                                })
                            }
                        ></MuiCheckbox>
                        <ConditionallyEnabledBlock
                            enable={
                                params.includeBookshelfFile && !!bookshelfUrlKey
                            }
                        >
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
                                    This file will cause these book to be
                                    grouped under a single bookshelf in Bloom
                                    Reader. This collection’s bookshelf is set
                                    to “$
                                    {params.bookshelfLabel}”.
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
                                            background-color: ${params.bookshelfColor};
                                            padding-left: 5px;
                                            padding-top: 3px;
                                        `}
                                    >
                                        {params.bookshelfLabel}
                                    </div>

                                    {/* TODO: for some reason, you can't enter custom colors with enter (but tab works).
                            After hitting Enter in the color chooser, we get "#" as the color, which appears as white. */}
                                    <ColorChooser
                                        menuLeft={true}
                                        disabled={false}
                                        color={params.bookshelfColor}
                                        onColorChanged={colorChoice => {
                                            console.log(
                                                "Color = " + colorChoice
                                            );
                                            setParams({
                                                ...params,
                                                bookshelfColor: colorChoice
                                            });
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
                            defaultValue={params.distributionTag}
                            onChange={event =>
                                setParams({
                                    ...params,
                                    distributionTag: event.target.value
                                })
                            }
                            margin="dense"
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
                            disabled={true} // not implemented yet
                            label="Zip up into a single .bloomBundle file"
                            checked={params.includeBloomBundle}
                            l10nKey="Publish.BulkBloomPub.MakeBloomBundle"
                            onCheckChanged={checked => {
                                setParams({
                                    ...params,
                                    includeBloomBundle: !!checked
                                });
                            }}
                        ></MuiCheckbox>
                    </WhatsThisBlock>
                </DialogMiddle>
            )}
            <DialogBottomButtons>
                <BloomButton
                    l10nKey="Publish.BulkBloomPub.Make"
                    hasText={true}
                    enabled={true}
                    variant={"contained"}
                    onClick={() => {
                        BloomApi.postData(
                            "publish/android/file/bulkSaveBloomPubs",
                            params
                        );
                        props.closeDialog();
                    }}
                >
                    Make
                </BloomButton>
                <DialogCancelButton onClick={props.closeDialog} />
            </DialogBottomButtons>
        </React.Fragment>
    );
};
