/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { useEffect, useState } from "react";
import ReactDOM = require("react-dom");
import { Tab, TabList, TabPanel } from "react-tabs";
import "react-tabs/style/react-tabs.less";

import { WireUpForWinforms } from "../../utils/WireUpWinform";
import { BloomApi } from "../../utils/bloomApi";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import {
    BloomDialog,
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog,
    DialogBottomButtons,
    DialogTitle,
    DialogMiddle
} from "../../react_components/BloomDialog/BloomDialog";
import {
    DialogCancelButton,
    DialogOkButton
} from "../../react_components/BloomDialog/commonDialogComponents";
import { BloomTabs } from "../../react_components/BloomTabs";
import { getEditTabBundleExports } from "../js/bloomFrames";
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
}> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);

    // Provide external ability to show the dialog
    show = showDialog;

    const data = props.data;

    // Tell edit tab to disable everything when the dialog is up.
    // (Without this, the page list is not disabled since the modal
    // div only exists in the book pane. Once the whole edit tab is inside
    // one browser, this would not be necessary.)
    React.useEffect(() => {
        if (propsForBloomDialog.open === undefined) return;

        BloomApi.postBoolean(
            "editView/setModalState",
            propsForBloomDialog.open
        );
    }, [propsForBloomDialog.open]);

    const [
        useOriginalCopyrightAndLicense,
        setUseOriginalCopyrightAndLicense
    ] = useState(false);
    useEffect(() => {
        setUseOriginalCopyrightAndLicense(
            data?.derivativeInfo?.useOriginalCopyright === true
        );
    }, [data?.derivativeInfo?.useOriginalCopyright]);

    const [isCopyrightValid, setIsCopyrightValid] = useState(false);
    const [isLicenseValid, setIsLicenseValid] = useState(true);

    const [forceRenderHack, setForceRenderHack] = useState(0);

    function onCopyrightChange(isValid: boolean) {
        setForceRenderHack(forceRenderHack + 1);
        setIsCopyrightValid(isValid);
    }

    function onLicenseChange(isValid: boolean) {
        setForceRenderHack(forceRenderHack + 1);
        setIsLicenseValid(isValid);
    }

    function handleOk() {
        BloomApi.postData(getApiUrlSuffix(props.isForBook), data);
        closeDialog();
    }

    return (
        <BloomDialog {...propsForBloomDialog} heightInPx={700}>
            {data?.licenseInfo && (
                <div
                    css={css`
                        position: absolute;
                        top: 20px;
                        right: 20px;
                    `}
                >
                    <LicenseBadge
                        licenseInfo={
                            useOriginalCopyrightAndLicense
                                ? data.derivativeInfo!.originalLicense!
                                : data.licenseInfo
                        }
                        disabled={useOriginalCopyrightAndLicense}
                    />
                </div>
            )}
            <DialogTitle
                title={useL10n("Copyright and License", "CopyrightAndLicense")}
                css={css`
                    padding-bottom: 0;
                    margin-bottom: 0;
                `}
            />
            <DialogMiddle
                css={css`
                    width: ${props.dialogEnvironment
                        ?.dialogFrameProvidedExternally
                        ? "100%"
                        : "500px"};
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
                        {data?.copyrightInfo && (
                            <CopyrightPanel
                                isForBook={props.isForBook}
                                derivativeInfo={data.derivativeInfo}
                                copyrightInfo={data.copyrightInfo}
                                onChange={isValid => onCopyrightChange(isValid)}
                            />
                        )}
                    </TabPanel>
                    <TabPanel>
                        {data?.licenseInfo && (
                            <LicensePanel
                                licenseInfo={data.licenseInfo}
                                derivativeInfo={data.derivativeInfo}
                                onChange={isValid => onLicenseChange(isValid)}
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
                <DialogCancelButton onClick={closeDialog} />
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
    data: ICopyrightAndLicenseData
) {
    try {
        ReactDOM.render(
            <CopyrightAndLicenseDialog isForBook={isForBook} data={data} />,
            getModalContainer()
        );
    } catch (error) {
        console.error(error);
    }
    show();
}

// Either the `get` call will show info to the user
// (e.g. read-only info if he can't modify the image)
// or we will display the dialog.
export function showCopyrightAndLicenseInfoOrDialog(imageUrl?: string) {
    const isForBook: boolean = !imageUrl;
    BloomApi.get(
        getApiUrlSuffix(isForBook) + (imageUrl ? `?imageUrl=${imageUrl}` : ""),
        result => {
            if (result.data && JSON.stringify(result.data) !== "{}") {
                showCopyrightAndLicenseDialog(isForBook, result.data);
            }
        },
        err => {
            console.error(err);
        }
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
    const editFrameDocument = getEditTabBundleExports().getDocument();
    let modalDialogContainer = editFrameDocument.getElementById(
        "CopyrightAndLicenseDialogContainer"
    );
    if (modalDialogContainer) {
        modalDialogContainer.remove();
    }
    modalDialogContainer = editFrameDocument.createElement("div");
    modalDialogContainer.id = "CopyrightAndLicenseDialogContainer";
    editFrameDocument.body.appendChild(modalDialogContainer);
    return modalDialogContainer;
}
