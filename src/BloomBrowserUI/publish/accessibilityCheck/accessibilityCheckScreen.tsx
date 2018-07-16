import * as React from "react";
import * as ReactDOM from "react-dom";
import "./accessibilityCheckScreen.less";
import { TabList, Tab, Tabs, TabPanel } from "react-tabs";
import { LearnAboutAccessibility } from "./learnAboutAccessibility";
import { AccessibilityChecklist } from "./accessibilityChecklist";
import { DaisyChecks } from "./daisyChecks";
import WebSocketManager from "../../utils/WebSocketManager";
import { BloomApi } from "../../utils/bloomApi";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";

// This is a screen of controls that gives the user instructions and controls
// for creating epubs
interface IState {
    bookName: string;
    learnTabName: string;
    checklistTabName: string;
    aceTabName: string;
}

class AccessibilityCheckScreen extends React.Component<{}, IState> {
    constructor(props) {
        super(props);
        this.state = {
            bookName: "?",
            learnTabName: "Learn About Accessibility",
            checklistTabName: "Accessibility Checklist",
            aceTabName: "ACE by Daisy Automated Checks"
        };
    }

    public componentDidMount() {
        // Listen for changes to state from C#-land
        WebSocketManager.addListener("a11yChecklist", e => {
            if (e.message === "bookSelectionChanged") this.refresh();
        });
        this.localizeTabNames();
        this.refresh();
    }

    private refresh() {
        BloomApi.get("accessibilityCheck/bookName", result => {
            this.setState({
                bookName: result.data
            });
        });
    }

    // After trying unsuccessfully to wrap react-tabs Tab in a l10n-aware component or create
    // a HOC to inject l10n, I'm resorting to this less-satisfactory, but functional way of
    // localizing the tab names.
    private localizeTabNames() {
        theOneLocalizationManager
            .asyncGetText(
                "AccessibilityCheck.LearnAbout",
                this.state.learnTabName,
                "Used as the name on a tab of the Accessibility Checks screen."
            )
            .done(result => {
                this.setState({
                    learnTabName: result
                });
            });
        theOneLocalizationManager
            .asyncGetText(
                "AccessibilityCheck.Checklist",
                this.state.checklistTabName,
                "Used as the name on a tab of the Accessibility Checks screen."
            )
            .done(result => {
                this.setState({
                    checklistTabName: result
                });
            });
        theOneLocalizationManager
            .asyncGetText(
                "AccessibilityCheck.ACEByDaisy",
                this.state.aceTabName,
                "Used as the name on a tab of the Accessibility Checks screen."
            )
            .done(result => {
                this.setState({
                    aceTabName: result
                });
            });
    }

    public render() {
        return (
            <div id="accessibilityCheckReactRoot" className={"screen-root"}>
                <div className="header">
                    <div className="book-name">{this.state.bookName}</div>
                    {/* <div className="overall-status">Status: TODO</div> */}
                </div>
                <Tabs defaultIndex={1}>
                    <TabList>
                        <Tab>{this.state.learnTabName}</Tab>
                        <Tab>{this.state.checklistTabName}</Tab>
                        <Tab>{this.state.aceTabName}</Tab>
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
