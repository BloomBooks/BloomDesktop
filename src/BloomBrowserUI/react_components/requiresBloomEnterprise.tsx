/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { createContext, useState, useEffect } from "react";
import "./requiresBloomEnterprise.less";
import { BloomApi } from "../utils/bloomApi";
import Button from "@material-ui/core/Button";
import theme from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import { Div } from "./l10nComponents";
import { useL10n } from "./l10nHooks";

/**
 * A component that gets the Bloom Enterprise settings value and stores it in BloomEnterpriseAvailableContext.Provider.
 * The child nodes of this component may access to Bloom Enterprise settings value using BloomEnterpriseAvailableContext.Consumer.
 */
export const RequiresBloomEnterpriseProvider: React.FunctionComponent = props => {
    const [enterpriseAvailable, setEnterpriseAvailable] = useState(true);

    useEffect(() => {
        BloomApi.get("settings/enterpriseEnabled", response => {
            setEnterpriseAvailable(response.data);
        });
    }, []);

    return (
        <BloomEnterpriseAvailableContext.Provider value={enterpriseAvailable}>
            {props.children}
        </BloomEnterpriseAvailableContext.Provider>
    );
};

/**
 * A component's props that include a "disabled" property
 */
interface IDisableable {
    disabled: boolean;
}

/**
 * Checks the Bloom Enterprise settings and appends a Bloom Enterprise icon after the children if enterprise is off.
 * The children will also have disabled set to true.
 * @param props.iconStyles: Optional. If specified, overrides the CSS styles for the Bloom Enterprise icon. Omit to use default styles.
 * @param props.children: The ReactElements that should be wrapped. The children must all support the "disabled" property
 */
export const RequiresBloomEnterpriseAdjacentIconWrapper = (props: {
    iconStyles?: string;
    children:
        | React.ReactElement<IDisableable>
        | Array<React.ReactElement<IDisableable>>;
}) => {
    // Note: currently the tooltip only appears over the icon itself. But it might be nice if it could go over the children too?
    const tooltip = useL10n(
        "To use this feature, you'll need to enable Bloom Enterprise.",
        "EditTab.RequiresEnterprise"
    );

    return (
        <RequiresBloomEnterpriseProvider>
            <BloomEnterpriseAvailableContext.Consumer>
                {enterpriseAvailable => {
                    // Set the disabled property on all the children
                    const children = React.Children.map(props.children, child =>
                        React.cloneElement(child, {
                            disabled: !enterpriseAvailable
                        })
                    );

                    const icon = enterpriseAvailable || (
                        <img
                            css={css`
                                ${props.iconStyles !== undefined
                                    ? props.iconStyles
                                    : "height: 16px; margin-left: 6px; cursor: pointer"}
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
                }}
            </BloomEnterpriseAvailableContext.Consumer>
        </RequiresBloomEnterpriseProvider>
    );
};

/**
 * Checks the Bloom Enterprise settings and overlays a RequiresBloomEnterprise notice over the children if enterprise is off.
 */
export const RequiresBloomEnterpriseOverlayWrapper: React.FunctionComponent = props => {
    return (
        <RequiresBloomEnterpriseProvider>
            <BloomEnterpriseAvailableContext.Consumer>
                {enterpriseAvailable => (
                    <div style={{ height: "100%" }}>
                        <div style={{ display: "block", height: "100%" }}>
                            {props.children}
                        </div>
                        {enterpriseAvailable || (
                            <div className="requiresEnterpriseOverlay">
                                <RequiresBloomEnterpriseNotice
                                    darkTheme={true}
                                />
                            </div>
                        )}
                    </div>
                )}
            </BloomEnterpriseAvailableContext.Consumer>
        </RequiresBloomEnterpriseProvider>
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
export const RequiresBloomEnterpriseNotice: React.FunctionComponent<IThemeProps> = ({
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

function openBloomEnterpriseSettings() {
    BloomApi.post("common/showSettingsDialog?tab=enterprise");
}

// A note about the default value (false): this would only be used if a component had a context-consumer but no parent had created a context-provider.
export const BloomEnterpriseAvailableContext = createContext(false);

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
