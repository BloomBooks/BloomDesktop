import * as React from "react";
import * as ReactDOM from "react-dom";
import ToolboxToolReactAdaptor from "../bookEdit/toolbox/toolboxToolReactAdaptor";
import { Label } from "./l10n";
import "./requiresBloomEnterprise.less";
import { HelpLink } from "./helpLink";

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
export class RequiresBloomEnterprise extends React.Component<{}, IComponentState> {

    constructor() {
        super({});
        this.state = { visible: !enterpriseFeaturesEnabled() };
    }

    public render() {
        return (<div className="requiresBloomEnterprise" style={this.state.visible ? {} : { display: "none" }}>
            <div className="redTriangle"><span className="triangleContent">!</span></div>
            <div className="messageHelpWrapper">
                <Label l10nKey="EditTab.Toolbox.RequiresEnterprise">Requires Bloom Enterprise Subscription.</Label>
                <div className="requiresEnterpriseHelp">
                    <HelpLink
                        helpId="Tasks/Edit_tasks/Enterprise/EnterpriseRequired.htm"
                        l10nKey="Common.Help">Help</HelpLink>
                </div>
            </div>
        </div>);
    }
}

export interface IWrapperComponentState {
    enterprise: boolean;
}

// The children of this component will be displayed if an enterprise project has been selected;
// otherwise, the RequiresBloomEnterprise message will be displayed.
export class RequiresBloomEnterpriseWrapper extends React.Component<{}, IWrapperComponentState> {
    constructor() {
        super({});
        this.state = { enterprise: enterpriseFeaturesEnabled() };
    }
    public render() {
        return (
            <div>
                <div style={{ display: (this.state.enterprise ? "block" : "none") }}>
                    {this.props.children}
                </div>
                <RequiresBloomEnterprise />
            </div>);
    }
}

export function enterpriseFeaturesEnabled(): boolean {
    var page = ToolboxToolReactAdaptor.getPage();
    return (page.classList.contains("enterprise-on"));
}