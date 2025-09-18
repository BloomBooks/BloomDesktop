import * as React from "react";
import { ApiCheckbox } from "./ApiCheckbox";

export default {
    title: "BloomCheckbox/ApiCheckbox",
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
    name: "ApiCheckbox",
};
