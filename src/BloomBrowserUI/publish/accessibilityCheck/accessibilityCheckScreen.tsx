﻿import * as React from "react";
import * as ReactDOM from "react-dom";
import "./accessibilityCheckScreen.less";
import { TabList, Tab, Tabs, TabPanel } from "react-tabs";
import { LearnAboutAccessibility } from "./learnAboutAccessibility";
import { AccessibilityChecklist } from "./accessibilityChecklist";
import { DaisyChecks } from "./daisyChecks";
import WebSocketManager from "../../utils/WebSocketManager";
import { get } from "../../utils/bloomApi";
import { LocalizedString } from "../../react_components/l10nComponents";
import { lightTheme } from "../../bloomMaterialUITheme";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { hookupLinkHandler } from "../../utils/linkHandler";
// This is a screen of controls that gives the user instructions and controls
// for creating epubs
interface IState {
    bookName: string;
}
export class AccessibilityCheckScreen extends React.Component<unknown, IState> {
    public readonly state: IState = {
        bookName: "?"
    };

    public componentDidMount() {
        hookupLinkHandler();
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
        get("accessibilityCheck/bookName", result => {
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
                            <LocalizedString
                                l10nKey="AccessibilityCheck.LearnAbout"
                                l10nComment="Used as the name on a tab of the Accessibility Checks screen."
                            >
                                Learn About Accessibility
                            </LocalizedString>
                        </Tab>
                        <Tab>
                            <LocalizedString
                                l10nKey="AccessibilityCheck.Checklist"
                                l10nComment="Used as the name on a tab of the Accessibility Checks screen."
                            >
                                Accessibility Checklist
                            </LocalizedString>
                        </Tab>
                        <Tab>
                            <LocalizedString
                                l10nKey="AccessibilityCheck.ACEByDaisy"
                                l10nComment="Used as the name on a tab of the Accessibility Checks screen."
                            >
                                Ace by DAISY Automated Checks
                            </LocalizedString>
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
    ReactDOM.render(
        <StyledEngineProvider injectFirst>
            <ThemeProvider theme={lightTheme}>
                <AccessibilityCheckScreen />
            </ThemeProvider>
        </StyledEngineProvider>,
        element
    );
};
