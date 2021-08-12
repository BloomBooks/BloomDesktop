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
    window.alert("showBulkBloomPubDialog is not set up yet.");
};

// NB: this must match BulkSaveBloomPubsParams on the c# side
interface IBulkBloomPUBPublishParams {
    makeBookshelfFile: boolean;
    makeBloomBundle: boolean;
    bookshelfColor: string;
    distributionTag: string;
    bookshelfLabel?: string;
}

// wrapping the innards so that we're only doing API queries when we're actually visible (react dialogs just sit there invisible until they are opened)
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
                <InnerBulkBloomPubDialog closeDialog={closeDialog} />
            )}
        </BloomDialog>
    );
};

// we split this out in order to avoid querying until we open it
export const InnerBulkBloomPubDialog: React.FunctionComponent<{
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
    )?.defaultBookshelfUrlKey;

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
            <DialogTitle title={"Make All BloomPUBs from Collection"}>
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
                            label="Produce a .bloomshelf file"
                            checked={
                                bookshelfUrlKey && params.makeBookshelfFile
                            }
                            disabled={!bookshelfUrlKey}
                            l10nKey="Publish.BulkBloomPub.ProduceBloomShelf"
                            onCheckChanged={() =>
                                setParams({
                                    ...params,
                                    makeBookshelfFile: !params.makeBookshelfFile
                                })
                            }
                        ></MuiCheckbox>
                        <ConditionallyEnabledBlock
                            enable={
                                params.makeBookshelfFile && !!bookshelfUrlKey
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
                                    to "{params.bookshelfLabel}". Bug: after
                                    entering a custom color, you must press TAB.
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
                            checked={params.makeBloomBundle}
                            l10nKey="Publish.BulkBloomPub.MakeBloomBundle"
                            onCheckChanged={checked => {
                                setParams({
                                    ...params,
                                    makeBloomBundle: !!checked
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
