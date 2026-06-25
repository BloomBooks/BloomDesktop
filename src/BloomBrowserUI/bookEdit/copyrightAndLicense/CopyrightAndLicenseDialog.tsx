import { css } from "@emotion/react";

import * as React from "react";
import { useState } from "react";
import { renderRootSync } from "../../utils/reactRender";
import { Tab, TabList, TabPanel } from "react-tabs";
import "react-tabs/style/react-tabs.less";
import { CircularProgress } from "@mui/material";
import CheckCircleIcon from "@mui/icons-material/CheckCircle";

import { WireUpForWinforms } from "../../utils/WireUpWinform";
import { get, postBoolean, postData } from "../../utils/bloomApi";
import { useSubscribeToWebSocketForEvent } from "../../utils/WebSocketManager";
import { kBloomBlue, kMutedTextGray } from "../../bloomMaterialUITheme";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogTitle,
    DialogMiddle,
} from "../../react_components/BloomDialog/BloomDialog";
import BloomButton from "../../react_components/bloomButton";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog,
} from "../../react_components/BloomDialog/BloomDialogPlumbing";
import {
    DialogCancelButton,
    DialogOkButton,
} from "../../react_components/BloomDialog/commonDialogComponents";
import { BloomTabs } from "../../react_components/BloomTabs";
import { LocalizedString } from "../../react_components/l10nComponents";
import { useL10n } from "../../react_components/l10nHooks";
import { CopyrightPanel, ICopyrightInfo } from "./CopyrightPanel";
import { ILicenseInfo, LicensePanel } from "./LicensePanel";
import { LicenseBadge } from "./LicenseBadge";
import { MetadataChooser } from "./MetadataChooser";
import BloomMessageBoxSupport from "../../utils/bloomMessageBoxSupport";

export interface ICopyrightAndLicenseData {
    derivativeInfo?: IDerivativeInfo;
    copyrightInfo: ICopyrightInfo;
    licenseInfo: ILicenseInfo;
}

export interface IDerivativeInfo {
    isBookDerivative: boolean;
    useOriginalCopyright: boolean;
    originalCopyrightAndLicenseText?: string;
    originalCopyrightYear?: string;
    originalCopyrightHolder?: string;
    originalLicense?: ILicenseInfo;
}

// This is currently launched from both js-world and C#.
// So props changes need to be reflected in C#, too.
export const CopyrightAndLicenseDialog: React.FunctionComponent<{
    isForBook: boolean; // or image
    data: ICopyrightAndLicenseData;
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = (props) => {
    const { showDialog, closeDialog, propsForBloomDialog } =
        useSetupBloomDialog(props.dialogEnvironment);

    // Configure the local function (`show`) for showing the dialog to be the one derived from useSetupBloomDialog (`showDialog`)
    // which allows js launchers of the dialog to make it visible (by calling showCopyrightAndLicenseInfoOrDialog)
    show = showDialog;

    const dialogTitle = useL10n("Copyright and License", "CopyrightAndLicense");

    // Tell edit tab to disable everything when the dialog is up.
    // (Without this, the page list is not disabled since the modal
    // div only exists in the book pane. Once the whole edit tab is inside
    // one browser, this would not be necessary.)
    React.useEffect(() => {
        if (propsForBloomDialog.open === undefined) return;

        postBoolean("editView/setModalState", propsForBloomDialog.open);
    }, [propsForBloomDialog.open]);

    const [useOriginalCopyrightAndLicense, setUseOriginalCopyrightAndLicense] =
        useState(
            !!props.data.derivativeInfo &&
                props.data.derivativeInfo.useOriginalCopyright,
        );
    const [copyrightInfo, setCopyrightInfo] = useState(
        props.data.copyrightInfo,
    );
    const [licenseInfo, setLicenseInfo] = useState(props.data.licenseInfo);

    const [isCopyrightValid, setIsCopyrightValid] = useState(false);
    const [isLicenseValid, setIsLicenseValid] = useState(true);

    // The single condition that gates saving. The OK button is enabled only when this is true;
    // the "Add to all images" and "copy" buttons are hidden entirely when it is false (rather
    // than shown disabled). Anything keyed to "can we save?" should use this, not the OK button.
    const canSave = isCopyrightValid && isLicenseValid;

    // The CopyrightPanel and LicensePanel seed their internal state from props only at mount.
    // To refill their fields when the user picks a metadata package, we update the data state
    // here and bump this version, which is used as the panels' `key` to force a remount.
    const [appliedVersion, setAppliedVersion] = useState(0);

    function applyMetadataPackage(data: ICopyrightAndLicenseData) {
        setCopyrightInfo(data.copyrightInfo);
        setLicenseInfo(data.licenseInfo);
        setAppliedVersion((v) => v + 1);
    }

    // Header for the image reuse chooser.
    const ReuseMetadataShortcutLabelHeader = useL10n(
        "Shortcuts:",
        "CopyrightAndLicense.ReuseMetadataShortcutLabel",
    );

    // Tracks the "Add this info to all images" operation. It can take a while on a book with
    // many images, so instead of closing the dialog (and leaving the UI seemingly frozen) we
    // stay open and show progress: a spinner while working, then a "done" confirmation.
    const [pushState, setPushState] = useState<"idle" | "working" | "done">(
        "idle",
    );
    const pushWorkingLabel = useL10n("Working…", "Common.Working");
    const pushDoneLabel = useL10n("Done", "Common.Done");

    // Editing makes a prior "pushed to all images" confirmation stale, so offer the button
    // again. But if a push is still running ("working"), leave the spinner alone: resetting to
    // "idle" mid-operation would hide the spinner and re-enable the button for redundant clicks.
    function markEditedSincePush() {
        setPushState((prev) => (prev === "working" ? prev : "idle"));
    }

    function onCopyrightChange(
        copyrightInfo: ICopyrightInfo,
        useOriginalCopyrightAndLicense: boolean,
        isValid: boolean,
    ) {
        setCopyrightInfo(copyrightInfo);
        setUseOriginalCopyrightAndLicense(useOriginalCopyrightAndLicense);
        setIsCopyrightValid(isValid);
        markEditedSincePush();
    }

    function onLicenseChange(licenseInfo: ILicenseInfo, isValid: boolean) {
        setLicenseInfo(licenseInfo);
        setIsLicenseValid(isValid);
        markEditedSincePush();
    }

    // The current dialog values, in the shape the backend expects.
    function gatherData(): ICopyrightAndLicenseData {
        const derivativeInfo: IDerivativeInfo = {
            isBookDerivative:
                !!props.data.derivativeInfo &&
                props.data.derivativeInfo.isBookDerivative,
            useOriginalCopyright: useOriginalCopyrightAndLicense,
        };
        return {
            copyrightInfo,
            licenseInfo,
            derivativeInfo,
        };
    }

    function handleOk() {
        // Save just the current image/book and close.
        postData(getApiUrlSuffix(props.isForBook), gatherData());
        closeDialog();
    }

    // Push the current metadata to every other image in the book. This can be slow, so we keep
    // the dialog open and show a "Working…" spinner. We do NOT switch to "done" when the POST
    // resolves — that happens as soon as the save is *initiated*, before the copy actually runs.
    // Instead the backend sends a websocket event (see useSubscribeToWebSocketForEvent below)
    // when the copy has finished, and that flips us to "done".
    function handlePushToAllImages() {
        setPushState("working");
        postData(
            getApiUrlSuffix(props.isForBook) + "?applyToAllImages=true",
            gatherData(),
            undefined,
            // If the POST itself fails before reaching the server, no "pushedToAllImages"
            // websocket event will ever arrive, so reset to "idle" rather than leaving the
            // spinner running forever.
            () => setPushState("idle"),
        );
    }

    // The backend fires this when a "Add to all images" operation has actually finished.
    useSubscribeToWebSocketForEvent(
        "copyrightAndLicense",
        "pushedToAllImages",
        () => setPushState("done"),
    );

    return (
        <BloomDialog {...propsForBloomDialog}>
            <DialogTitle
                title={dialogTitle}
                css={css`
                    padding-bottom: 0;
                    margin-bottom: 0;
                `}
            />
            {
                // This absolutely positioned div will appear to the right of the title text
                licenseInfo && (
                    <div
                        css={css`
                            position: absolute;
                            top: 20px;
                            right: 20px;
                            z-index: 1; // Otherwise, the DialogMiddle can end up on top of it
                        `}
                    >
                        <LicenseBadge
                            licenseInfo={
                                useOriginalCopyrightAndLicense
                                    ? props.data.derivativeInfo!
                                          .originalLicense!
                                    : licenseInfo
                            }
                            onChange={(newLicenseInfo: ILicenseInfo) => {
                                setLicenseInfo(newLicenseInfo);
                            }}
                            disabled={useOriginalCopyrightAndLicense}
                        />
                    </div>
                )
            }
            <DialogMiddle
                css={css`
                    width: ${props.dialogEnvironment
                        ?.dialogFrameProvidedExternally
                        ? "100%"
                        : "500px"};
                    height: 575px;
                `}
            >
                <BloomTabs
                    defaultIndex={0}
                    color="black"
                    selectedColor={kBloomBlue}
                    labelBackgroundColor="white"
                    css={css`
                        .react-tabs__tab-panel--selected > div {
                            padding: 0; // This cancels a rule in BloomUI.less which is too global
                            padding-top: 20px;
                        }
                    `}
                >
                    <TabList>
                        <Tab>
                            <LocalizedString l10nKey="Common.Copyright">
                                Copyright
                            </LocalizedString>
                        </Tab>
                        <Tab disabled={useOriginalCopyrightAndLicense}>
                            <LocalizedString l10nKey="Common.License">
                                License
                            </LocalizedString>
                        </Tab>
                    </TabList>
                    <TabPanel>
                        {copyrightInfo && (
                            <div
                                css={css`
                                    display: flex;
                                    flex-direction: column;
                                    // Fill the tab so the chooser can hug the bottom (it uses
                                    // margin-top: auto) while the copyright fields stay at top.
                                    height: 100%;
                                    box-sizing: border-box;
                                `}
                            >
                                <CopyrightPanel
                                    key={appliedVersion}
                                    isForBook={props.isForBook}
                                    derivativeInfo={props.data.derivativeInfo}
                                    copyrightInfo={copyrightInfo}
                                    onChange={(
                                        copyrightInfo,
                                        useOriginalCopyrightAndLicense,
                                        isValid,
                                    ) =>
                                        onCopyrightChange(
                                            copyrightInfo,
                                            useOriginalCopyrightAndLicense,
                                            isValid,
                                        )
                                    }
                                />
                                {
                                    // The image-only "Add to all images" button, sitting just
                                    // below the copyright fields. Shown only for images, and only
                                    // when the metadata can be saved (or a push is in progress/done).
                                    !props.isForBook &&
                                        (canSave || pushState !== "idle") && (
                                            <div
                                                css={css`
                                                    display: flex;
                                                    align-items: center;
                                                    // Right-align the button/progress.
                                                    justify-content: flex-end;
                                                    min-height: 36px;
                                                `}
                                            >
                                                {pushState === "idle" && (
                                                    <BloomButton
                                                        l10nKey="CopyrightAndLicense.CopyToAllImages"
                                                        hasText={true}
                                                        enabled={true}
                                                        variant="outlined"
                                                        css={css`
                                                            // Outlined button.
                                                            text-transform: none;
                                                        `}
                                                        onClick={
                                                            handlePushToAllImages
                                                        }
                                                    >
                                                        Add this info to all
                                                        images in this book
                                                    </BloomButton>
                                                )}
                                                {pushState === "working" && (
                                                    <div
                                                        css={css`
                                                            display: flex;
                                                            align-items: center;
                                                            gap: 8px;
                                                            color: ${kMutedTextGray};
                                                        `}
                                                    >
                                                        <CircularProgress
                                                            size={16}
                                                        />
                                                        {pushWorkingLabel}
                                                    </div>
                                                )}
                                                {pushState === "done" && (
                                                    <div
                                                        css={css`
                                                            display: flex;
                                                            align-items: center;
                                                            gap: 6px;
                                                        `}
                                                    >
                                                        <CheckCircleIcon
                                                            fontSize="small"
                                                            css={css`
                                                                color: #4caf50;
                                                            `}
                                                        />
                                                        {pushDoneLabel}
                                                    </div>
                                                )}
                                            </div>
                                        )
                                }
                                {
                                    // A bottom-hugging group: the reuse chooser, shown only when
                                    // editing an image (not for the book's own copyright/license).
                                    // It is a sibling of (not keyed like) CopyrightPanel, so
                                    // applying a package remounts the panel to refill its fields
                                    // without resetting the chooser. (copyrightInfo is already
                                    // guaranteed truthy by the enclosing block.)
                                    !props.isForBook && licenseInfo && (
                                        <div
                                            css={css`
                                                margin-top: auto;
                                                display: flex;
                                                flex-direction: column;
                                            `}
                                        >
                                            <MetadataChooser
                                                currentData={{
                                                    copyrightInfo,
                                                    licenseInfo,
                                                }}
                                                onChoose={applyMetadataPackage}
                                                headerText={
                                                    ReuseMetadataShortcutLabelHeader
                                                }
                                                listEndpoint="copyrightAndLicense/imageFileNamesInBook"
                                                getItemEndpoint={(file) =>
                                                    "copyrightAndLicense/imageMetadataForFile?fileName=" +
                                                    encodeURIComponent(file)
                                                }
                                                alsoOfferBookMetadata={true}
                                            />
                                        </div>
                                    )
                                }
                            </div>
                        )}
                    </TabPanel>
                    <TabPanel>
                        {licenseInfo && (
                            <LicensePanel
                                key={appliedVersion}
                                isForBook={props.isForBook}
                                licenseInfo={licenseInfo}
                                derivativeInfo={props.data.derivativeInfo}
                                onChange={(licenseInfo, isValid) =>
                                    onLicenseChange(licenseInfo, isValid)
                                }
                            />
                        )}
                    </TabPanel>
                </BloomTabs>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton
                    onClick={handleOk}
                    default={true}
                    // Disable OK while a push is in flight, so the user can't fire a
                    // redundant save and unmount the dialog before the "pushedToAllImages"
                    // websocket event arrives (which would set state on an unmounted component).
                    enabled={canSave && pushState !== "working"}
                />
                <DialogCancelButton onClick_DEPRECATED={closeDialog} />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

WireUpForWinforms(CopyrightAndLicenseDialog);

let show: () => void = () => {
    window.alert("CopyrightAndLicenseDialog is not set up yet.");
};

function showCopyrightAndLicenseDialog(
    isForBook: boolean,
    data: ICopyrightAndLicenseData,
) {
    try {
        renderRootSync(
            <CopyrightAndLicenseDialog isForBook={isForBook} data={data} />,
            getModalContainer(),
        );
    } catch (error) {
        console.error(error);
    }
    show();
}

// The `get` call either returns the image's copyright/license data (so we display the dialog),
// returns nothing (e.g. the image file wasn't found, so there is nothing to show), or fails with
// a message we surface to the user (e.g. the image is embedded data rather than a file, so its
// information can't be edited).
export function showCopyrightAndLicenseInfoOrDialog(imageUrl?: string) {
    const isForBook: boolean = !imageUrl;
    get(
        // We don't uri-encode the imageUrl because we are getting it from the html tag (and therefore its already encoded).
        getApiUrlSuffix(isForBook) + (imageUrl ? `?imageUrl=${imageUrl}` : ""),
        (result) => {
            if (result.data) {
                showCopyrightAndLicenseDialog(isForBook, result.data);
            }
        },
        (err) => {
            const responseData = err.response?.data;
            const serverMessage =
                (typeof responseData === "string" ? responseData : undefined) ||
                err.response?.statusText;
            const message =
                serverMessage ||
                "Bloom could not open image copyright and license information.";
            BloomMessageBoxSupport.CreateAndShowSimpleMessageBoxWithLocalizedText(
                message,
            );
            console.error(err);
        },
    );
}

function getApiUrlSuffix(isForBook: boolean): string {
    return isForBook
        ? "copyrightAndLicense/bookCopyrightAndLicense"
        : "copyrightAndLicense/imageCopyrightAndLicense";
}

// It would be simpler to just use getWorkspaceBundleExports().getModalDialogContainer()
// but we were getting strange interactions between this component and others which use that container.
// We were also having trouble rendering this component more than once for two different book pages.
// So we just always use our own, new, unique container.
function getModalContainer(): HTMLElement {
    let modalDialogContainer = document.getElementById(
        "CopyrightAndLicenseDialogContainer",
    );
    if (modalDialogContainer) {
        modalDialogContainer.remove();
    }
    modalDialogContainer = document.createElement("div");
    modalDialogContainer.id = "CopyrightAndLicenseDialogContainer";
    document.body.appendChild(modalDialogContainer);
    return modalDialogContainer;
}
