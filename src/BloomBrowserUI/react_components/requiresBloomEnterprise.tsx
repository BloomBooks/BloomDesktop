import * as React from "react";
import "./requiresBloomEnterprise.less";
import { Link } from "./link";
import { BloomApi } from "../utils/bloomApi";

export interface IComponentState {
    visible: boolean;
}

// This element displays a notice saying that a certain feature requires a Bloom Enterprise subscription,
// if a bloom enterprise project has not been selected; if one has, it displays nothing at all.
// Typically, it is displayed along with a div that shows all the controls requiring the subscription,
// which is visible when this is not, that is, when enterpriseFeaturesEnabled(), a function in this
// module, returns true. Currently this is detected by looking for the class enterprise-on being set
// on the content page body.
// Often it will be convenient to use this by embedding the controls to be hidden in a
// RequiresBloomEnterpriseWrapper, also defined in this file.
export class RequiresBloomEnterprise extends React.Component<
    {},
    IComponentState
> {
    public readonly state: IComponentState = {
        visible: false
    };

    constructor(props) {
        super(props);
        checkIfEnterpriseAvailable().then(enabled =>
            this.setState({ visible: !enabled })
        );
    }

    public render() {
        return (
            <div
                className="requiresBloomEnterprise"
                style={this.state.visible ? {} : { display: "none" }}
            >
                <div className="messageSettingsDialogWrapper">
                    <div className="requiresEnterpriseSettingsDialog">
                        <Link
                            l10nKey="EditTab.Toolbox.RequiresEnterprise"
                            onClick={() =>
                                BloomApi.post(
                                    "common/showSettingsDialog?tab=enterprise"
                                )
                            }
                        >
                            This feature requires Bloom Enterprise.
                        </Link>
                    </div>
                </div>
            </div>
        );
    }
}

export interface IWrapperComponentState {
    enterpriseAvailable: boolean;
}

export interface IRequiresBloomEnterpriseProps {
    className?: string;
}

// A note about the default value (false): this would only be used if a component had a context-consumer but no parent had created a context-provider.
export const BloomEnterpriseAvailableContext = React.createContext(false);

// The children of this component will be enabled and displayed if an enterprise project has been
// selected; otherwise, the RequiresBloomEnterprise message will be displayed and the children
// will be disabled and partially obscured.
export class RequiresBloomEnterpriseWrapper extends React.Component<
    IRequiresBloomEnterpriseProps,
    IWrapperComponentState
> {
    constructor(props) {
        super(props);
        this.state = { enterpriseAvailable: true };
        checkIfEnterpriseAvailable().then(enabled =>
            this.setState({ enterpriseAvailable: enabled })
        );
    }
    public render() {
        return (
            <BloomEnterpriseAvailableContext.Provider
                value={this.state.enterpriseAvailable}
            >
                <div style={{ height: "100%" }}>
                    <div style={{ display: "block", height: "100%" }}>
                        {this.props.children}
                    </div>
                    {this.state.enterpriseAvailable || (
                        <div className="requiresEnterpriseOverlay">
                            <RequiresBloomEnterprise />
                        </div>
                    )}
                </div>
            </BloomEnterpriseAvailableContext.Provider>
        );
    }
}

export function checkIfEnterpriseAvailable(): EnterpriseEnabledPromise {
    return new EnterpriseEnabledPromise();
}

// A very minimal implementation of Promise which supports only then() taking a boolean function.
// The function will be called with argument true if enterprise features are enabled, false otherwise.
class EnterpriseEnabledPromise {
    public then(resolve: (boolean) => void) {
        BloomApi.get("common/enterpriseFeaturesEnabled", response => {
            resolve(response.data);
        });
    }
}
