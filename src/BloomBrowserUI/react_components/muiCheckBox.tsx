/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { useState } from "react";
import { FormControlLabel, Checkbox } from "@material-ui/core";
import { useL10n } from "./l10nHooks";

// wrap up the complex material-ui checkbox in something simple and make it handle tristate
export const MuiCheckbox: React.FunctionComponent<{
    className?: string;
    label: string;
    l10nKey: string;
    l10nComment?: string;
    checked: boolean | undefined;
    tristate?: boolean;
    disabled?: boolean;
    alreadyLocalized?: boolean;
    temporarilyDisableI18nWarning?: boolean;
    onCheckChanged: (v: boolean | undefined) => void;
    l10nParam0?: string;
    l10nParam1?: string;
    // The original version of this didn't properly handle multiline (wrapped) labels.
    // To make multiline labels work, we had to add an extra div layer.
    // That broke some existing usages. When I started trying to fix them,
    // I ended up deep in the rabbit hole, so I punted and added this prop instead.
    // Ideally, we would fix those and get rid of this parameter.
    deprecatedVersionWhichDoesntEnsureMultilineLabelsWork?: boolean;
}> = props => {
    const [previousTriState, setPreviousTriState] = useState<
        boolean | undefined
    >(props.checked);

    const localizedLabel = useL10n(
        props.label,
        props.alreadyLocalized || props.temporarilyDisableI18nWarning
            ? null
            : props.l10nKey,
        props.l10nComment,
        props.l10nParam0,
        props.l10nParam1
    );

    // Work has been done below to ensure that a wrapped label will align with the control correctly.
    // If messing with the layout, be sure you didn't break this by checking the storybook story.

    const checkboxControl = (
        <Checkbox
            css={
                !props.deprecatedVersionWhichDoesntEnsureMultilineLabelsWork
                    ? css`
                          margin-top: -1px !important;
                      `
                    : css``
            }
            className={props.className}
            disabled={props.disabled}
            checked={!!props.checked}
            indeterminate={props.checked == null}
            //enhance; I would like  it to show a square with a question mark inside: indeterminateIcon={"?"}
            onChange={(e, newState) => {
                if (!props.tristate) {
                    props.onCheckChanged(newState);
                } else {
                    let next: boolean | undefined = false;
                    switch (previousTriState) {
                        case null:
                            next = false;
                            break;
                        case true:
                            next = undefined;
                            break;
                        case false:
                            next = true;
                            break;
                    }
                    setPreviousTriState(next);
                    props.onCheckChanged(next);
                }
            }}
            color="primary"
        />
    );

    return (
        <FormControlLabel
            css={
                !props.deprecatedVersionWhichDoesntEnsureMultilineLabelsWork
                    ? css`
                          align-items: baseline !important; // !important needed to override MUI default
                      `
                    : css``
            }
            className={props.className}
            control={
                // Without this empty div, the vertical alignment between the button and the label is wrong
                // when the label wraps.
                !props.deprecatedVersionWhichDoesntEnsureMultilineLabelsWork ? (
                    <div>{checkboxControl}</div>
                ) : (
                    checkboxControl
                )
            }
            label={localizedLabel}
            disabled={props.disabled}
        />
    );
};
