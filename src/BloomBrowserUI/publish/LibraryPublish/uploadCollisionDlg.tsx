/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import { Radio, Typography } from "@mui/material";
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
import { post } from "../../utils/bloomApi";
import { WireUpForWinforms } from "../../utils/WireUpWinform";
import { lightTheme } from "../../bloomMaterialUITheme";
import HelpLink from "../../react_components/helpLink";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "../../react_components/BloomDialog/BloomDialogPlumbing";
import { BloomCheckbox } from "../../react_components/BloomCheckBox";
import { default as RightIcon } from "@mui/icons-material/Forward";
import WarningIcon from "@mui/icons-material/Warning";

export interface IPermissions {
    reupload: boolean;
    delete: boolean;
    editSurfaceMetadata: boolean;
    becomeUploader: boolean;
}

// Data this dialog uses (from C# api)
export interface IUploadCollisionDlgData {
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
    uploader?: string;
    onCancel?: () => void;
    dialogEnvironment?: IBloomDialogEnvironmentParams;
    permissions?: IPermissions;
    // The number of books on the server with the same ID as the book being uploaded.
    // (The other details apply to one of them)
    count: number;
}

// Props for the dialog adds these two items
export interface IUploadCollisionDlgProps extends IUploadCollisionDlgData {
    conflictIndex: number;
    setConflictIndex: (index: number) => void;
}

export let showUploadCollisionDialog: () => void = () => {
    console.error("showUploadCollisionDialog is not set up yet.");
};

export const UploadCollisionDlg: React.FunctionComponent<IUploadCollisionDlgProps> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);
    showUploadCollisionDialog = showDialog;

    enum RadioState {
        Indeterminate,
        Same,
        Different
    }

    const [buttonState, setButtonState] = useState<RadioState>(
        RadioState.Indeterminate
    );

    const [doChangeUploader, setDoChangeUploader] = useState(false);

    const kAskForHelpColor = "#D65649";
    const kDarkerSecondaryTextColor = "#555555";

    const canUpload = props.permissions?.reupload ?? false;
    const canBecomeUploader = props.permissions?.becomeUploader ?? false;

    const sameBook = useL10n(
        "Is this an update of your existing book?",
        "PublishTab.UploadCollisionDialog.SameBook",
        "This is the dialog title"
    );
    const sameBookNotMine = useL10n(
        "Is this an update of an existing book?",
        "PublishTab.UploadCollisionDialog.SameBookNotMine",
        "This is the dialog title when this user is not allowed to upload"
    );
    const mainTitle = canUpload ? sameBook : sameBookNotMine;

    const bloomLibraryHasOne = useL10n(
        "BloomLibrary.org already has a book with this ID.",
        "PublishTab.UploadCollisionDialog.HaveOne2",
        "This is the dialog subtitle."
    );

    const bloomLibraryHasMany = useL10n(
        "BloomLibrary.org already has {0} books that have the same ID as this one.",
        "PublishTab.UploadCollisionDialog.HaveMany",
        "This is the dialog subtitle.",
        props.count.toString()
    );

    const subtitle =
        props.count === 1 ? bloomLibraryHasOne : bloomLibraryHasMany;

    const existingCardHeader = useL10n(
        "Already in Bloom Library",
        "PublishTab.UploadCollisionDialog.AlreadyIn",
        "This is the header for the book that is in bloomlibrary.org already."
    );

    const uploadingCardHeader = useL10n("Uploading", "Common.Uploading");

    const differentBooksCommentary = useL10n(
        "Bloom will fix the ID of your book and upload it as a new book. The old book on Bloom Library will stay the same.",
        "PublishTab.UploadCollisionDialog.Radio.DifferentBooks.Commentary2",
        "This is explanatory commentary on a radio button."
    );

    const sameBookRadioLabel = useL10n(
        "Yes, I want to update this book",
        "PublishTab.UploadCollisionDialog.Radio.SameBook2",
        "This is the label on a radio button."
    );

    const sameBookCancelRadioLabel = useL10n(
        "Yes, these are the same book -- Cancel upload for now",
        "PublishTab.UploadCollisionDialog.Radio.CancelUpload",
        "This is the label on a radio button."
    );

    const differentBooksRadioLabel = useL10n(
        "No, these are different books",
        "PublishTab.UploadCollisionDialog.Radio.DifferentBooks",
        "This is the label on a radio button."
    );

    const changeTheUploader = useL10n(
        "Change the official uploader to {0}. (Bloom Library will hide part of your email address)",
        "PublishTab.UploadCollisionDialog.ChangeUploader",
        undefined,
        props.userEmail
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
                    text-decoration: underline;
                }
            `}
        >
            <Typography color="textSecondary">
                {canUpload ? (
                    <TextWithEmbeddedLink
                        className="embeddedLink"
                        l10nKey="PublishTab.UploadCollisionDialog.Radio.SameBook2.Commentary"
                        l10nComment="This is explanatory commentary on a radio button. Don't translate the website reference in brackets ([bloomlibrary.org]). It will be replaced by a link to bloomlibrary.org."
                        href="https://www.bloomlibrary.org"
                    >
                        Bloom will remove the version on [bloomlibrary.org] and
                        replace it with your upload.
                    </TextWithEmbeddedLink>
                ) : (
                    <TextWithEmbeddedLink
                        className="embeddedLink"
                        l10nKey="PublishTab.UploadCollisionDialog.Radio.SameBookCancel.Commentary"
                        l10nComment="This is explanatory commentary on a radio button. Don't translate the email in brackets ([librarian@bloomlibrary.org]). It will be replaced by a link to send an email."
                        href="mailto:librarian@bloomlibrary.org"
                    >
                        If your organization has an Enterprise Subscription, you
                        may ask [librarian@bloomlibrary.org] to connect your
                        account to your organization's bookshelves. Then you
                        will be able to update books on those bookshelves.
                    </TextWithEmbeddedLink>
                )}
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

    const whatCausedThisStyles: React.CSSProperties = {
        fontSize: "smaller",
        textDecoration: "underline"
    };

    function cancel() {
        props.onCancel?.();
        closeDialog();
    }

    return (
        <BloomDialog
            onCancel={() => {
                cancel();
            }}
            {...propsForBloomDialog}
            maxWidth={false}
        >
            <div
                css={css`
                    flex-direction: column;
                `}
            >
                <DialogTitle
                    title={mainTitle}
                    icon={
                        canUpload ? (
                            "/bloom/publish/LibraryPublish/BookIdCollision.svg"
                        ) : (
                            <WarningIcon color="warning" />
                        )
                    }
                ></DialogTitle>
                <div
                    // This ugliness aligns the subtitle with the title, handling the different icon sizes.
                    // If this happens at all often, we should probably build the subtitle into the title
                    // component so it can share a parent with the title text.
                    css={css`
                        margin-left: ${canUpload ? "45px" : "31px"};
                        margin-top: -20px;
                    `}
                >
                    <Typography color="textSecondary">{subtitle}</Typography>
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
                        margin-left: 15px;
                        margin-top: 20px;
                    `}
                >
                    <div
                        css={css`
                            p {
                                margin-block-end: 0;
                            }
                        `}
                    >
                        <div
                            css={css`
                                display: flex;
                                justify-content: space-between;
                            `}
                        >
                            <Typography>{existingCardHeader}</Typography>
                            {props.count > 1 && (
                                // This chunk shows the index of the book being displayed and arrows to move to the next or previous book.
                                <div
                                    css={css`
                                        display: flex;
                                        margin-right: 10px;
                                    `}
                                >
                                    <div
                                        // I can't find a Material UI icon that is the sort of left arrow JohnH asked for,
                                        // so rotate the right one and tweak the position.
                                        css={css`
                                            transform: rotate(180deg);
                                            padding-top: 4px;
                                            margin-top: -4px;
                                            // A crude way of making it look disabled when it is.
                                            // I tried wrapping in IconButton, which has a disabled property, but there was no visual change.
                                            // There ought to be some component that would give it a 'standard' Bloom disabled look,
                                            // but I can't find it, and it doesn't seem worth creating for a low-priority control we hope
                                            // most users will never see.
                                            opacity: ${props.conflictIndex > 0
                                                ? 1
                                                : 0.4};
                                        `}
                                    >
                                        <RightIcon
                                            color="primary"
                                            onClick={() => {
                                                if (props.conflictIndex > 0)
                                                    props.setConflictIndex(
                                                        props.conflictIndex - 1
                                                    );
                                            }}
                                        />
                                    </div>
                                    <div
                                        css={css`
                                            margin-top: 2px;
                                        `}
                                    >
                                        {props.conflictIndex + 1}
                                    </div>
                                    <div
                                        css={css`
                                            opacity: ${props.conflictIndex <
                                            props.count - 1
                                                ? 1
                                                : 0.4};
                                        `}
                                    >
                                        <RightIcon
                                            color="primary"
                                            onClick={() => {
                                                if (
                                                    props.conflictIndex <
                                                    props.count - 1
                                                )
                                                    props.setConflictIndex(
                                                        props.conflictIndex + 1
                                                    );
                                            }}
                                        />
                                    </div>
                                </div>
                            )}
                        </div>
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
                            uploadedBy={props.uploader}
                            userEmail={props.userEmail}
                            canUpload={canUpload}
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
                            languages={props.newLanguages ?? [""]}
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
                        margin-left: 15px;
                        margin-top: 10px;
                        width: 560px;
                    `}
                >
                    <div
                        css={css`
                            p {
                                margin-block-end: 0;
                            }
                        `}
                    >
                        <RadioWithLabelAndCommentary
                            buttonStateToMatch={RadioState.Same}
                            radioValue="Same"
                            radioLabel={
                                canUpload
                                    ? sameBookRadioLabel
                                    : sameBookCancelRadioLabel
                            }
                            ariaLabel="Same book radio button"
                            commentaryChildren={sameBookRadioCommentary()}
                        />
                        {canUpload &&
                            canBecomeUploader &&
                            props.uploader !== props.userEmail && (
                                <div
                                    css={css`
                                        margin-left: 37px;
                                        margin-top: -12px;
                                    `}
                                >
                                    <BloomCheckbox
                                        label={changeTheUploader}
                                        alreadyLocalized={true}
                                        l10nKey="ignored"
                                        checked={doChangeUploader}
                                        onCheckChanged={() => {
                                            setDoChangeUploader(
                                                !doChangeUploader
                                            );
                                        }}
                                    ></BloomCheckbox>
                                </div>
                            )}

                        <RadioWithLabelAndCommentary
                            buttonStateToMatch={RadioState.Different}
                            radioValue="Different"
                            radioLabel={differentBooksRadioLabel}
                            ariaLabel="Different book radio button"
                            commentaryChildren={differentBooksRadioCommentary()}
                        />
                    </div>
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
                    enabled={
                        // If we don't have permission to overwrite, we can only upload using a new ID
                        buttonState !== RadioState.Indeterminate &&
                        (canUpload || buttonState === RadioState.Different)
                    }
                    size="large"
                    onClick={() => {
                        const isSameBook: boolean =
                            buttonState === RadioState.Same;
                        let command = "uploadAfterChangingBookId";
                        if (isSameBook) {
                            if (doChangeUploader) {
                                command = "uploadWithNewUploader";
                            } else {
                                command = "upload";
                            }
                        }
                        post(`libraryPublish/${command}`);
                        closeDialog();
                    }}
                >
                    Upload
                </BloomButton>
                <DialogCancelButton
                    // Something is preventing onCancel on BloomDialog from working in
                    // this case. I'm not sure if it has to do with this dialog
                    // being in a separate browser... that's my theory since it
                    // works in storybook. For now, I'm just restoring the functionality
                    // by putting the deprecated onClick back.
                    onClick_DEPRECATED={() => {
                        cancel();
                    }}
                ></DialogCancelButton>
            </DialogBottomButtons>
        </BloomDialog>
    );
};

WireUpForWinforms(UploadCollisionDlg);
