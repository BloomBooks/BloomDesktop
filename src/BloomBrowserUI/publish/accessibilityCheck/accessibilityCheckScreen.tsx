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
    // a HOC to inject l10n, I'm resorting to this method of localizing the tab names.
    private localizeTabNames() {
        this.localizeOneTabName(
            "AccessibilityCheck.LearnAbout",
            this.state.learnTabName,
            "learnTabName"
        );
        this.localizeOneTabName(
            "AccessibilityCheck.Checklist",
            this.state.checklistTabName,
            "checklistTabName"
        );
        this.localizeOneTabName(
            "AccessibilityCheck.ACEByDaisy",
            this.state.aceTabName,
            "aceTabName"
        );
    }

    private localizeOneTabName(
        id: string,
        englishText: string,
        stateMemberName: string
    ) {
        theOneLocalizationManager
            .asyncGetText(
                id,
                englishText,
                "Used as the name on a tab of the Accessibility Checks screen."
            )
            .done(result => {
                this.setState({ [stateMemberName]: result } as IState);
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
