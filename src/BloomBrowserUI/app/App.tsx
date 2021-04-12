import Button from "@material-ui/core/Button";
import * as React from "react";
import { useState } from "react";
import "App.less";
import { CollectionTabPane } from "../collectionTab/CollectionTabPane";

const kBloomBlue = "#1d94a4";
const kBackgroundGray = "#2e2e2e";
export const App: React.FunctionComponent<{}> = props => {
    return (
        <div style={{ backgroundColor: kBackgroundGray, height: "100%" }}>
            <div style={{ backgroundColor: kBloomBlue, paddingTop: "3px" }}>
                <Tabs />
            </div>
            <CollectionTabPane />
        </div>
    );
};
const Tabs: React.FunctionComponent<{}> = props => {
    return (
        <ul id="main-tabs" style={{ height: "77px" }}>
            <li>
                <Button
                    className={"selected"}
                    startIcon={<img src="../images/CollectionsTab.svg" />}
                >
                    Collections
                </Button>
                <Button startIcon={<img src="../images/EditTab.svg" />}>
                    Edit
                </Button>
                <Button startIcon={<img src="../images/PublishTab.svg" />}>
                    Publish
                </Button>
            </li>
        </ul>
    );
};
