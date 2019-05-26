import * as React from "react";
import * as ReactDOM from "react-dom";
import "./accessibilityCheckScreen.less";
import { TabList, Tab, Tabs, TabPanel } from "react-tabs";
import { LearnAboutAccessibility } from "./learnAboutAccessibility";
import { AccessibilityChecklist } from "./accessibilityChecklist";
import { DaisyChecks } from "./daisyChecks";
import WebSocketManager from "../../utils/WebSocketManager";
import { BloomApi } from "../../utils/bloomApi";
import { String } from "../../react_components/l10nComponents";

// This is a screen of controls that gives the user instructions and controls
// for creating epubs
interface IState {
    bookName: string;
}
class AccessibilityCheckScreen extends React.Component<{}, IState> {
    public readonly state: IState = {
        bookName: "?"
    };

    public componentDidMount() {
        // Listen for changes to state from C#-land
        WebSocketManager.addListener("a11yChecklist", event => {
            if (
                // chose a different book
                event.id === "bookSelectionChanged" ||
                // get this if the title changed
                event.id === "bookContentsMayHaveChanged"
            ) {
                this.refresh();
            }
        });
        this.refresh();
    }

    private refresh() {
        BloomApi.get("accessibilityCheck/bookName", result => {
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
                    {/* <div className="overall-status">Status: TODO</div> */}
                </div>
                <Tabs defaultIndex={1}>
                    <TabList>
                        <Tab>
                            <String
                                l10nKey="AccessibilityCheck.LearnAbout"
                                l10nComment="Used as the name on a tab of the Accessibility Checks screen."
                            >
                                Learn About Accessibility
                            </String>
                        </Tab>
                        <Tab>
                            <String
                                l10nKey="AccessibilityCheck.Checklist"
                                l10nComment="Used as the name on a tab of the Accessibility Checks screen."
                            >
                                Accessibility Checklist
                            </String>
                        </Tab>
                        <Tab>
                            <String
                                l10nKey="AccessibilityCheck.ACEByDaisy"
                                l10nComment="Used as the name on a tab of the Accessibility Checks screen."
                            >
                                Ace by DAISY Automated Checks
                            </String>
                        </Tab>
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
(window as any).connectAccessibilityCheckScreen = element => {
    ReactDOM.render(<AccessibilityCheckScreen />, element);
};
