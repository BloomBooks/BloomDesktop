/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { useState, useEffect } from "react";
import "./requiresBloomEnterprise.less";
import { BloomApi } from "../utils/bloomApi";
import Button from "@material-ui/core/Button";
import theme from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import { Div } from "./l10nComponents";
import { useL10n } from "./l10nHooks";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import { Dialog, DialogActions, DialogContent } from "@material-ui/core";
import BloomButton from "./bloomButton";
import { kFormBackground } from "../utils/colorUtils";
import { BloomDialog, useSetupBloomDialog } from "./BloomDialog/BloomDialog";

/**
 * This function sets up the hooks to get the status of whether Bloom Enterprise is available or not
 * @returns A boolean, which is true if Bloom Enterprise is enabled and false otherwise
 */
function useEnterpriseAvailable() {
    const [enterpriseAvailable, setEnterpriseAvailable] = useState(true);

    useEffect(() => {
        BloomApi.get("settings/enterpriseEnabled", response => {
            setEnterpriseAvailable(response.data);
        });
    }, []);

    return enterpriseAvailable;
}

/**
 * A component's props that include a "disabled" property
 */
interface IDisableable {
    disabled: boolean;
}

/**
 * Checks the Bloom Enterprise settings and appends a Bloom Enterprise icon after the children if enterprise is off.
 * The children will also have disabled set to true in that case.
 * The children and the icon (if applicable) will be displayed as a flex row.
 * @param props.iconStyles: Optional. If specified, provides additional CSS styles for the Bloom Enterprise icon. Omit to use just the default styles.
 * Note that you can override a style in the default styles by specifying it in iconStyles (because the last one wins)
 * @param props.children: The ReactElements that should be wrapped. The children must all support the "disabled" property
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
        "To use this feature, you'll need to enable Bloom Enterprise.",
        "EditTab.RequiresEnterprise"
    );

    // Set the disabled property on all the children
    const children = React.Children.map(props.children, child =>
        React.cloneElement(child, {
            disabled: !enterpriseAvailable
        })
    );

    const icon = enterpriseAvailable || (
        <img
            css={css`
                ${"height: 16px; margin-left: 6px; cursor: pointer; " +
                    (props.iconStyles ?? "")}
            `}
            src="../../../images/bloom-enterprise-badge.svg"
            title={tooltip}
            onClick={openBloomEnterpriseSettings}
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
            {children}
            {icon}
        </div>
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
                <div className="requiresEnterpriseOverlay">
                    <RequiresBloomEnterpriseNotice darkTheme={true} />
                </div>
            )}
        </div>
    );
};

export interface IThemeProps {
    darkTheme?: boolean;
}

// This element displays a notice saying that a certain feature requires a Bloom Enterprise subscription,
// if a bloom enterprise project has not been selected; if one has, it displays nothing at all.
// Typically, it is displayed along with a div that shows all the controls requiring the subscription,
// which is visible when this is not, that is, when enterpriseEnabled(), a function in this
// module, returns true. Currently this is detected by looking for the class enterprise-on being set
// on the content page body.
// Often it will be convenient to use this by embedding the controls to be hidden in a
// RequiresBloomEnterpriseWrapper, also defined in this file.
export const RequiresBloomEnterpriseNotice: React.VoidFunctionComponent<IThemeProps> = ({
    darkTheme
}) => {
    const [visible, setVisible] = useState(false);

    useEffect(() => {
        BloomApi.get("settings/enterpriseEnabled", response => {
            setVisible(!response.data);
        });
    }, []);

    const noticeClasses =
        "requiresEnterpriseNotice" + (darkTheme ? " darkTheme" : "");

    return (
        <ThemeProvider theme={theme}>
            <div
                className="requiresBloomEnterprise"
                style={visible ? {} : { display: "none" }}
            >
                <div className="messageSettingsDialogWrapper">
                    <div className={noticeClasses}>
                        <Div
                            className="requiresEnterpriseEnablingLabel"
                            l10nKey="EditTab.RequiresEnterprise"
                        />
                        <Button
                            className="requiresEnterpriseButton"
                            variant="contained"
                            onClick={openBloomEnterpriseSettings}
                        >
                            <img src="../images/bloom-enterprise-badge.svg" />
                            <Div l10nKey="EditTab.EnterpriseSettingsButton">
                                Bloom Enterprise Settings
                            </Div>
                        </Button>
                    </div>
                </div>
            </div>
        </ThemeProvider>
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
                        onClick={() => BloomApi.post("common/closeReactDialog")} // from pressing Close button
                    />
                </DialogActions>
            </Dialog>
        </BloomDialog>
    );
};

function openBloomEnterpriseSettings() {
    BloomApi.post("common/showSettingsDialog?tab=enterprise");
}

// Still used in imageDescription.tsx and talkingBook.ts
export function checkIfEnterpriseAvailable(): EnterpriseEnabledPromise {
    return new EnterpriseEnabledPromise();
}

// A very minimal implementation of Promise which supports only then() taking a boolean function.
// The function will be called with argument true if enterprise features are enabled, false otherwise.
class EnterpriseEnabledPromise {
    public then(resolve: (enterpriseAvailable: boolean) => void) {
        BloomApi.get("settings/enterpriseEnabled", response => {
            resolve(response.data);
        });
    }
}

WireUpForWinforms(RequiresBloomEnterpriseNoticeDialog);
