import * as React from "react";
import { createContext, useState, useEffect } from "react";
import "./requiresBloomEnterprise.less";
import { BloomApi } from "../utils/bloomApi";
import Button from "@material-ui/core/Button";
import theme from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import { Div } from "./l10nComponents";

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
export const RequiresBloomEnterprise: React.FunctionComponent<IThemeProps> = ({
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
                            onClick={() =>
                                BloomApi.post(
                                    "common/showSettingsDialog?tab=enterprise"
                                )
                            }
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

// A note about the default value (false): this would only be used if a component had a context-consumer but no parent had created a context-provider.
export const BloomEnterpriseAvailableContext = createContext(false);

// The children of this component will be enabled and displayed if an enterprise project has been
// selected; otherwise, the RequiresBloomEnterprise message will be displayed and the children
// will be disabled and partially obscured.
export const RequiresBloomEnterpriseWrapper: React.FunctionComponent = props => {
    const [enterpriseAvailable, setEnterpriseAvailable] = useState(true);

    useEffect(() => {
        BloomApi.get("settings/enterpriseEnabled", response => {
            setEnterpriseAvailable(response.data);
        });
    }, []);

    return (
        <BloomEnterpriseAvailableContext.Provider value={enterpriseAvailable}>
            <div style={{ height: "100%" }}>
                <div style={{ display: "block", height: "100%" }}>
                    {props.children}
                </div>
                {enterpriseAvailable || (
                    <div className="requiresEnterpriseOverlay">
                        <RequiresBloomEnterprise darkTheme={true} />
                    </div>
                )}
            </div>
        </BloomEnterpriseAvailableContext.Provider>
    );
};

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
