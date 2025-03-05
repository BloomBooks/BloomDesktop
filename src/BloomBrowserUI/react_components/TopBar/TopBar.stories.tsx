/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { TopBar } from "./TopBar";

export default {
    title: "TopBar"
};

export const _TopBar = () => <TopBar />;

_TopBar.story = {
    name: "TopBar"
};
