import * as React from "react";
import { BloomCheckbox } from "./BloomCheckBox";
import { useState } from "react";
import {
    FormControlLabel,
    Checkbox as OriginalMuiCheckbox,
} from "@mui/material";
import { VisuallyImpairedIcon } from "./icons/VisuallyImpairedIcon";
import { MotionIcon } from "./icons/MotionIcon";

const kLongText =
    "Bacon ipsum dolor amet ribeye spare ribs bresaola t-bone. Strip steak turkey shankle pig ground round, biltong t-bone kevin alcatra flank ribeye beef ribs meatloaf filet mignon. Buffalo ham t-bone short ribs.";

export default {
    title: "BloomCheckbox",
};

export const Various = () =>
    React.createElement(() => {
        const [checked, setChecked] = useState<boolean | undefined>(false);
        return (
            <React.Fragment>
                <strong>
                    The original purpose of this story was to compare the
                    vertical alignment of the text to the checkbox.
                </strong>
                <hr />
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
                    <hr />
                    <BloomCheckbox
                        label={"With Icon " + kLongText}
                        icon={<VisuallyImpairedIcon />}
                        checked={checked}
                        tristate={true}
                        onCheckChanged={(newState) => setChecked(newState)}
                        l10nKey="bogus"
                    />
                    <div>
                        <BloomCheckbox
                            label={"hideBox, checked"}
                            checked={true}
                            hideBox={true}
                            onCheckChanged={() => {}}
                            l10nKey="bogus"
                        />
                    </div>
                    <div>
                        <BloomCheckbox
                            label={"hideBox, not checked, no icon"}
                            checked={false}
                            hideBox={true}
                            onCheckChanged={() => {}}
                            l10nKey="bogus"
                        />
                    </div>
                    <div>
                        <BloomCheckbox
                            label={"hideBox, not checked, with icon"}
                            icon={<MotionIcon />}
                            checked={false}
                            hideBox={true}
                            onCheckChanged={() => {}}
                            l10nKey="bogus"
                        />
                    </div>
                </div>
            </React.Fragment>
        );
    });

export const Off = () =>
    React.createElement(() => {
        const [checked, setChecked] = useState<boolean | undefined>(false);
        return (
            <BloomCheckbox
                label=" Click me"
                checked={checked}
                onCheckChanged={(newState) => setChecked(newState)}
                l10nKey="bogus"
            />
        );
    });

Off.story = {
    name: "off",
};

export const On = () =>
    React.createElement(() => {
        const [checked, setChecked] = useState<boolean | undefined>(true);
        return (
            <BloomCheckbox
                label=" Click me"
                checked={checked}
                onCheckChanged={(newState) => setChecked(newState)}
                l10nKey="bogus"
            />
        );
    });

On.story = {
    name: "on",
};

export const Tristate = () =>
    React.createElement(() => {
        const [checked, setChecked] = useState<boolean | undefined>(undefined);
        return (
            <BloomCheckbox
                label=" Click me"
                checked={checked}
                tristate={true}
                onCheckChanged={(newState) => setChecked(newState)}
                l10nKey="bogus"
            />
        );
    });

Tristate.story = {
    name: "tristate",
};
