import * as React from "react";
import * as ReactDOM from "react-dom";
import "./accessibilityCheckScreen.less";
import { TabList, Tab, Tabs, TabPanel } from "react-tabs";
import { LearnAboutAccessibility } from "./learnAboutAccessibility";
import { AccessibilityChecklist } from "./accessibilityChecklist";
import { DaisyChecks } from "./daisyChecks";
import WebSocketManager from "../../utils/WebSocketManager";
import { BloomApi } from "../../utils/bloomApi";

// This is a screen of controls that gives the user instructions and controls
// for creating epubs
interface IState {
    bookName: string;
}
class AccessibilityCheckScreen extends React.Component<{}, IState> {
    constructor(props) {
        super(props);
        this.state = { bookName: "?" };
    }
    public componentDidMount() {
        // Listen for changes to state from C#-land
        WebSocketManager.addListener("a11yChecklist", event => {
            if (event.data === "bookSelectionChanged") this.refresh();
        });
        this.refresh();
    }
    private refresh() {
        BloomApi.get("api/accessibilityCheck/bookName", result => {
            this.setState({
                bookName: result.data
            });
        });
    }

    public render() {
        return (
            <div id="accessibilityCheckReactRoot" className={"screen-root"}>
                <div className="header">
                    <div className="book-name">{this.state.bookName}</div>
                    <div className="overall-status">Status: TODO</div>
                </div>
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
