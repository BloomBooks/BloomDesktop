import { css } from "@emotion/react";

import * as React from "react";
import { FormControlLabel, Radio } from "@mui/material";
import { useL10n } from "./l10nHooks";
import { ILocalizationProps } from "./l10nComponents";

// wrap up the complex material-ui radio control in something simple; and make it localizable
export const MuiRadio: React.FunctionComponent<
    ILocalizationProps & {
        label: string;
        // An optional, already-localized secondary line shown in smaller, muted text
        // beneath the main label (e.g. to explain the consequence of the choice).
        description?: string;
        value?: string;
        disabled?: boolean;
        onChanged?: (v: boolean | undefined) => void;
    }
> = (props) => {
    const localizedLabel = useL10n(
        props.label,
        props.alreadyLocalized || props.temporarilyDisableI18nWarning
            ? null
            : props.l10nKey,
        props.l10nComment,
        props.l10nParam0,
        props.l10nParam1,
    );

    const labelContent =
        props.description === undefined ? (
            localizedLabel
        ) : (
            <div
                css={css`
                    display: flex;
                    flex-direction: column;
                `}
            >
                <span>{localizedLabel}</span>
                <span
                    css={css`
                        font-size: 0.85em;
                        opacity: 0.7;
                    `}
                >
                    {props.description}
                </span>
            </div>
        );

    // Work has been done below to ensure that a multiline (wrapped) label will align with the control correctly.
    // If messing with the layout, be sure you didn't break this by checking the storybook story.
    return (
        <FormControlLabel
            css={css`
                align-items: baseline !important; // !important needed to override MUI default
            `}
            control={
                // Without this empty div, the vertical alignment between the button and the label is wrong
                <div>
                    <Radio
                        css={css`
                            margin-top: -1px !important;
                        `}
                        value={props.value}
                        disabled={props.disabled}
                        onChange={(e, newState) => {
                            if (props.onChanged) {
                                props.onChanged(newState);
                            }
                        }}
                        color="primary"
                    />
                </div>
            }
            label={labelContent}
        />
    );
};
