import * as React from "react";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import { Div, Label } from "../../../react_components/l10n";
import { BloomApi } from "../../../utils/bloomApi";
import { ToolBox, ITool } from "../toolbox";
import { ApiBackedCheckbox } from "../../../react_components/apiBackedCheckbox";
import "./accessibleImage.less";
import { RequiresBloomEnterpriseWrapper } from "../../../react_components/requiresBloomEnterprise";

// This react class implements the UI for the accessible images toolbox.
// Note: this file is included in toolboxBundle.js because webpack.config says to include all
// tsx files in bookEdit/toolbox.
// The toolbox is included in the list of tools because of the one line of immediately-executed code
// which  passes an instance of AccessibleImageToolAdaptor to ToolBox.registerTool();
export class AccessibleImageControls extends React.Component<{}> {
    // This wants to be part of our state, passed as a prop to ApiBackedCheckbox.
    // But then we, and all the other clients of that class, have to be responsible
    // for interacting with the api to get and set that state. So, for the moment,
    // we just let the check box tell us what its value should be using onCheckChanged,
    // and use it to update the appearance of the page. Better solution wanted!
    private simulatingCataracts: boolean;

    public render() {
        return (
            <RequiresBloomEnterpriseWrapper>
                <div className="accessibleImageBody">
                    <Div l10nKey="EditTab.Toolbox.AccessibleImage.Overview">
                        You can use these check boxes to have Bloom simulate how
                        your images would look with various visual impairments.
                    </Div>
                    <ApiBackedCheckbox
                        className="checkBox"
                        apiEndpoint="accessibilityCheck/cataracts"
                        l10nKey="EditTab.Toolbox.AccessibleImage/Cataracts"
                        onCheckChanged={simulate =>
                            this.updateCataracts(simulate)
                        }
                    >
                        Cataracts
                    </ApiBackedCheckbox>
                </div>
            </RequiresBloomEnterpriseWrapper>
        );
    }

    private updateCataracts(simulate: boolean) {
        this.simulatingCataracts = simulate;
        this.updateSimulations();
    }

    public updateSimulations() {
        var page = ToolboxToolReactAdaptor.getPage();
        var body = page.ownerDocument.body;
        if (this.simulatingCataracts) {
            body.classList.add("simulateCataracts");
        } else {
            body.classList.remove("simulateCataracts");
        }
    }
}

// This class implements the ITool interface through our adaptor's abstract methods by calling
// the appropriate AccessibleImageControls methods.
export class AccessibleImageToolAdaptor extends ToolboxToolReactAdaptor {
    private controlsElement: AccessibleImageControls;

    public makeRootElement(): HTMLDivElement {
        return super.adaptReactElement(
            <AccessibleImageControls
                ref={renderedElement =>
                    (this.controlsElement = renderedElement)
                }
            />
        );
    }

    public id(): string {
        return "accessibleImage";
    }

    public showTool() {
        this.controlsElement.updateSimulations();
    }

    public newPageReady() {
        this.controlsElement.updateSimulations();
    }

    public isExperimental(): boolean {
        return true;
    }
}
