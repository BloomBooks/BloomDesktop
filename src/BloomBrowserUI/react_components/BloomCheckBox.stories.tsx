/* eslint-disable @typescript-eslint/no-empty-function */
// Don't add /** @jsxFrag React.Fragment */ or these stories won't show up in StoryBook! (at least in Aug 2022)
/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { storiesOf } from "@storybook/react";
import { BloomCheckbox } from "./BloomCheckBox";
import { useState } from "react";
import { ApiCheckbox } from "./ApiCheckbox";

import {
    FormControlLabel,
    Checkbox as OriginalMuiCheckbox
} from "@mui/material";
import { VisuallyImpairedIcon } from "./icons/VisuallyImpairedIcon";
import { MotionIcon } from "./icons/MotionIcon";

const kLongText =
    "Bacon ipsum dolor amet ribeye spare ribs bresaola t-bone. Strip steak turkey shankle pig ground round, biltong t-bone kevin alcatra flank ribeye beef ribs meatloaf filet mignon. Buffalo ham t-bone short ribs.";

storiesOf("BloomCheckbox", module)
    .add("Various", () =>
        React.createElement(() => {
            const [checked, setChecked] = useState<boolean | undefined>(false);
            return (
                <React.Fragment>
                    original mui checkbox:
                    <div>
                        <FormControlLabel
                            control={<OriginalMuiCheckbox />}
                            label={"short"}
                        />
                        <FormControlLabel
                            control={<OriginalMuiCheckbox />}
                            label={kLongText}
                        />
                    </div>
                    <hr />
                    BloomCheckbox, which allows multi-line labels and icons:
                    <div>
                        <BloomCheckbox
                            label="short"
                            checked={true}
                            onCheckChanged={() => {}}
                            l10nKey="bogus"
                        />
                        <BloomCheckbox
                            label={kLongText}
                            checked={true}
                            onCheckChanged={() => {}}
                            l10nKey="bogus"
                        />
                        <BloomCheckbox
                            label={"With Icon " + kLongText}
                            icon={<VisuallyImpairedIcon />}
                            checked={checked}
                            tristate={true}
                            onCheckChanged={newState => setChecked(newState)}
                            l10nKey="bogus"
                        />
                        <BloomCheckbox
                            label={"hideBox, checked"}
                            checked={true}
                            hideBox={true}
                            onCheckChanged={() => {}}
                            l10nKey="bogus"
                        />
                        <BloomCheckbox
                            label={"hideBox, not checked, with icon"}
                            icon={<MotionIcon />}
                            checked={false}
                            hideBox={true}
                            onCheckChanged={() => {}}
                            l10nKey="bogus"
                        />
                    </div>
                </React.Fragment>
            );
        })
    )
    .add("off", () =>
        React.createElement(() => {
            const [checked, setChecked] = useState<boolean | undefined>(false);
            return (
                <BloomCheckbox
                    label=" Click me"
                    checked={checked}
                    onCheckChanged={newState => setChecked(newState)}
                    l10nKey="bogus"
                />
            );
        })
    )
    .add("on", () =>
        React.createElement(() => {
            const [checked, setChecked] = useState<boolean | undefined>(true);
            return (
                <BloomCheckbox
                    label=" Click me"
                    checked={checked}
                    onCheckChanged={newState => setChecked(newState)}
                    l10nKey="bogus"
                />
            );
        })
    )
    .add("tristate", () =>
        React.createElement(() => {
            const [checked, setChecked] = useState<boolean | undefined>(
                undefined
            );
            return (
                <BloomCheckbox
                    label=" Click me"
                    checked={checked}
                    tristate={true}
                    onCheckChanged={newState => setChecked(newState)}
                    l10nKey="bogus"
                />
            );
        })
    );
storiesOf("BloomCheckbox/ApiCheckbox", module).add("ApiCheckbox", () =>
    React.createElement(() => (
        <ApiCheckbox
            english="Motion Book"
            l10nKey="PublishTab.Android.MotionBookMode"
            apiEndpoint="publish/bloompub/motionBookMode"
        />
    ))
);
