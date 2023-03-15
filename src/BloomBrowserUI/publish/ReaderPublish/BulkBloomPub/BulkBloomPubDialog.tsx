/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import BloomButton from "../../../react_components/bloomButton";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle
} from "../../../react_components/BloomDialog/BloomDialog";
import { DialogCancelButton } from "../../../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../../../react_components/l10nHooks";
import { BloomCheckbox } from "../../../react_components/BloomCheckBox";
import TextField from "@mui/material/TextField";
import { WhatsThisBlock } from "../../../react_components/helpLink";
import { BloomPalette } from "../../../react_components/color-picking/bloomPalette";
import { ColorDisplayButton } from "../../../react_components/color-picking/colorPickerDialog";
import { ConditionallyEnabledBlock } from "../../../react_components/ConditionallyEnabledBlock";
import {
    postData,
    useApiData,
    useApiOneWayState
} from "../../../utils/bloomApi";
import { useGetLabelForCollection } from "../../../contentful/UseContentful";
import { Div } from "../../../react_components/l10nComponents";
import { kMutedTextGray } from "../../../bloomMaterialUITheme";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "../../../react_components/BloomDialog/BloomDialogPlumbing";

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
    const [params, setParams] = useApiOneWayState<
        IBulkBloomPUBPublishParams | undefined
    >("publish/bloompub/file/bulkSaveBloomPubsParams", undefined);

    const bookshelfUrlKey = useApiData<any>("settings/bookShelfData", "")
        ?.defaultBookshelfUrlKey;

    // the server doesn't actually know the label for the bookshelf, just its urlKey. So we have to look that up ourselves.
    const bookshelfLabel = useGetLabelForCollection(bookshelfUrlKey, "");
    React.useEffect(() => {
        if (bookshelfLabel) {
            setParams({ ...params!, bookshelfLabel });
        }
    }, [bookshelfLabel]);

    const kBlockSeparation = "30px";

    const dialogTitle = useL10n(
        "Make All BloomPUBs from Collection",
        "PublishTab.BulkBloomPub.MakeAllBloomPubs"
    );

    const colorPickerTitle = useL10n(
        "Bookshelf Color",
        "PublishTab.Android.BulkBookshelfColor"
    );

    return (
        <React.Fragment>
            <DialogTitle title={dialogTitle}>
                <img
                    css={css`
                        height: 16px;
                        margin-left: 20px;
                    `}
                    src="/bloom/images/bloom-enterprise-badge.svg"
                />
            </DialogTitle>
            {!!params && (
                <DialogMiddle>
                    <WhatsThisBlock url="https://docs.bloomlibrary.org/bloom-reader-shelves">
                        <BloomCheckbox
                            label="Produce a .bloomshelf file"
                            checked={
                                bookshelfUrlKey && params.makeBookshelfFile
                            }
                            disabled={!bookshelfUrlKey}
                            l10nKey="PublishTab.BulkBloomPub.ProduceBloomShelf"
                            onCheckChanged={() =>
                                setParams({
                                    ...params,
                                    makeBookshelfFile: !params.makeBookshelfFile
                                })
                            }
                        ></BloomCheckbox>
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
                                <Div
                                    l10nKey={
                                        "PublishTab.BulkBloomPub.Explanation"
                                    }
                                    l10nParam0={params.bookshelfLabel ?? ""}
                                    css={css`
                                        font-size: 10px;
                                        color: ${kMutedTextGray};
                                        margin-top: -9px;
                                    `}
                                >
                                    {`This file will cause these books to be
                                    grouped under a single bookshelf in Bloom
                                    Reader. This collection's bookshelf is set
                                    to "${params.bookshelfLabel ?? ""}"`}
                                </Div>
                                <div
                                    css={css`
                                        margin-top: 10px;
                                        display: flex;
                                    `}
                                >
                                    <div
                                        css={css`
                                            border: dotted 1px gray;
                                            width: 225px;
                                            height: 20px;
                                            padding-left: 5px;
                                            padding-top: 3px;
                                        `}
                                    >
                                        {params.bookshelfLabel}
                                    </div>
                                    <div
                                        css={css`
                                            margin-left: 16px;
                                        `}
                                    >
                                        <ColorDisplayButton
                                            initialColor={params.bookshelfColor}
                                            localizedTitle={colorPickerTitle}
                                            transparency={false}
                                            palette={
                                                BloomPalette.BloomReaderBookshelf
                                            }
                                            width={75}
                                            onClose={(
                                                _result,
                                                newColor: string
                                            ) => {
                                                setParams({
                                                    ...params,
                                                    bookshelfColor: newColor
                                                });
                                            }}
                                        />
                                    </div>
                                </div>
                            </div>
                        </ConditionallyEnabledBlock>
                    </WhatsThisBlock>
                    <WhatsThisBlock
                        url="https://docs.bloomlibrary.org/bloom-reader-distribution-tags"
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
                        url="https://docs.bloomlibrary.org/bloomPUB-bundles"
                        css={css`
                            margin-top: ${kBlockSeparation};
                        `}
                    >
                        <BloomCheckbox
                            label="Compress into a single .bloombundle file"
                            checked={params.makeBloomBundle}
                            l10nKey="PublishTab.BulkBloomPub.MakeBloomBundle"
                            onCheckChanged={checked => {
                                setParams({
                                    ...params,
                                    makeBloomBundle: !!checked
                                });
                            }}
                        ></BloomCheckbox>
                    </WhatsThisBlock>
                </DialogMiddle>
            )}
            <DialogBottomButtons>
                <BloomButton
                    l10nKey="PublishTab.BulkBloomPub.Make"
                    hasText={true}
                    enabled={true}
                    variant={"contained"}
                    onClick={() => {
                        postData(
                            "publish/bloompub/file/bulkSaveBloomPubs",
                            params
                        );
                        props.closeDialog();
                    }}
                >
                    Make
                </BloomButton>
                <DialogCancelButton onClick_DEPRECATED={props.closeDialog} />
            </DialogBottomButtons>
        </React.Fragment>
    );
};
