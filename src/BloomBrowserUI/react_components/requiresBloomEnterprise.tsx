/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useState, useEffect } from "react";
import { get, post } from "../utils/bloomApi";
import Button from "@mui/material/Button";
import { kBloomBlue50Transparent, lightTheme } from "../bloomMaterialUITheme";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { Div } from "./l10nComponents";
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

const badgeUrl = `${getBloomApiPrefix(false)}images/bloom-enterprise-badge.svg`;

/**
 * This function sets up the hooks to get the status of whether Bloom Enterprise is available or not
 * @returns A boolean, which is true if Bloom Enterprise is enabled and false otherwise
 */
export function useEnterpriseAvailable() {
    const [enterpriseAvailable, setEnterpriseAvailable] = useState(true);

    useEffect(() => {
        get("settings/enterpriseEnabled", response => {
            setEnterpriseAvailable(response.data);
        });
    }, []);

    return enterpriseAvailable;
}

/**
 * This function sets up a hook to get the Bloom Enterprise status.
 * @returns A string which matches one of the enum values in CollectionSettingsApi.cs.
 */
export function useGetEnterpriseStatus(): string {
    const [enterpriseStatus, setEnterpriseStatus] = useState<string>("None");

    useEffect(() => {
        get("settings/enterpriseStatus", result => {
            setEnterpriseStatus(result.data);
        });
    }, []);

    return enterpriseStatus;
}

/**
 * A component's props that include a "disabled" property
 */
interface IDisableable {
    disabled: boolean;
}

/**
 * Checks the Bloom Enterprise settings and appends a Bloom Enterprise icon after the children if enterprise is off.
 * The children and the icon (if applicable) will be displayed as a flex row.
 * @param props.iconStyles: Optional. If specified, provides additional CSS styles for the Bloom Enterprise icon. Omit to use just the default styles.
 * Note that you can override a style in the default styles by specifying it in iconStyles (because the last one wins)
 * @param props.children: The ReactElements that should be wrapped.
 */
export const RequiresBloomEnterpriseAdjacentIconWrapper = (props: {
    iconStyles?: string;
    children:
        | React.ReactElement<IDisableable>
        | Array<React.ReactElement<IDisableable>>;
}) => {
    const enterpriseAvailable = useEnterpriseAvailable();

    // Note: currently the tooltip only appears over the icon itself. But it might be nice if it could go over the children too?
    const tooltip = useL10n(
        enterpriseAvailable
            ? "This Bloom Enterprise feature is enabled for you."
            : "To use this feature, you'll need to enable Bloom Enterprise.",
        "EditTab." +
            (enterpriseAvailable ? "EnterpriseEnabled" : "RequiresEnterprise")
    );

    // // Set the disabled property on all the children
    const children = enterpriseAvailable
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
            title={tooltip}
            onClick={() => {
                if (!enterpriseAvailable) {
                    showRequiresBloomEnterpriseDialog();
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
                    opacity: ${enterpriseAvailable
                        ? 1.0
                        : kBloomDisabledOpacity};
                `}
                ref={node =>
                    node &&
                    // later version of react reportedly do support `inert`, but this version doesn't,
                    // so we are using this `ref` way to get it into the DOM.
                    (enterpriseAvailable || node.setAttribute("inert", ""))
                }
            >
                {children}
            </div>
            {icon}
        </div>
    );
};

export const BloomEnterpriseIcon = props => {
    const needEnterpriseTooltip = useL10n(
        "To use this feature, you'll need to enable Bloom Enterprise.",
        "EditTab.RequiresEnterprise"
    );
    const enterpriseFeatureTooltip = useL10n(
        "Bloom Enterprise Feature",
        "Common.BloomEnterpriseFeature"
    );
    const enterpriseAvailable = useEnterpriseAvailable();

    // Note: currently the tooltip only appears over the icon itself. But it might be nice if it could go over the children too?
    const tooltip = enterpriseAvailable
        ? enterpriseFeatureTooltip
        : needEnterpriseTooltip;

    return (
        <img
            css={css`
                height: 1.5em;
                margin-left: 1em;
            `}
            {...props} // let caller override the size and whatever
            src={badgeUrl}
            title={tooltip}
        />
    );
};

/**
 * Checks the Bloom Enterprise settings and overlays a RequiresBloomEnterprise notice over the children if enterprise is off.
 */
export const RequiresBloomEnterpriseOverlayWrapper: React.FunctionComponent = props => {
    const enterpriseAvailable = useEnterpriseAvailable();
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
            {enterpriseAvailable || (
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
                        <RequiresBloomEnterpriseNotice darkTheme={true} />
                    </div>
                </div>
            )}
        </div>
    );
};

export interface IRequiresEnterpriseNoticeProps {
    darkTheme?: boolean;
    inSeparateDialog?: boolean;
}

// This element displays a notice saying that a certain feature requires a Bloom Enterprise subscription,
// if a bloom enterprise project has not been selected; if one has, it displays nothing at all.
// Typically, it is displayed along with a div that shows all the controls requiring the subscription,
// which is visible when this is not, that is, when enterpriseEnabled(), a function in this
// module, returns true. Currently this is detected by looking for the class enterprise-on being set
// on the content page body.
// Often it will be convenient to use this by embedding the controls to be hidden in a
// RequiresBloomEnterpriseWrapper, also defined in this file.
export const RequiresBloomEnterpriseNotice: React.VoidFunctionComponent<IRequiresEnterpriseNoticeProps> = ({
    darkTheme,
    inSeparateDialog
}) => {
    const [visible, setVisible] = useState(false);

    useEffect(() => {
        get("settings/enterpriseEnabled", response => {
            setVisible(!response.data);
        });
    }, []);

    const kBloomEnterpriseNoticePadding = "15px;";
    const kButtonRadius = "4px;";
    const kBloomEnterpriseNoticeWidth = "250px;";
    const kBloomEnterpriseNoticeHeight = "120px;";
    const kBloomEnterpriseButtonWidth = "150px;";

    const noticeCommonCss = css`
        color: black;
        height: ${kBloomEnterpriseNoticeHeight};
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
                        !visible
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
                                          padding: ${kBloomEnterpriseNoticePadding};
                                          padding-bottom: 20px;
                                          max-width: ${kBloomEnterpriseNoticeWidth};
                                          ${noticeCommonCss}
                                          ${darkTheme
                                              ? noticeDarkCss
                                              : css`
                                                    color: black;
                                                    background-color: ${kFormBackground};
                                                `}
                                          // this is needed to overcome having MuiButton override the settings
                                          .requiresEnterpriseButton {
                                              ${buttonCommonCss}
                                          }
                                      `
                            }
                        >
                            <Div
                                l10nKey="EditTab.RequiresEnterprise"
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
                            />
                            <Button
                                className="requiresEnterpriseButton"
                                variant={"contained"}
                                onClick={openBloomEnterpriseSettings}
                                css={
                                    inSeparateDialog
                                        ? css`
                                              max-width: ${kBloomEnterpriseButtonWidth};
                                              align-self: normal;
                                              ${buttonCommonCss}
                                              border-radius: ${kButtonRadius};
                                              border-width: thin;
                                              border-color: ${kBloomBlue};
                                              border-style: solid;
                                          `
                                        : css`
                                              max-width: ${kBloomEnterpriseButtonWidth};
                                              align-self: center;
                                              ${buttonCommonCss}
                                          `
                                }
                            >
                                <img src={badgeUrl} />
                                <Div l10nKey="EditTab.EnterpriseSettingsButton">
                                    Bloom Enterprise Settings
                                </Div>
                            </Button>
                        </div>
                    </div>
                </div>
            </ThemeProvider>
        </StyledEngineProvider>
    );
};

/**
 * Brings up the Requires Bloom Enterprise notice as a MaterialUI dialog
 */
export const RequiresBloomEnterpriseNoticeDialog: React.VoidFunctionComponent = () => {
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
                    <RequiresBloomEnterpriseNotice darkTheme={false} />
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

export let showRequiresBloomEnterpriseDialog: () => void = () => {
    window.alert("showRequiresBloomEnterpriseDialog is not set up yet.");
};

export const RequiresBloomEnterpriseDialog: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = props => {
    // Designed to be invoked natively from TypeScript land.
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);
    showRequiresBloomEnterpriseDialog = showDialog;

    const dialogTitle = useL10n(
        "Bloom Enterprise Feature",
        "Common.BloomEnterpriseFeature"
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
                    <RequiresBloomEnterpriseNotice
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

function openBloomEnterpriseSettings() {
    post("common/showSettingsDialog?tab=enterprise");
}

// Still used in imageDescription.tsx and talkingBook.ts
export function checkIfEnterpriseAvailable(): EnterpriseEnabledPromise {
    return new EnterpriseEnabledPromise();
}

// A very minimal implementation of Promise which supports only then() taking a boolean function.
// The function will be called with argument true if enterprise features are enabled, false otherwise.
class EnterpriseEnabledPromise {
    public then(resolve: (enterpriseAvailable: boolean) => void) {
        get("settings/enterpriseEnabled", response => {
            resolve(response.data);
        });
    }
}

WireUpForWinforms(RequiresBloomEnterpriseNoticeDialog);
