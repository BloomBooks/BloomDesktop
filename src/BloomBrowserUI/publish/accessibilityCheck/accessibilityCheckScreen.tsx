import * as React from "react";
import * as ReactDOM from "react-dom";
import "./accessibilityCheckScreen.less";
import { TabList, Tab, Tabs, TabPanel } from "react-tabs";
import { LearnAboutAccessibility } from "./learnAboutAccessibility";
import { AccessibilityChecklist } from "./accessibilityChecklist";
import { IUILanguageAwareProps } from "../../react_components/l10n";
import { DaisyChecks } from "./daisyChecks";

// This is a screen of controls that gives the user instructions and controls
// for creating epubs
class AccessibilityCheckScreen extends React.Component<IUILanguageAwareProps> {
    public render() {
        return (
            <div id="accessibilityCheckReactRoot" className={"screen-root"}>
                <div className="overall-status">Status: TODO</div>
                <Tabs defaultIndex={1}>
                    <TabList>
                        <Tab>Learn About Accessibility</Tab>
                        <Tab>Accessibility Checklist</Tab>
                        <Tab>ACE by Daisy Automated Checks</Tab>
                    </TabList>
                    <TabPanel>
                        <LearnAboutAccessibility />
                    </TabPanel>
                    <TabPanel>
                        <AccessibilityChecklist />
                    </TabPanel>
                    <TabPanel>
                        <DaisyChecks />
                    </TabPanel>
                </Tabs>
            </div>
        );
    }
}

// allow plain 'ol javascript in the html to connect up react
(window as any).connectAccessibilityCheckScreen = function(element) {
    ReactDOM.render(<AccessibilityCheckScreen />, element);
};
