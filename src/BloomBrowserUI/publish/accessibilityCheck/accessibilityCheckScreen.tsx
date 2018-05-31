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
                <Tabs>
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

// a bit goofy... currently the html loads everything in publishUIBundlejs. So all the publish screens
// get any code that isn't in a class called, the following. But it only makes sense to get wired up
// if that html has the root page we need.
if (document.getElementById("accessibilityCheckScreen")) {
    ReactDOM.render(
        <AccessibilityCheckScreen />,
        document.getElementById("accessibilityCheckScreen")
    );
}
