import Button from "@material-ui/core/Button";
import * as React from "react";
import { useState } from "react";
import "App.less";

const kBloomBlue = "#1d94a4";
const kBackgroundGray = "#252525";
export const App: React.FunctionComponent<{}> = props => {
    return (
        <div style={{ backgroundColor: kBackgroundGray, height: "100%" }}>
            <div style={{ backgroundColor: kBloomBlue }}>
                <Tabs />
            </div>
        </div>
    );
};
const Tabs: React.FunctionComponent<{}> = props => {
    return (
        <ul id="main-tabs" style={{ height: "77px" }}>
            <li>
                <Button>Collections</Button>
            </li>
        </ul>
    );
};
