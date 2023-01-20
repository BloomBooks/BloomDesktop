/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import { Radio, Typography } from "@material-ui/core";
import * as React from "react";
import { useState } from "react";
import BloomButton from "../../react_components/bloomButton";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogMiddle,
    DialogTitle
} from "../../react_components/BloomDialog/BloomDialog";

import {
    DialogCancelButton,
    DialogReportButton
} from "../../react_components/BloomDialog/commonDialogComponents";
import { BookInfoCard } from "../../react_components/bookInfoCard";
import { useL10n } from "../../react_components/l10nHooks";
import { TextWithEmbeddedLink } from "../../react_components/link";
import { BloomApi } from "../../utils/bloomApi";
import { WireUpForWinforms } from "../../utils/WireUpWinform";
import { lightTheme } from "../../bloomMaterialUITheme";
import { CSSProperties, ThemeProvider } from "@material-ui/styles";
import HelpLink from "../../react_components/helpLink";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "../../react_components/BloomDialog/BloomDialogPlumbing";

export interface IUploadCollisionDlgProps {
    userEmail: string;
    newThumbUrl?: string;
    newTitle: string;
    newLanguages?: string[];
    existingTitle: string;
    existingLanguages?: string[];
    existingCreatedDate: string;
    existingUpdatedDate: string;
    existingBookUrl: string;
    existingThumbUrl?: string;
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}

export const UploadCollisionDlg: React.FunctionComponent<IUploadCollisionDlgProps> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);

    enum RadioState {
        Indeterminate,
        Same,
        Different
    }

    const [buttonState, setButtonState] = useState<RadioState>(
        RadioState.Indeterminate
    );

    const kAskForHelpColor = "#D65649";
    const kDarkerSecondaryTextColor = "#555555";

    const sameBook = useL10n(
        "Is this an update of your existing book?",
        "PublishTab.UploadCollisionDialog.SameBook",
        "This is the dialog title"
    );

    const bloomLibraryHasOne = useL10n(
        "BloomLibrary.org already has a book with this ID from you ({0}).",
        "PublishTab.UploadCollisionDialog.HaveOne",
        "This is the dialog subtitle. The {0} will be replaced with the uploader's email address.",
        props.userEmail
    );

    const existingCardHeader = useL10n(
        "Already in Bloom Library",
        "PublishTab.UploadCollisionDialog.AlreadyIn",
        "This is the header for the book that is in bloomlibrary.org already."
    );

    const uploadingCardHeader = useL10n("Uploading", "Common.Uploading");

    const differentBooksCommentary = useL10n(
        "Add a new book. Bloom will fix the ID of your book and upload it. The old book on Bloom Library will stay the same.",
        "PublishTab.UploadCollisionDialog.Radio.DifferentBooks.Commentary",
        "This is explanatory commentary on a radio button."
    );

    const sameBookRadioLabel = useL10n(
        "Yes, this is an update of my book",
        "PublishTab.UploadCollisionDialog.Radio.SameBook",
        "This is the label on a radio button."
    );

    const differentBooksRadioLabel = useL10n(
        "No, these are different books",
        "PublishTab.UploadCollisionDialog.Radio.DifferentBooks",
        "This is the label on a radio button."
    );

    lightTheme.palette.text.secondary = kDarkerSecondaryTextColor;

    // This could be pulled out to a separate file, or at least outside of the UploadCollisionDlg
    // component. But then this component would not have access to the buttonState.
    const RadioWithLabelAndCommentary: React.FC<{
        buttonStateToMatch: RadioState;
        radioValue: string;
        radioLabel: string;
        ariaLabel: string;
        commentaryChildren: JSX.Element;
    }> = props => (
        <div
            css={css`
                flex-direction: row;
                display: flex;
                align-items: flex-start;
            `}
        >
            <Radio
                checked={buttonState === props.buttonStateToMatch}
                value={props.radioValue}
                onChange={() => setButtonState(props.buttonStateToMatch)}
                name="radio-buttons"
                inputProps={{
                    "aria-label": props.ariaLabel
                }}
                color="primary"
            />
            <div
                css={css`
                    flex-direction: column;
                    display: flex;
                    margin-top: 10px;
                `}
            >
                <div
                    css={css`
                        font-weight: bold;
                    `}
                >
                    {props.radioLabel}
                </div>
                {props.commentaryChildren}
            </div>
        </div>
    );

    const sameBookRadioCommentary = (): JSX.Element => (
        <div
            css={css`
                margin-top: 5px;
                margin-bottom: 5px;
                a.embeddedLink {
                    color: #555555;
                    text-decoration: underline;
                }
            `}
        >
            <Typography color="textSecondary">
                <TextWithEmbeddedLink
                    className="embeddedLink"
                    l10nKey="PublishTab.UploadCollisionDialog.Radio.SameBook.Commentary"
                    l10nComment="This is explanatory commentary on a radio button. Don't translate the website reference in brackets ([bloomlibrary.org]). It will be replaced by a link to bloomlibrary.org."
                    href="https://www.bloomlibrary.org"
                >
                    Update the book. Bloom will remove the version on
                    [bloomlibrary.org] and replace it with your upload.
                </TextWithEmbeddedLink>
            </Typography>
        </div>
    );

    const differentBooksRadioCommentary = (): JSX.Element => (
        <div
            css={css`
                margin-top: 5px;
                p {
                    margin-block-end: 0;
                }
            `}
        >
            <Typography color="textSecondary">
                {differentBooksCommentary}
            </Typography>
            <div
                css={css`
                    margin-top: 5px;
                `}
            >
                <HelpLink
                    l10nKey="Common.WhatCausedThisProblem"
                    l10nComment="This is usually a link to an explanatory document."
                    helpId="User_Interface/Dialog_boxes/Are_these_the_same_book_dialog_box.htm"
                    style={whatCausedThisStyles}
                >
                    What caused this problem?
                </HelpLink>
            </div>
        </div>
    );

    const whatCausedThisStyles: CSSProperties = {
        fontSize: "smaller",
        color: kDarkerSecondaryTextColor,
        textDecoration: "underline"
    };

    return (
        <ThemeProvider theme={lightTheme}>
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
                        <Typography color="textSecondary">
                            {bloomLibraryHasOne}
                        </Typography>
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
                        <div
                            css={css`
                                p {
                                    margin-block-end: 0;
                                }
                            `}
                        >
                            <Typography>{existingCardHeader}</Typography>
                            <BookInfoCard
                                title={props.existingTitle}
                                bookUrl={props.existingBookUrl}
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
                        <div
                            css={css`
                                p {
                                    margin-block-end: 0;
                                }
                            `}
                        >
                            <Typography>{uploadingCardHeader}</Typography>
                            <BookInfoCard
                                title={props.newTitle}
                                thumbnailUrl={props.newThumbUrl}
                                languages={
                                    props.newLanguages
                                        ? props.newLanguages
                                        : [""]
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
                            width: 420px;
                        `}
                    >
                        <RadioWithLabelAndCommentary
                            buttonStateToMatch={RadioState.Same}
                            radioValue="Same"
                            radioLabel={sameBookRadioLabel}
                            ariaLabel="Same book radio button"
                            commentaryChildren={sameBookRadioCommentary()}
                        />
                        <RadioWithLabelAndCommentary
                            buttonStateToMatch={RadioState.Different}
                            radioValue="Different"
                            radioLabel={differentBooksRadioLabel}
                            ariaLabel="Different book radio button"
                            commentaryChildren={differentBooksRadioCommentary()}
                        />
                    </div>
                </DialogMiddle>
                <DialogBottomButtons>
                    <DialogBottomLeftButtons>
                        <DialogReportButton
                            l10nKey="Common.AskForHelp"
                            buttonText="Ask for help"
                            css={css`
                                span {
                                    color: ${kAskForHelpColor};
                                }
                            `}
                            shortMessage="Problem deciding if the uploading book is the same as the one on bloomlibrary.org."
                            messageGenerator={() =>
                                // Not trying to be very nice about this message. The user will not usually see it.
                                // It will be buried in the details of the report sent to YouTrack to tell US what went wrong.
                                `Trying to decide if the bloomlibrary.org '${props.existingTitle}', uploaded on ${props.existingCreatedDate}, is the same book as '${props.newTitle}'.`
                            }
                        />
                    </DialogBottomLeftButtons>
                    <BloomButton
                        l10nKey={"Common.Upload"}
                        enabled={buttonState !== RadioState.Indeterminate}
                        size="large"
                        onClick={() => {
                            BloomApi.postJson("libraryPublish/upload", {
                                sameOrDifferent:
                                    buttonState === RadioState.Same
                                        ? "same"
                                        : "different"
                            });
                            closeDialog();
                        }}
                    >
                        Upload
                    </BloomButton>
                    <DialogCancelButton
                        onClick={() => {
                            BloomApi.post("libraryPublish/cancel");
                            closeDialog();
                        }}
                    ></DialogCancelButton>
                </DialogBottomButtons>
            </BloomDialog>
        </ThemeProvider>
    );
};

WireUpForWinforms(UploadCollisionDlg);
