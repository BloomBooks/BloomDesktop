import { css } from "@emotion/react";

import * as React from "react";
import { useEffect } from "react";
import { post } from "../utils/bloomApi";
import Button from "@mui/material/Button";
import { kBloomBlue50Transparent, lightTheme } from "../bloomMaterialUITheme";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { Div, Span } from "./l10nComponents";
import { useL10n } from "./l10nHooks";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import { Dialog, DialogActions, DialogContent } from "@mui/material";
import BloomButton from "./bloomButton";
import {
    kFormBackground,
    kBloomGray,
    kBloomBlue,
    kBloomDisabledOpacity,
} from "../utils/colorUtils";
import {
    BloomDialog,
    DialogTitle,
    DialogMiddle,
    DialogBottomButtons,
} from "./BloomDialog/BloomDialog";
import { kUiFontStack } from "../bloomMaterialUITheme";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog,
} from "./BloomDialog/BloomDialogPlumbing";
import { getBloomApiPrefix } from "../utils/bloomApi";
import {
    openBloomSubscriptionSettings,
    useGetFeatureStatus,
    useGetFeatureAvailabilityMessage,
} from "./featureStatus";
import { ShowEditViewDialog } from "../bookEdit/editViewFrame";
import { getPageIframeBody } from "../utils/shared";
import { getEditTabBundleExports } from "../bookEdit/js/bloomFrames";

const badgeUrl = `${getBloomApiPrefix(false)}images/bloom-enterprise-badge.svg`;

/**
 * A component's props that include a "disabled" property
 */
interface IDisableable {
    disabled: boolean;
}

export const RequiresSubscriptionAdjacentIconWrapper = (props: {
    iconStyles?: string;
    featureName: string;
    children:
        | React.ReactElement<IDisableable>
        | Array<React.ReactElement<IDisableable>>;
}) => {
    const memoizedFeatureName = React.useMemo(
        () => props.featureName,
        [props.featureName],
    );
    const featureStatus = useGetFeatureStatus(memoizedFeatureName);
    const tierMessage = useGetFeatureAvailabilityMessage(featureStatus);

    // Set the disabled property on all the children
    const children = featureStatus?.enabled
        ? props.children
        : React.Children.map(props.children, (child) =>
              React.cloneElement(child, {
                  disabled: false, // we're going to slap a partial opacity on the whole thing, so we don't want the children to be disabled
              }),
          );

    const icon = (
        <img
            css={css`
                ${"height: 16px; margin-left: 6px; cursor: pointer; " +
                (props.iconStyles ?? "")}
            `}
            src={badgeUrl}
            title={tierMessage}
            onClick={() => {
                if (!featureStatus?.enabled) {
                    showRequiresSubscriptionDialogInAnyView(props.featureName);
                }
            }}
        />
    );

    // The align-items is so that the img is aligned properly with the text
    return (
        <div
            css={css`
                display: flex;
                align-items: center;
            `}
        >
            <div
                css={css`
                    opacity: ${featureStatus?.enabled
                        ? 1.0
                        : kBloomDisabledOpacity};
                `}
                ref={(node) =>
                    node &&
                    // later version of react reportedly do support `inert`, but this version doesn't,
                    // so we are using this `ref` way to get it into the DOM.
                    (featureStatus?.enabled
                        ? node.removeAttribute("inert")
                        : node.setAttribute("inert", ""))
                }
            >
                {children}
            </div>
            {icon}
        </div>
    );
};
export const BloomEnterpriseIconWithTooltip: React.FunctionComponent<{
    featureName: string;
}> = (props) => {
    const featureStatus = useGetFeatureStatus(props.featureName);
    const featureMessage = useGetFeatureAvailabilityMessage(featureStatus);

    return (
        <img
            css={css`
                height: 1.5em;
                margin-left: 1em;
            `}
            {...props} // let caller override the size and whatever
            src={badgeUrl}
            title={featureMessage}
        />
    );
};

export const RequiresSubscriptionOverlayWrapper: React.FunctionComponent<{
    featureName: string;
}> = (props) => {
    const memoizedFeatureName = React.useMemo(
        () => props.featureName,
        [props.featureName],
    );
    const featureStatus = useGetFeatureStatus(memoizedFeatureName);

    return (
        <div
            data-testid="requires-subscription-overlay-wrapper"
            data-feature-name={memoizedFeatureName} // somehow used by tests
            css={css`
                height: 100%;
                box-sizing: border-box;
                // position:relative allows the overlay to cover only the children of this div.
                // Do not set any of left, right, top, or bottom since we don't want to shift
                // the position of this div and its children relative to our parent, just to
                // affect the starting point of the position:absolute used below.
                position: relative;
            `}
        >
            <div
                css={css`
                    display: block;
                    height: 100%;
                `}
            >
                {props.children}
            </div>
            {featureStatus?.enabled || (
                <div
                    css={css`
                        position: absolute;
                        top: 0;
                        left: 0;
                        right: 0;
                        bottom: 0;
                        background-color: ${kBloomBlue50Transparent};
                        z-index: 2; // Specify a stack order in case you're using a different order for other elements
                        display: flex;
                        align-items: center; // center vertically
                    `}
                >
                    <div // center horizontally
                        css={css`
                            margin-left: auto;
                            margin-right: auto;
                        `}
                    >
                        <RequiresSubscriptionNotice
                            featureName={memoizedFeatureName}
                            darkTheme={true}
                        />
                    </div>
                </div>
            )}
        </div>
    );
};

// Shown in the add page dialog when you select a page that requires a subscription, but don't have one.
export const RequiresSubscriptionNotice: React.VoidFunctionComponent<{
    darkTheme?: boolean;
    inSeparateDialog?: boolean;
    featureName?: string;
}> = ({ darkTheme, inSeparateDialog, featureName }) => {
    const memoizedFeatureName = React.useMemo(() => featureName, [featureName]);
    const featureStatus = useGetFeatureStatus(memoizedFeatureName); // Use the memoized value
    const subscriptionMessage = useGetFeatureAvailabilityMessage(featureStatus);

    const kBloomSubscriptionNoticePadding = "15px;";
    const kButtonRadius = "4px;";
    const kBloomSubscriptionNoticeWidth = "250px;";
    const kBloomSubscriptionNoticeHeight = "120px;";
    const kBloomSubscriptionButtonWidth = "150px;";

    const noticeCommonCss = css`
        color: black;
        height: ${kBloomSubscriptionNoticeHeight};
        display: flex;
        flex-direction: column;
        justify-content: space-between;
    `;
    const noticeDarkCss = css`
        color: white;
        background-color: ${kBloomGray};
    `;
    const buttonCommonCss = css`
        background-color: white;
        line-height: 1.1;
        font-size: small;

        // These were the original values for the MuiButton when color was "default".
        // That setting is no longer part of material, starting with v5.
        color: rgba(0, 0, 0, 0.87);
        &:hover {
            background-color: #d5d5d5;
        }
    `;

    return (
        <StyledEngineProvider injectFirst>
            <ThemeProvider theme={lightTheme}>
                <div
                    css={
                        featureStatus?.enabled
                            ? css`
                                  display: none;
                              `
                            : inSeparateDialog
                              ? css`
                                    padding: 10px 0px 0px 0px;
                                `
                              : css`
                                    padding: 5px;
                                `
                    }
                >
                    <div className="messageSettingsDialogWrapper">
                        <div
                            css={
                                inSeparateDialog
                                    ? css`
                                          text-align: left;
                                          padding: 0;
                                          max-width: 500px;
                                          ${noticeCommonCss}
                                          ${darkTheme
                                              ? noticeDarkCss
                                              : css`
                                                    color: black;
                                                    background-color: white;
                                                `}
                                      `
                                    : css`
                                          text-align: center;
                                          padding: ${kBloomSubscriptionNoticePadding};
                                          padding-bottom: 20px;
                                          max-width: ${kBloomSubscriptionNoticeWidth};
                                          ${noticeCommonCss}
                                          ${darkTheme
                                              ? noticeDarkCss
                                              : css`
                                                    color: black;
                                                    background-color: ${kFormBackground};
                                                `}
                                          // this is needed to overcome having MuiButton override the settings
                                          .requiresSubscriptionButton {
                                              ${buttonCommonCss}
                                          }
                                      `
                            }
                        >
                            <div
                                css={
                                    inSeparateDialog
                                        ? css`
                                              margin: 0;
                                              font-size: medium;
                                          `
                                        : css`
                                              margin: 0 5px;
                                              font-size: small;
                                          `
                                }
                            >
                                {subscriptionMessage}
                            </div>
                            <Button
                                className="requiresSubscriptionButton"
                                variant={"contained"}
                                onClick={openBloomSubscriptionSettings}
                                css={
                                    inSeparateDialog
                                        ? css`
                                              max-width: ${kBloomSubscriptionButtonWidth};
                                              align-self: normal;
                                              ${buttonCommonCss}
                                              border-radius: ${kButtonRadius};
                                              border-width: thin;
                                              border-color: ${kBloomBlue};
                                              border-style: solid;
                                          `
                                        : css`
                                              max-width: ${kBloomSubscriptionButtonWidth};
                                              align-self: center;
                                              ${buttonCommonCss}
                                          `
                                }
                            >
                                <img src={badgeUrl} />
                                <Div l10nKey="EditTab.SubscriptionSettingsButton">
                                    Bloom Subscription Settings
                                </Div>
                            </Button>
                        </div>
                    </div>
                </div>
            </ThemeProvider>
        </StyledEngineProvider>
    );
};

// I can't find ANY uses of this component, so if there are any, they have probably
// not been updated to supply the reaquired featureName prop.
export const RequiresSubscriptionNoticeDialog: React.FunctionComponent<{
    featureName: string;
}> = (props) => {
    // Designed to be invoked from WinForms land.
    const { showDialog, closeDialog, propsForBloomDialog } =
        useSetupBloomDialog({
            dialogFrameProvidedExternally: true,
            initiallyOpen: true,
        });

    return (
        <BloomDialog {...propsForBloomDialog}>
            <Dialog
                open={propsForBloomDialog.open}
                maxWidth={"md"}
                fullScreen={true}
                css={css`
                    // We aren't getting the font family from the theme for an unknown reason.
                    // (See comment in BloomDialog where we had to add the font family explicitly for the same reason.)
                    // We aren't getting the font family from BloomDialog because somehow in this context,
                    // we are not actually a child of BloomDialog by the time we render. My only guess
                    // is that it has something to do with the way the modal stuff is happening.
                    // But we are in a MuiDialog with is a sibling to a react root which has the BloomDialog.
                    // Anyway, with Material 5 ready now to switch to, and having already spent a couple hours trying to nail
                    // down what, exactly, is going on, I settled for this unfortunate band-aid. BL-10688
                    font-family: ${kUiFontStack};
                `}
            >
                <DialogContent
                    css={css`
                        background-color: ${kFormBackground};
                    `}
                >
                    <RequiresSubscriptionNotice
                        darkTheme={false}
                        featureName={props.featureName}
                    />
                </DialogContent>
                <DialogActions>
                    <BloomButton
                        enabled={true}
                        l10nKey="Common.Close"
                        variant="text"
                        onClick={() => post("common/closeReactDialog")} // from pressing Close button
                    />
                </DialogActions>
            </Dialog>
        </BloomDialog>
    );
};

// This gets replaced by a showDialog function when the RequiresSubscriptionDialog component is mounted.
let showRequiresSubscriptionDialogInternal: () => void = () => {
    window.alert("showRequiresSubscriptionDialog is not set up yet.");
};
const defaultShowfunction = showRequiresSubscriptionDialogInternal;

let featureNameToShowInRequiresSubscriptionDialog: string | undefined;

// This function should be used to show the dialog in contexts where there is a RequiresSubscriptionDialog
// component in the React tree. It will make it visible with the appropriate information
// for the featureName provided here.
export function showRequiresSubscriptionDialog(featureName: string) {
    featureNameToShowInRequiresSubscriptionDialog = featureName;
    showRequiresSubscriptionDialogInternal();
}

// This function shows it in the edit view, or any other window where the root isn't a React component
// that already has a RequiresSubscriptionDialog component in it and we can show the dialog in our
// own document. It makes a div at the document root and renders the RequiresSubscriptionDialog
// component into it.
export function showRequiresSubscriptionDialogInEditView(featureName: string) {
    ShowEditViewDialog(
        <RequiresSubscriptionDialog featureName={featureName} />,
    );
    // We have to give it a chance to render, since a useEffect in the render
    // is what sets the showRequiresSubscriptionDialogInternal function.
    setTimeout(() => showRequiresSubscriptionDialogInternal(), 0);
}

export function showRequiresSubscriptionDialogInAnyView(featureName: string) {
    if (getPageIframeBody()?.ownerDocument ?? document !== document) {
        // We're in edit mode, but executing from the toolbox. We want the dialog to
        // show in the edit view, where there is room for it, so we have to
        // use a function in that iframe's code to show it. (It's not enough
        // to just make the root element in that iframe; it seems to quietly
        // not work if we try to render a react component using code in a different
        // iframe than the element it is rendered into.)
        getEditTabBundleExports().showRequiresSubscriptionDialog(featureName);
    } else if (showRequiresSubscriptionDialogInternal === defaultShowfunction) {
        // no mounted component to show it, so we'll make one.
        showRequiresSubscriptionDialogInEditView(featureName);
    } else {
        showRequiresSubscriptionDialog(featureName);
    }
}

// To show this dialog properly, we definitely need a feature name. However, we embed the dialog
// at the root of various displays, like the whole publish screen, where we don't know which
// feature we may be displaying it for. So we allow one to be omitted, but if this is to be done,
// it must be provided when the dialog is shown. This is managed through showRequiresSubscriptionDialog,
// where the featureName is required, and the featureNameToShowInRequiresSubscriptionDialog, where
// we put the feature name to be used when the dialog is shown without a props.featureName.
// This only works because we don't expect more than one instance of this dialog to be shown at a time.
export const RequiresSubscriptionDialog: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
    featureName?: string;
}> = (props) => {
    // Designed to be invoked natively from TypeScript land.
    const { showDialog, closeDialog, propsForBloomDialog } =
        useSetupBloomDialog(props.dialogEnvironment);
    useEffect(() => {
        showRequiresSubscriptionDialogInternal = showDialog;
        return () => {
            showRequiresSubscriptionDialogInternal = defaultShowfunction;
        };
    }, [showDialog]);

    const dialogTitle = useL10n(
        "Bloom Subscription Feature",
        "Common.BloomSubscriptionFeature",
    );

    return (
        <BloomDialog {...propsForBloomDialog}>
            <div>
                <DialogTitle
                    title={dialogTitle}
                    css={css`
                        margin-left: 0;
                        margin-right: 0;
                        h1 {
                            font-size: large;
                        }
                    `}
                />
                <DialogMiddle>
                    <RequiresSubscriptionNotice
                        darkTheme={false}
                        inSeparateDialog={true}
                        featureName={
                            props.featureName ??
                            featureNameToShowInRequiresSubscriptionDialog
                        }
                    />
                </DialogMiddle>
                <DialogBottomButtons
                    css={css`
                        padding: 10px 0px 10px 0px;
                    `}
                >
                    <BloomButton
                        enabled={true}
                        l10nKey="Common.Close"
                        variant="contained"
                        onClick={() => closeDialog()} // from pressing Close button
                    />
                </DialogBottomButtons>
            </div>
        </BloomDialog>
    );
};

export const BloomSubscriptionIndicatorIconAndText: React.FunctionComponent<{
    feature: string;
    disabled?: boolean;
    className?: string;
}> = (props) => {
    const tierAllowsFeature = useGetFeatureStatus(props.feature)?.enabled;

    return (
        <div
            onClick={() => {
                if (!tierAllowsFeature && !props.disabled) {
                    openBloomSubscriptionSettings();
                }
            }}
            css={css`
                display: flex;
                align-items: center;
                ${tierAllowsFeature || props.disabled || "cursor:pointer"};

                opacity: ${props.disabled ? kBloomDisabledOpacity : 1.0};
            `}
            className={props.className}
        >
            <img
                src={badgeUrl}
                css={css`
                    height: 1.5em;
                    padding-right: 0.5em;
                `}
            />
            {tierAllowsFeature ? (
                <Span l10nKey={"AvailableWithSubscription"}>
                    Available with your Bloom Subscription
                </Span>
            ) : (
                <Span l10nKey={"Common.HigherSubscriptionTierRequired"}>
                    Feature Requires Higher Subscription Tier
                </Span>
            )}
        </div>
    );
};

WireUpForWinforms(RequiresSubscriptionNoticeDialog);
