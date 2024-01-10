/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { useEffect, useRef, useState } from "react";
import { TextField, Step, StepLabel, StepContent } from "@mui/material";

import {
    get,
    getBloomApiPrefix,
    getBoolean,
    post,
    postBoolean,
    postString
} from "../../utils/bloomApi";
import { kBloomDisabledOpacity } from "../../utils/colorUtils";
import { BloomStepper } from "../../react_components/BloomStepper";
import { Div, Span } from "../../react_components/l10nComponents";
import BloomButton from "../../react_components/bloomButton";
import { PWithLink } from "../../react_components/pWithLink";
import {
    ProgressBox,
    ProgressBoxHandle
} from "../../react_components/Progress/progressBox";
import { BloomCheckbox } from "../../react_components/BloomCheckBox";
import { useL10n } from "../../react_components/l10nHooks";
import { kWebSocketContext } from "./LibraryPublishScreen";
import {
    useSubscribeToWebSocketForEvent,
    useSubscribeToWebSocketForObject,
    useSubscribeToWebSocketForStringMessage
} from "../../utils/WebSocketManager";
import { Link } from "../../react_components/link";
import {
    DialogResult,
    ConfirmDialog,
    showConfirmDialog
} from "../../react_components/confirmDialog";
import { BloomSplitButton } from "../../react_components/bloomSplitButton";
import { ErrorBox, WaitBox } from "../../react_components/boxes";
import {
    IUploadCollisionDlgProps,
    showUploadCollisionDialog,
    UploadCollisionDlg
} from "./uploadCollisionDlg";
import { showCopyrightAndLicenseInfoOrDialog } from "../../bookEdit/copyrightAndLicense/CopyrightAndLicenseDialog";
import { useGetEnterpriseBookshelves } from "../../collection/useGetEnterpriseBookshelves";
import { MustBeCheckedOut } from "../../react_components/MustBeCheckedOut";
import { SelectedBookContext } from "../../app/SelectedBookContext";

interface IReadonlyBookInfo {
    title: string;
    copyright: string;
    license: string;
    licenseType: string;
    licenseToken: string;
    licenseRights: string;
    isTemplate: boolean;
    isTitleOKToPublish: boolean; // This will be false if there is no L1 title, unless we don't need languages.
}

const kWebSocketEventId_uploadSuccessful: string = "uploadSuccessful";
const kWebSocketEventId_uploadCanceled: string = "uploadCanceled";
const kWebSocketEventId_loginSuccessful: string = "loginSuccessful";

export const LibraryPublishSteps: React.FunctionComponent = () => {
    const selectedBookContext = React.useContext(SelectedBookContext);
    const [bookshelfHasProblem, setBookshelfHasProblem] = useState(false);
    const {
        project,
        defaultBookshelfUrlKey,
        validBookshelves,
        error: serverError
    } = useGetEnterpriseBookshelves();

    useEffect(() => {
        if (serverError) {
            return;
        } else {
            if (
                project !== "" &&
                project !== "local-community" &&
                defaultBookshelfUrlKey !== ""
            ) {
                setBookshelfHasProblem(
                    validBookshelves.filter(
                        b => b.value === defaultBookshelfUrlKey
                    ).length === 0
                );
            }
        }
    }, [project, defaultBookshelfUrlKey, validBookshelves, serverError]);

    const localizedSummary = useL10n("Summary", "PublishTab.Upload.Summary");
    const localizedAllRightsReserved = useL10n(
        "All rights reserved (Contact the Copyright holder for any permissions.)",
        "PublishTab.Upload.AllReserved"
    );
    const localizedSuggestChangeCC = useL10n(
        "Suggestion: Creative Commons Licenses make it much easier for others to use your book, even if they aren't fluent in the language of your custom license.",
        "PublishTab.Upload.SuggestChangeCC"
    );
    const localizedSuggestAssignCC = useL10n(
        "Suggestion: Assigning a Creative Commons License makes it easy for you to clearly grant certain permissions to everyone.",
        "PublishTab.Upload.SuggestAssignCC"
    );
    const localizedUploadBook = useL10n(
        "Upload Book",
        "PublishTab.Upload.UploadButton"
    );
    const localizedUploadCollection = useL10n(
        "Upload this Collection",
        "PublishTab.Upload.UploadCollection"
    );
    const localizedUploadFolder = useL10n(
        "Upload Folder of Collections",
        "PublishTab.Upload.UploadFolder"
    );
    const localizedEnterpriseTooltip = useL10n(
        "This feature requires an Enterprise subscription and a destination shelf selected in Collection Settings.",
        "PublishTab.Upload.EnterpriseShelfRequiredTooltip"
    );

    const [reload, setReload] = useState<number>(0);

    const progressBoxRef = useRef<ProgressBoxHandle>(null);

    const [isLoading, setIsLoading] = useState<boolean>(true);
    const [bookInfo, setBookInfo] = useState<IReadonlyBookInfo>();
    useEffect(() => {
        post("libraryPublish/checkForLoggedInUser");
        getBoolean("libraryPublish/agreementsAccepted", result => {
            setAgreedPreviously(result);
            setAgreementsAccepted(result);
        });
        get("libraryPublish/getBookInfo", result => {
            setBookInfo(result.data);
            setSummary(result.data.summary);
            setIsLoading(false);
        });
    }, [reload]);
    useSubscribeToWebSocketForStringMessage(
        "bookCopyrightAndLicense",
        "saved",
        () => {
            setReload(reload => reload + 1);
        }
    );

    const [useSandbox, setUseSandbox] = useState<boolean>(false);
    const [uploadButtonText, setUploadButtonText] = useState<string>(
        localizedUploadBook
    );
    useEffect(() => {
        getBoolean("libraryPublish/useSandbox", setUseSandbox);
    }, []);
    useEffect(() => {
        setUploadButtonText(
            localizedUploadBook +
                (useSandbox ? " (to dev.bloomlibrary.org)" : "")
        );
    }, [useSandbox, localizedUploadBook]);

    const [summary, setSummary] = useState<string>("");
    useEffect(() => {
        if (bookInfo) postString("libraryPublish/setSummary", summary);
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [summary]); // purposefully not including bookInfo, so we don't post on initial load

    function isReadyForAgreements(): boolean {
        return (
            !!bookInfo?.title && (!!bookInfo?.copyright || bookInfo?.isTemplate)
        );
    }
    const [agreedPreviously, setAgreedPreviously] = useState<boolean>(false);
    const [agreementsAccepted, setAgreementsAccepted] = useState<boolean>(
        false
    );
    // This useRef silliness is to prevent the postBoolean from happening on the initial render.
    const hasRenderedRef = useRef(false);
    useEffect(() => {
        if (hasRenderedRef.current)
            postBoolean(
                "libraryPublish/agreementsAccepted",
                agreementsAccepted
            );
        else hasRenderedRef.current = true;
    }, [agreementsAccepted]);

    const [loggedInEmail, setLoggedInEmail] = useState<string>();

    useSubscribeToWebSocketForStringMessage(
        kWebSocketContext,
        kWebSocketEventId_loginSuccessful,
        email => {
            setLoggedInEmail(email);
        }
    );

    function isReadyForUpload(): boolean {
        return isReadyForAgreements() && agreementsAccepted;
    }

    function confirmWithUserIfNecessaryAndUpload() {
        if (bookInfo?.isTemplate) {
            showConfirmDialog();
        } else {
            uploadOneBook();
        }
    }

    const [uploadCollisionInfo, setUploadCollisionInfo] = useState<
        IUploadCollisionDlgProps
    >({
        userEmail: "",
        newTitle: "",
        existingTitle: "",
        existingCreatedDate: "",
        existingUpdatedDate: "",
        existingBookUrl: ""
    });

    const [isUploading, setIsUploading] = useState<boolean>(false);
    function uploadOneBook() {
        setIsUploadComplete(false);
        setIsUploading(true);
        get("libraryPublish/getUploadCollisionInfo", result => {
            if (result.data.error) {
                // The API already sent an error message
                return;
            }
            if (result.data.shouldShow) {
                setUploadCollisionInfo(result.data);
                showUploadCollisionDialog();
            } else post("libraryPublish/upload");
        });
    }

    const [isCanceling, setIsCanceling] = useState<boolean>(false);
    useSubscribeToWebSocketForEvent(
        kWebSocketContext,
        kWebSocketEventId_uploadCanceled,
        () => {
            setIsCanceling(false);
        }
    );

    function bulkUploadCollection() {
        post("libraryPublish/uploadCollection");
    }
    function bulkUploadFolderOfCollections() {
        // Nothing to do either on success or failure, including possible timeout,
        // or the user canceling. This is because the "result" comes back
        // via a websocket that sets the new result (just below). This approach is needed because otherwise
        // the browser would time out while waiting for the user to finish using the system folder-choosing dialog.
        post("fileIO/chooseFolder");
    }
    useSubscribeToWebSocketForObject<{ success: boolean; path: string }>(
        "fileIO",
        "chooseFolder-results",
        results => {
            if (results.success) {
                postString(
                    "libraryPublish/uploadFolderOfCollections",
                    results.path
                );
            }
        }
    );

    const lastElementOnPageRef = React.useRef<HTMLDivElement>(null);
    const [bookUrl, setBookUrl] = useState<string>("");

    // When C# finishes the upload, it calls this.
    useSubscribeToWebSocketForObject<{ bookId: string; url: string }>(
        kWebSocketContext,
        kWebSocketEventId_uploadSuccessful,
        results => {
            setIsUploading(false);
            setBookUrl(results.url);
            setIsUploadComplete(true);
        }
    );

    const [isUploadComplete, setIsUploadComplete] = useState<boolean>(false);
    useEffect(() => {
        // We want to scroll to the end when upload is complete so the user notices
        // the final step which contains the link to the book.
        // The 300ms timeout allows time for the transition which slides the step open to complete.
        // As far as I can tell, mui's Collapse component is used, where they seem to default to 300ms and then
        // try to calculate a more accurate time based on the height of the content.
        // As of now, it is setting it to 261ms. But that may change based on UI changes in the future
        // or even localization. Even if 300ms becomes not quite enough, it isn't a show-stopper.
        // At least most of the step will be shown.
        window.setTimeout(() => {
            if (isUploadComplete) {
                lastElementOnPageRef?.current?.scrollIntoView({
                    behavior: "smooth",
                    block: "start"
                });
            }
        }, 300);
    }, [isUploadComplete]);

    const [licenseBlock, setLicenseBlock] = useState<JSX.Element>(
        <React.Fragment />
    );
    useEffect(() => {
        switch (bookInfo?.licenseType) {
            case "CreativeCommons":
                setLicenseBlock(
                    <img
                        src={`${getBloomApiPrefix()}copyrightAndLicense/ccImage?token=${bookInfo?.licenseToken?.toLowerCase()}`}
                        css={css`
                            width: 100px;
                        `}
                    />
                );
                break;
            case "Null":
                setLicenseBlock(
                    <div>
                        <div>{localizedAllRightsReserved}</div>
                        <WarningMessage>
                            {localizedSuggestAssignCC}
                        </WarningMessage>
                    </div>
                );
                break;
            case "Custom":
                setLicenseBlock(
                    <div>
                        <div>{bookInfo?.licenseRights}</div>
                        <WarningMessage>
                            {localizedSuggestChangeCC}
                        </WarningMessage>
                    </div>
                );
                break;
        }
    }, [
        bookInfo,
        localizedAllRightsReserved,
        localizedSuggestAssignCC,
        localizedSuggestChangeCC
    ]);

    const serverErrorBox = serverError && (
        <ErrorBox
            l10Msg="Bloom could not reach the server to get the list of bookshelves."
            l10nKey="CollectionSettingsDialog.BookMakingTab.NoBookshelvesFromServer"
        ></ErrorBox>
    );
    const bookshelfErrorBox = bookshelfHasProblem && (
        <ErrorBox
            l10Msg="The collection's bookshelf was not on the list of bookshelves for this Enterprise subscription."
            l10nKey="PublishTab.Upload.BookshelfError"
        />
    );

    const uploadButton = (
        <BloomSplitButton
            disabled={
                isCanceling ||
                !isReadyForUpload() ||
                !loggedInEmail ||
                // If 'error', there's probably an internet problem that will
                // hinder upload anyway.
                // If 'bookshelfHasProblem', the collection settings have a
                // bookshelf that isn't acceptable according to the current
                // subscription. In both cases, we give the user a tool tip on the
                // disabled button to tell them what the problem is.
                serverError ||
                bookshelfHasProblem
            }
            options={[
                {
                    english: uploadButtonText,
                    l10nId: "already-localized",
                    onClick: () => {
                        progressBoxRef.current?.clear();
                        confirmWithUserIfNecessaryAndUpload();
                    }
                },
                {
                    english: localizedUploadCollection,
                    l10nId: "already-localized",
                    requiresEnterpriseSubscription: true,
                    enterpriseTooltipOverride: localizedEnterpriseTooltip,
                    onClick: () => {
                        progressBoxRef.current?.clear();
                        bulkUploadCollection();
                    }
                },
                {
                    english: localizedUploadFolder,
                    l10nId: "already-localized",
                    requiresEnterpriseSubscription: true,
                    enterpriseTooltipOverride: localizedEnterpriseTooltip,
                    onClick: () => {
                        progressBoxRef.current?.clear();
                        bulkUploadFolderOfCollections();
                    }
                }
            ]}
        ></BloomSplitButton>
    );

    const getTitleBlock = () => {
        if (isLoading || !bookInfo) {
            return <React.Fragment />;
        }
        if (!bookInfo.isTitleOKToPublish) {
            return (
                <MissingInfo
                    text="Missing Title"
                    l10nKey={"PublishTab.Upload.Missing.Title"}
                    onClick={() => post("libraryPublish/goToEditBookCover")}
                />
            );
        }
        return (
            <div
                css={css`
                    font-weight: bold;
                `}
            >
                {bookInfo?.title}
            </div>
        );
    };

    return (
        <React.Fragment>
            <BloomStepper orientation="vertical">
                <Step
                    active={true}
                    completed={isReadyForAgreements()}
                    key="ConfirmMetadata"
                >
                    <StepLabel>
                        <Span l10nKey="PublishTab.Upload.ConfirmMetadata">
                            Confirm Metadata
                        </Span>
                    </StepLabel>
                    <StepContent>
                        {/* The isLoading check prevents pretty bad flashing of the "missing" error boxes. */}
                        {!isLoading && (
                            <div
                                css={css`
                                    font-size: larger;
                                `}
                            >
                                {getTitleBlock()}
                                {bookInfo?.isTemplate ||
                                    (bookInfo?.copyright ? (
                                        <div>{bookInfo?.copyright}</div>
                                    ) : (
                                        <MissingInfo
                                            text="Missing Copyright"
                                            l10nKey={
                                                "PublishTab.Upload.Missing.Copyright"
                                            }
                                            onClick={
                                                showCopyrightAndLicenseInfoOrDialog
                                            }
                                        />
                                    ))}
                                {licenseBlock}
                            </div>
                        )}
                        <MustBeCheckedOut placement="bottom">
                            <TextField
                                // needed by aria for a11y
                                id="book summary"
                                value={summary}
                                onChange={e => setSummary(e.target.value)}
                                label={localizedSummary}
                                margin="normal"
                                variant="outlined"
                                InputLabelProps={{
                                    shrink: true
                                }}
                                multiline
                                rows="2"
                                aria-label="Book summary"
                                fullWidth
                                css={css`
                                    margin-top: 24px;

                                    // This is messy. MUI doesn't seem to let you easily (and correctly) change the label size.
                                    // You're supposed to be able to set a style on InputLabelProps and set fontSize, but then
                                    // the border around the textbox partially goes through it.
                                    // The way that break in the border is implemented is a "legend" which obscures the border.
                                    // The legend has the same text as the label. So we have to make the text the same size.
                                    // The original transform is translate(14px, -9px) scale(1). In order to make "larger" match,
                                    // we unscale it here -- scale(1), and as a result we have to increase the scale of the legend.
                                    .MuiInputLabel-root {
                                        color: inherit;
                                        font-weight: 500;
                                        font-size: larger;
                                        transform: translate(14px, -9px)
                                            scale(1);
                                        &.Mui-focused {
                                            color: inherit;
                                        }
                                    }
                                    legend {
                                        font-weight: 500;
                                        font-size: larger;
                                        transform: scale(1.5);
                                    }
                                `}
                                disabled={!selectedBookContext.saveable}
                            />
                        </MustBeCheckedOut>
                    </StepContent>
                </Step>
                <Step
                    active={isReadyForAgreements()}
                    completed={isReadyForUpload()}
                    key="Agreements"
                >
                    <StepLabel>
                        <Span l10nKey="PublishTab.Upload.Agreements">
                            Agreements
                        </Span>
                    </StepLabel>
                    <StepContent>
                        <Agreements
                            initiallyChecked={agreedPreviously}
                            disabled={!isReadyForAgreements()}
                            onReadyChange={setAgreementsAccepted}
                        />
                    </StepContent>
                </Step>
                <Step
                    active={isReadyForUpload()}
                    completed={isUploadComplete}
                    key="Upload"
                >
                    <StepLabel>
                        <Span l10nKey={"Common.Upload"}>Upload</Span>
                    </StepLabel>
                    <StepContent>
                        {serverErrorBox}
                        {bookshelfErrorBox}
                        <div
                            css={css`
                                display: flex;
                                justify-content: space-between;
                            `}
                        >
                            {!loggedInEmail && (
                                <BloomButton
                                    variant="contained"
                                    color="secondary"
                                    enabled={isReadyForUpload()}
                                    l10nKey="PublishTab.Upload.SignIn"
                                    onClick={() => post("libraryPublish/login")}
                                >
                                    Sign in or sign up to BloomLibrary.org
                                </BloomButton>
                            )}
                            {isUploading ? (
                                <BloomButton
                                    enabled={!isCanceling}
                                    l10nKey={"Common.Cancel"}
                                    onClick={() => {
                                        setIsCanceling(true);
                                        setIsUploading(false);
                                        post("libraryPublish/cancel");
                                    }}
                                >
                                    Cancel
                                </BloomButton>
                            ) : (
                                loggedInEmail && uploadButton
                            )}
                            {loggedInEmail && (
                                <BloomButton
                                    variant="text"
                                    enabled={isReadyForUpload()}
                                    l10nKey="PublishTab.Upload.SignOut"
                                    l10nComment="The %0 will be replaced with the email address of the user."
                                    l10nParam0={loggedInEmail}
                                    onClick={() => {
                                        post("libraryPublish/logout");
                                        setLoggedInEmail(undefined);
                                    }}
                                >
                                    Sign out (%0)
                                </BloomButton>
                            )}
                        </div>
                        <div
                            css={css`
                                margin-top: 16px;
                            `}
                        >
                            <Div l10nKey={"PublishTab.Upload.UploadProgress"}>
                                Upload Progress
                            </Div>
                            <ProgressBox
                                ref={progressBoxRef}
                                webSocketContext={kWebSocketContext}
                                onGotErrorMessage={() => {
                                    setIsUploading(false);
                                }}
                                css={css`
                                    height: 200px;
                                `}
                            ></ProgressBox>
                        </div>
                    </StepContent>
                </Step>
                <Step active={isUploadComplete} key="BloomLibrary">
                    <StepLabel>
                        <Span l10nKey="PublishTab.Upload.YourBookOnBloomLibrary">
                            Your Book on BloomLibrary.org
                        </Span>
                    </StepLabel>
                    <StepContent>
                        <BloomButton
                            href={bookUrl}
                            enabled={isUploadComplete}
                            l10nKey={"PublishTab.Upload.ViewOnBloomLibrary"}
                            css={css`
                                span {
                                    // Otherwise we get Bloom blue.
                                    // This button is different than others because using
                                    // href rather than onClick means it uses the 'a' tag.
                                    color: white;
                                }
                            `}
                        >
                            View on Bloom Library
                        </BloomButton>
                        <WaitBox
                            css={css`
                                max-width: 550px;
                                margin-top: 16px;
                            `}
                        >
                            <PWithLink
                                l10nKey={
                                    "PublishTab.Upload.YourBookOnBloomLibrary.ServerWillProcess"
                                }
                                href={bookUrl}
                                css={css`
                                    margin: 0;
                                `}
                            >
                                Our server will soon process your book into
                                various formats and add them to [your book's
                                page] on BloomLibrary.org. Check back in about
                                10 minutes. If we encounter any problems, your
                                book's page will tell you about them.
                            </PWithLink>
                        </WaitBox>
                        <div ref={lastElementOnPageRef} />
                    </StepContent>
                </Step>
            </BloomStepper>
            <ConfirmDialog
                title="Warning"
                titleL10nKey="Warning"
                message={
                    "This book seems to be a template, that is, it contains blank pages for authoring a new book " +
                    "rather than content to translate into other languages. " +
                    "If that is not what you intended, you should get expert help before uploading this book." +
                    "\n\n" +
                    "Do you want to go ahead?"
                }
                messageL10nKey="PublishTab.Upload.Template"
                confirmButtonLabel="Yes"
                confirmButtonLabelL10nKey="Common.Yes"
                cancelButtonLabel="No"
                cancelButtonLabelL10nKey="Common.No"
                onDialogClose={function(result: DialogResult): void {
                    if (result === DialogResult.Confirm) uploadOneBook();
                }}
            />
            <UploadCollisionDlg
                {...uploadCollisionInfo}
                onCancel={() => {
                    setIsUploading(false);
                }}
            />
        </React.Fragment>
    );
};

const Agreements: React.FunctionComponent<{
    initiallyChecked: boolean;
    disabled: boolean;
    onReadyChange: (v: boolean) => void;
}> = props => {
    const totalCheckboxes = 3;
    const [numChecked, setNumChecked] = useState<number>(
        props.initiallyChecked ? 3 : 0
    );
    useEffect(() => {
        props.onReadyChange(numChecked === totalCheckboxes);
    }, [numChecked]);
    function handleChange(isChecked: boolean) {
        setNumChecked(prevNumChecked =>
            isChecked ? prevNumChecked + 1 : prevNumChecked - 1
        );
    }
    return (
        <React.Fragment>
            <AgreementCheckbox
                initiallyChecked={props.initiallyChecked}
                label={
                    <React.Fragment>
                        <Span l10nKey="PublishTab.Upload.Agreement.PermissionToPublish">
                            I have permission to publish all the text and images
                            in this book.
                        </Span>{" "}
                        <Link
                            href={
                                "https://docs.bloomlibrary.org/permission-to-publish"
                            }
                            l10nKey="Common.LearnMore"
                        >
                            Learn More
                        </Link>
                    </React.Fragment>
                }
                disabled={props.disabled}
                onChange={checked => handleChange(checked)}
            />
            <AgreementCheckbox
                initiallyChecked={props.initiallyChecked}
                label={
                    <Span l10nKey={"PublishTab.Upload.Agreement.GivesCredit"}>
                        The book gives credit to the the author, translator, and
                        illustrator(s).
                    </Span>
                }
                disabled={props.disabled}
                onChange={checked => handleChange(checked)}
            />
            <AgreementCheckbox
                initiallyChecked={props.initiallyChecked}
                label={
                    <PWithLink
                        href={"https://bloomlibrary.org/terms"}
                        l10nKey={"PublishTab.Upload.Agreement.AgreeToTerms"}
                        css={css`
                            /* We don't want normal padding the browser adds, mostly so the height matches the other checkboxes. */
                            margin: 0;

                            & a {
                                text-decoration: none;
                                :hover {
                                    text-decoration: underline;
                                }
                            }
                        `}
                    >
                        I agree to the [Bloom Library Terms of Use].
                    </PWithLink>
                }
                disabled={props.disabled}
                onChange={checked => handleChange(checked)}
            />
        </React.Fragment>
    );
};

const AgreementCheckbox: React.FunctionComponent<{
    initiallyChecked: boolean;
    label: string | React.ReactNode;
    disabled: boolean;
    onChange: (v: boolean) => void;
}> = props => {
    const [isChecked, setIsChecked] = useState(props.initiallyChecked);
    function handleCheckChanged(isChecked: boolean) {
        setIsChecked(isChecked);
        props.onChange(isChecked);
    }
    return (
        <div>
            <BloomCheckbox
                label={props.label}
                checked={isChecked}
                onCheckChanged={newState => {
                    handleCheckChanged(!!newState);
                }}
                disabled={props.disabled}
                alreadyLocalized={true}
            ></BloomCheckbox>
        </div>
    );
};

const WarningMessage: React.FunctionComponent = props => {
    return (
        <div
            css={css`
                font-size: small;
                color: red;
            `}
        >
            {props.children}
        </div>
    );
};

const MissingInfo: React.FunctionComponent<{
    text: string;
    l10nKey: string;
    onClick: () => void;
}> = props => {
    const selectedBookContext = React.useContext(SelectedBookContext);
    return (
        <ErrorBox
            css={css`
                max-width: 550px;
            `}
        >
            <div>
                <Div
                    css={css`
                        font-style: italic;
                    `}
                    l10nKey={props.l10nKey}
                >
                    {props.text}
                </Div>
                <MustBeCheckedOut placement="bottom-start">
                    <Link
                        css={css`
                            text-decoration: underline;
                            opacity: ${selectedBookContext.saveable
                                ? 1
                                : kBloomDisabledOpacity};
                        `}
                        l10nKey={"PublishTab.Upload.ClickToFix"}
                        onClick={props.onClick}
                        disabled={!selectedBookContext.saveable}
                    >
                        Click to fix
                    </Link>
                </MustBeCheckedOut>
            </div>
        </ErrorBox>
    );
};
