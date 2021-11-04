/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import { Radio, Typography } from "@material-ui/core";
import * as React from "react";
import { useState } from "react";
import { CSSProperties } from "reactcss/node_modules/@types/react";
import BloomButton from "../../react_components/bloomButton";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogMiddle,
    DialogTitle,
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "../../react_components/BloomDialog/BloomDialog";
import { DialogCancelButton } from "../../react_components/BloomDialog/commonDialogComponents";
import { BookInfoCard } from "../../react_components/bookInfoCard";
import { Div } from "../../react_components/l10nComponents";
import { useL10n } from "../../react_components/l10nHooks";
import { Link } from "../../react_components/link";
import { BloomApi } from "../../utils/bloomApi";
import { WireUpForWinforms } from "../../utils/WireUpWinform";

export interface IUploadCollisionDlgProps {
    userEmail: string;
    newThumbUrl?: string;
    newTitle: string;
    newLanguages?: string[];
    existingTitle: string;
    existingLanguages?: string[];
    existingCreatedDate: string;
    existingUpdatedDate: string;
    existingThumbUrl?: string;
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}

export const UploadCollisionDlg: React.FunctionComponent<IUploadCollisionDlgProps> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);

    const [buttonState, setButtonState] = useState<
        "indeterminate" | "same" | "different"
    >("indeterminate");

    const kAskForHelpColor = "#D65649";

    const sameBook = useL10n(
        "Are these the same book?",
        "PublishTab.UploadCollisionDialog.SameBook",
        "This is the dialog title"
    );

    const bloomLibraryHasOne = useL10n(
        "BloomLibrary.org already has a book with this ID from you ({0}).",
        "PublishTab.UploadCollisionDialog.HaveOne",
        "This is the dialog subtitle",
        props.userEmail
    );

    const alreadyIn = useL10n(
        "Already in Bloom Library",
        "PublishTab.UploadCollisionDialog.AlreadyIn",
        "This is the header for the book that is in bloomlibrary.org already."
    );

    const uploading = useL10n(
        "Uploading",
        "PublishTab.UploadCollisionDialog.Uploading",
        "This is the header for the book that is about to be uploaded."
    );

    // Normally we prefer the Emotion CSS, but I can't get it to work in the case we need here.
    const infoCardHeaderStyle: CSSProperties = {
        marginBlockEnd: "unset"
    };

    return (
        <BloomDialog {...propsForBloomDialog}>
            <div
                css={css`
                    flex-direction: column;
                `}
            >
                <DialogTitle
                    title={sameBook}
                    icon="BookIdCollision.svg"
                ></DialogTitle>
                <div
                    css={css`
                        margin-left: 45px;
                        margin-top: -20px;
                    `}
                >
                    <Typography>{bloomLibraryHasOne}</Typography>
                </div>
            </div>
            <DialogMiddle>
                {/* This section contains two BookInfoCards.
                    The first is for the book on the server.
                    The second is for the book the user is uploading currently.
                */}
                <div
                    css={css`
                        flex-direction: row;
                        display: flex;
                        margin-left: 45px;
                        margin-top: 38px;
                    `}
                >
                    <div>
                        <Typography style={infoCardHeaderStyle}>
                            {alreadyIn}
                        </Typography>
                        <BookInfoCard
                            title={props.existingTitle}
                            thumbnailUrl={props.existingThumbUrl}
                            languages={
                                props.existingLanguages
                                    ? props.existingLanguages
                                    : [""]
                            }
                            originalUpload={props.existingCreatedDate}
                            lastUpdated={props.existingUpdatedDate}
                        ></BookInfoCard>
                    </div>
                    <div
                        css={css`
                            min-width: 20px;
                        `}
                    />
                    <div>
                        <Typography style={infoCardHeaderStyle}>
                            {uploading}
                        </Typography>
                        <BookInfoCard
                            title={props.newTitle}
                            thumbnailUrl={props.newThumbUrl}
                            languages={
                                props.newLanguages ? props.newLanguages : [""]
                            }
                        ></BookInfoCard>
                    </div>
                </div>
                {/* This section contains two Radio buttons. Neither should be initially selected.
                    The first choice is to replace the book on the server.
                    The second choice is to change the ID of the book the user is uploading currently.
                    This would enable both books to exist on the server.
                */}
                <div
                    css={css`
                        flex-direction: column;
                        display: flex;
                        margin-left: 36px;
                        margin-top: 15px;
                    `}
                >
                    <div
                        css={css`
                            flex-direction: row;
                            display: flex;
                            align-items: flex-start;
                        `}
                    >
                        <Radio
                            checked={buttonState === "same"}
                            value="Same"
                            onChange={() => setButtonState("same")}
                            name="radio-buttons"
                            inputProps={{
                                "aria-label": "Same book radio button"
                            }}
                        />
                        <div
                            css={css`
                                flex-direction: column;
                                display: flex;
                                margin-top: 10px;
                                width: 360px;
                            `}
                        >
                            <Div
                                l10nKey="PublishTab.UploadCollisionDialog.Radio.SameBook"
                                l10nComment="This is the label on a radio button."
                                css={css`
                                    font-weight: bold;
                                `}
                            >
                                These are the same book
                            </Div>
                            <Div
                                l10nKey="PublishTab.UploadCollisionDialog.Radio.SameBook.Commentary"
                                l10nComment="This is explanatory commentary on a radio button."
                                css={css`
                                    margin-top: 5px;
                                `}
                            >
                                Update the book. Bloom will remove the version
                                on BloomLibrary.org and replace it with your
                                upload.
                            </Div>
                        </div>
                    </div>
                    <div
                        css={css`
                            flex-direction: row;
                            display: flex;
                            align-items: flex-start;
                            margin-top: 20px;
                        `}
                    >
                        <Radio
                            checked={buttonState === "different"}
                            value="Different"
                            onChange={() => setButtonState("different")}
                            name="radio-buttons"
                            inputProps={{
                                "aria-label": "Different book radio button"
                            }}
                        />
                        <div
                            css={css`
                                flex-direction: column;
                                display: flex;
                                margin-top: 10px;
                                width: 360px;
                            `}
                        >
                            <Div
                                l10nKey="PublishTab.UploadCollisionDialog.Radio.DifferentBooks"
                                l10nComment="This is the label on a radio button."
                                css={css`
                                    font-weight: bold;
                                `}
                            >
                                These are different books
                            </Div>
                            <Div
                                l10nKey="PublishTab.UploadCollisionDialog.Radio.DifferentBooks.Commentary"
                                l10nComment="This is explanatory commentary on a radio button."
                                css={css`
                                    margin-top: 5px;
                                `}
                            >
                                Add a new book. Bloom will fix the ID of your
                                book and upload it. The old book on Bloom
                                Library will stay the same.
                            </Div>
                            <Link
                                l10nKey="PublishTab.UploadCollisionDialog.Radio.DifferentBooks.WhatCausedLink"
                                l10nComment="This is a link in the label on a radio button."
                                color="textPrimary"
                            >
                                What caused this problem?
                            </Link>
                        </div>
                    </div>
                </div>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogBottomLeftButtons>
                    <BloomButton
                        css={css`
                            span {
                                color: ${kAskForHelpColor};
                            }
                        `}
                        variant="text"
                        size="large"
                        l10nKey={
                            "PublishTab.UploadCollisionDialog.AskForHelpButton"
                        }
                        enabled={true}
                    >
                        Ask For Help
                    </BloomButton>
                </DialogBottomLeftButtons>
                <BloomButton
                    l10nKey={"PublishTab.UploadCollisionDialog.UploadButton"}
                    enabled={
                        buttonState === "same" || buttonState === "different"
                    }
                    size="large"
                    // We need a new api maybe that has a ref to the upload control object.
                    onClick={() => {
                        BloomApi.postJson("webPublish/upload", {
                            sameOrDifferent: buttonState
                        });
                        closeDialog();
                    }}
                >
                    Upload
                </BloomButton>
                <DialogCancelButton
                    onClick={() => {
                        BloomApi.post("webPublish/cancel");
                        closeDialog();
                    }}
                ></DialogCancelButton>
            </DialogBottomButtons>
        </BloomDialog>
    );
};

WireUpForWinforms(UploadCollisionDlg);
