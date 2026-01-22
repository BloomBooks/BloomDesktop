import * as React from "react";
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
import { WireUpForWinforms } from "../../utils/WireUpWinform";
// This is a screen of controls that gives the user instructions and controls
// for creating epubs
interface IState {
    bookName: string;
}
export class AccessibilityCheckScreen extends React.Component<unknown, IState> {
    public readonly state: IState = {
        bookName: "?",
    };

    public componentDidMount() {
        // Add a class to the html element so we can scope our CSS.
        // This is a bit of a hack.
        // When trying to wrap up 6.3, we realized this dialog wasn't working at all.
        // The easiest/safest fix was to get rid of the .html file and use ReactControl.
        // That meant we lost the accessibilityCheckScreen class on the html element.
        // This restores it.
        // The real fix is to redo the CSS so it doesn't depend on that class,
        // but I didn't want to risk that close to a release.
        // Note that simply removing the class changes some styling (at least the underlining of links)
        // because of specificity differences.
        const html = document.getElementsByTagName("html")[0];
        if (html) {
            html.classList.add("accessibilityCheckScreen");
        }

        hookupLinkHandler();
        // Listen for changes to state from C#-land
        WebSocketManager.addListener("a11yChecklist", (event) => {
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
        get("accessibilityCheck/bookName", (result) => {
            this.setState({
                bookName: result.data,
            });
        });
    }

    public render() {
        return (
            <StyledEngineProvider injectFirst>
                <ThemeProvider theme={lightTheme}>
                    <div
                        id="accessibilityCheckReactRoot"
                        className={"screen-root"}
                    >
                        <div className="header">
                            <div className="book-name">
                                {this.state.bookName}
                            </div>
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
                </ThemeProvider>
            </StyledEngineProvider>
        );
    }
}

WireUpForWinforms(() => <AccessibilityCheckScreen />);
