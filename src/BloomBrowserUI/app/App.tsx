// We started needing the following triple-slash directive (<reference types=...)
// with Emotion 11; I don't understand why.
// Without it, the system doesn't resolve the type for the css prop.
// This migration guide describes it, but I don't understand why we aren't the "normal" case:
// https://emotion.sh/docs/emotion-11#css-prop-types
/// <reference types="@emotion/react/types/css-prop" />

import Button from "@mui/material/Button";
import * as React from "react";
import "App.less";
import { CollectionsTabPane } from "../collectionsTab/CollectionsTabPane";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import { kBloomBlue, kPanelBackground } from "../bloomMaterialUITheme";

// invoke this with http://localhost:8089". Doesn't do much yet... someday will be the root of our UI.

export const App: React.FunctionComponent = (props) => {
    return (
        <div style={{ backgroundColor: kPanelBackground, height: "100%" }}>
            <div style={{ backgroundColor: kBloomBlue, paddingTop: "3px" }}>
                <Tabs />
            </div>
            <CollectionsTabPane />
        </div>
    );
};
const Tabs: React.FunctionComponent = (props) => {
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

WireUpForWinforms(App);
