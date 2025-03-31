/* eslint-disable @typescript-eslint/no-empty-function */
// Don't add /** @jsxFrag React.Fragment */ or these stories won't show up in StoryBook! (at least in Aug 2022)
/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { ApiCheckbox } from "./ApiCheckbox";

export default {
    title: "BloomCheckbox/ApiCheckbox"
};

export const _ApiCheckbox = () =>
    React.createElement(() => (
        <ApiCheckbox
            label="Motion Book"
            l10nKey="PublishTab.Android.MotionBookMode"
            apiEndpoint="publish/bloompub/motionBookMode"
        />
    ));

_ApiCheckbox.story = {
    name: "ApiCheckbox"
};
