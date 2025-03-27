/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useState, useEffect } from "react";
import { get, post } from "../utils/bloomApi";
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
    kBloomDisabledOpacity
} from "../utils/colorUtils";
import {
    BloomDialog,
    DialogTitle,
    DialogMiddle,
    DialogBottomButtons
} from "./BloomDialog/BloomDialog";
import { kUiFontStack } from "../bloomMaterialUITheme";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "./BloomDialog/BloomDialogPlumbing";
import { getBloomApiPrefix } from "../utils/bloomApi";
import {
    openBloomSubscriptionSettings,
    useGetFeatureStatus,
    useGetFeatureAvailabilityMessage
} from "./featureStatus";

const badgeUrl = `${getBloomApiPrefix(false)}images/bloom-enterprise-badge.svg`;

// TODO: REMOVE THIS. Everything should be using useGetFeatureStatus() instead of this.
// Tells you wether the user has an active subscription or not.
export function useHaveSubscription() {
    const [haveSubscription, setHaveSubscription] = useState(true);

    useEffect(() => {
        get("settings/subscriptionEnabled", response => {
            setHaveSubscription(response.data);
        });
    }, []);

    return haveSubscription;
}

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
    const memoizedFeatureName = React.useMemo(() => props.featureName, [
        props.featureName
    ]);
    const featureStatus = useGetFeatureStatus(memoizedFeatureName);
    const tierMessage = useGetFeatureAvailabilityMessage(featureStatus);

    // Set the disabled property on all the children
    const children = featureStatus?.enabled
        ? props.children
        : React.Children.map(props.children, child =>
              React.cloneElement(child, {
                  disabled: false // we're going to slap a partial opacity on the whole thing, so we don't want the children to be disabled
              })
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
                    showRequiresSubscriptionDialog();
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
                ref={node =>
                    node &&
                    // later version of react reportedly do support `inert`, but this version doesn't,
                    // so we are using this `ref` way to get it into the DOM.
                    (featureStatus?.enabled || node.setAttribute("inert", ""))
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
}> = props => {
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
}> = props => {
    const memoizedFeatureName = React.useMemo(() => props.featureName, [
        props.featureName
    ]);
    const featureStatus = useGetFeatureStatus(memoizedFeatureName);

    return (
        <div
            css={css`
                height: 100%;
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

export const RequiresSubscriptionNoticeDialog: React.VoidFunctionComponent = () => {
    // Designed to be invoked from WinForms land.
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog({
        dialogFrameProvidedExternally: true,
        initiallyOpen: true
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
                    <RequiresSubscriptionNotice darkTheme={false} />
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

export let showRequiresSubscriptionDialog: () => void = () => {
    window.alert("showRequiresSubscriptionDialog is not set up yet.");
};

export const RequiresSubscriptionDialog: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = props => {
    // Designed to be invoked natively from TypeScript land.
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);
    showRequiresSubscriptionDialog = showDialog;

    const dialogTitle = useL10n(
        "Bloom Subscription Feature",
        "Common.BloomSubscriptionFeature"
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
    disabled?: boolean;
    className?: string;
}> = props => {
    const haveSubscription = useHaveSubscription();

    return (
        <div
            onClick={() => {
                if (!haveSubscription && !props.disabled) {
                    openBloomSubscriptionSettings();
                }
            }}
            css={css`
                display: flex;
                align-items: center;
                ${haveSubscription || props.disabled || "cursor:pointer"};

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
            {haveSubscription ? (
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
