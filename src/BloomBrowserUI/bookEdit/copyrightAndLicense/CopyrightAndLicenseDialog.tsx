import { css } from "@emotion/react";

import * as React from "react";
import { useState } from "react";
import * as ReactDOM from "react-dom";
import { Tab, TabList, TabPanel } from "react-tabs";
import "react-tabs/style/react-tabs.less";

import { WireUpForWinforms } from "../../utils/WireUpWinform";
import { get, postBoolean, postData } from "../../utils/bloomApi";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogTitle,
    DialogMiddle,
} from "../../react_components/BloomDialog/BloomDialog";
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

    function onCopyrightChange(
        copyrightInfo: ICopyrightInfo,
        useOriginalCopyrightAndLicense: boolean,
        isValid: boolean,
    ) {
        setCopyrightInfo(copyrightInfo);
        setUseOriginalCopyrightAndLicense(useOriginalCopyrightAndLicense);
        setIsCopyrightValid(isValid);
    }

    function onLicenseChange(licenseInfo: ILicenseInfo, isValid: boolean) {
        setLicenseInfo(licenseInfo);
        setIsLicenseValid(isValid);
    }

    function handleOk() {
        const derivativeInfo: IDerivativeInfo = {
            isBookDerivative:
                !!props.data.derivativeInfo &&
                props.data.derivativeInfo.isBookDerivative,
            useOriginalCopyright: useOriginalCopyrightAndLicense,
        };
        const data: ICopyrightAndLicenseData = {
            copyrightInfo,
            licenseInfo,
            derivativeInfo,
        };
        postData(getApiUrlSuffix(props.isForBook), data);
        closeDialog();
    }

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
                            <CopyrightPanel
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
                        )}
                    </TabPanel>
                    <TabPanel>
                        {licenseInfo && (
                            <LicensePanel
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
                    enabled={isCopyrightValid && isLicenseValid}
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
        ReactDOM.render(
            <CopyrightAndLicenseDialog isForBook={isForBook} data={data} />,
            getModalContainer(),
        );
    } catch (error) {
        console.error(error);
    }
    show();
}

// Either the `get` call will show info to the user
// (e.g. read-only info if he can't modify the image or a message stating images cannot be changed unless unlocked)
// or we will display the dialog.
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
            console.error(err);
        },
    );
}

function getApiUrlSuffix(isForBook: boolean): string {
    return isForBook
        ? "copyrightAndLicense/bookCopyrightAndLicense"
        : "copyrightAndLicense/imageCopyrightAndLicense";
}

// It would be simpler to just use getEditTabBundleExports().getModalDialogContainer()
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
