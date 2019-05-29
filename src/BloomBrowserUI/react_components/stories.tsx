import * as React from "react";
import { storiesOf } from "@storybook/react";
import { Expandable } from "./expandable";
import { Checkbox } from "./checkbox";
import { MuiCheckbox } from "./muiCheckBox";
import { useState } from "react";
import { ApiCheckbox } from "./ApiCheckbox";
import BloomButton from "./bloomButton";

storiesOf("Localizable Widgets", module)
    .add("Expandable", () => (
        <Expandable
            l10nKey="bogus"
            expandedHeight="30px"
            headingText="I am so advanced"
        >
            Look at this!
        </Expandable>
    ))
    .add("BloomButton", () => (
        <BloomButton
            l10nKey="bogus"
            l10nComment="hello"
            enabled={true}
            hasText={true}
        >
            Look at this!
        </BloomButton>
    ));

storiesOf("Localizable Widgets/Checkbox", module)
    .add("off", () => <Checkbox l10nKey="bogus">Click me</Checkbox>)
    .add("on", () => (
        <Checkbox checked={true} l10nKey="bogus">
            Click me
        </Checkbox>
    ))
    .add("indeterminate", () => (
        <Checkbox tristate={true} l10nKey="bogus">
            Click me
        </Checkbox>
    ));
// see https://github.com/storybooks/storybook/issues/5721

storiesOf("Localizable Widgets/MuiCheckbox", module)
    .add("off", () =>
        React.createElement(() => {
            const [checked, setChecked] = useState<boolean | null>(false);
            return (
                <MuiCheckbox
                    english=" Click me"
                    checked={checked}
                    onCheckChanged={newState => setChecked(newState)}
                    l10nKey="bogus"
                />
            );
        })
    )
    .add("on", () =>
        React.createElement(() => {
            const [checked, setChecked] = useState<boolean | null>(true);
            return (
                <MuiCheckbox
                    english=" Click me"
                    checked={checked}
                    onCheckChanged={newState => setChecked(newState)}
                    l10nKey="bogus"
                />
            );
        })
    )
    .add("indeterminate", () =>
        React.createElement(() => {
            const [checked, setChecked] = useState<boolean | null>(null);
            return (
                <MuiCheckbox
                    english=" Click me"
                    checked={checked}
                    tristate={true}
                    onCheckChanged={newState => setChecked(newState)}
                    l10nKey="bogus"
                />
            );
        })
    );
storiesOf("Localizable Widgets/ApiCheckbox", module).add("ApiCheckbox", () =>
    React.createElement(() => (
        <ApiCheckbox
            english="Motion Book"
            l10nKey="PublishTab.Android.MotionBookMode"
            apiEndpoint="publish/android/motionBookMode"
        />
    ))
);
