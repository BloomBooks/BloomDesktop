import * as React from "react";
import { Tab, TabList, TabPanel } from "react-tabs";
import { BloomTabs } from "../../BloomTabs";

export const BloomTabsTestHarness: React.FunctionComponent = () => {
    return (
        <div
            style={{
                height: "400px",
                width: "600px",
                backgroundColor: "#2f2f2f",
            }}
        >
            <BloomTabs
                defaultIndex={0}
                color="white"
                selectedColor="white"
                labelBackgroundColor="#2f2f2f"
            >
                <TabList>
                    <Tab>Preview</Tab>
                    <Tab>History</Tab>
                </TabList>
                <TabPanel>
                    <div data-testid="preview-content">Preview Content</div>
                </TabPanel>
                <TabPanel>
                    <div data-testid="history-content">History Content</div>
                </TabPanel>
            </BloomTabs>
        </div>
    );
};
