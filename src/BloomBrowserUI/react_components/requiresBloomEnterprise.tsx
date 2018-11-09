import * as React from "react";
import * as ReactDOM from "react-dom";
import { Label } from "./l10n";
import "./requiresBloomEnterprise.less";
import { HelpLink } from "./helpLink";
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
        enterpriseFeaturesEnabled().then(enabled =>
            this.setState({ visible: !enabled })
        );
    }

    public render() {
        return (
            <div
                className="requiresBloomEnterprise"
                style={this.state.visible ? {} : { display: "none" }}
            >
                <div className="redTriangle">
                    <span className="triangleContent">!</span>
                </div>
                <div className="messageHelpWrapper">
                    <Label l10nKey="EditTab.Toolbox.RequiresEnterprise">
                        Requires Bloom Enterprise Subscription.
                    </Label>
                    <div className="requiresEnterpriseHelp">
                        <HelpLink
                            helpId="Tasks/Edit_tasks/Enterprise/EnterpriseRequired.htm"
                            l10nKey="Common.Help"
                        >
                            Help
                        </HelpLink>
                    </div>
                </div>
            </div>
        );
    }
}

export interface IWrapperComponentState {
    enterprise: boolean;
}

export interface IRequiresBloomEnterpriseProps {
    className?: string;
}

// The children of this component will be displayed if an enterprise project has been selected;
// otherwise, the RequiresBloomEnterprise message will be displayed.
export class RequiresBloomEnterpriseWrapper extends React.Component<
    IRequiresBloomEnterpriseProps,
    IWrapperComponentState
> {
    constructor(props) {
        super(props);
        this.state = { enterprise: true };
        enterpriseFeaturesEnabled().then(enabled =>
            this.setState({ enterprise: enabled })
        );
    }
    public render() {
        return (
            <div className={this.props.className}>
                <div
                    className="enterpriseContentWrapper"
                    style={{
                        display: this.state.enterprise ? "block" : "none"
                    }}
                >
                    {this.props.children}
                </div>
                <RequiresBloomEnterprise />
            </div>
        );
    }
}

export function enterpriseFeaturesEnabled(): EnterpriseEnabledPromise {
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
