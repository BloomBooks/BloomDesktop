/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import { FormGroup, Checkbox, FormControlLabel } from "@mui/material";
import * as React from "react";
import { useL10n } from "../../react_components/l10nHooks";
import { SettingsGroup } from "../commonPublish/PublishScreenBaseComponents";
import { BloomCheckbox } from "../../react_components/BloomCheckBox";
import { BloomTooltip } from "../../react_components/BloomToolTip";

export interface LangCheckboxValue {
    code; // the language code
    name; // the language name
    warnIncomplete;
    isEnabled;
    isChecked;
    required?: boolean;
}

export const LanguageSelectionSettingsGroup: React.FunctionComponent<{
    forAudioLanguages: boolean;

    // The state of the checkboxes.
    langCheckboxValues: LangCheckboxValue[];

    // If defined, will be invoked after the checkbox changes its value.
    onChange?: (item: LangCheckboxValue, newState: boolean) => void;
}> = props => {
    const incompleteTag = useL10n(
        "(incomplete translation)",
        "PublishTab.Upload.IncompleteTranslation"
    );
    const talkingLabel = useL10n(
        "Talking Book Languages",
        "PublishTab.Android.TalkingBookLanguages"
    );

    const textLabel = useL10n(
        "Text Languages",
        "PublishTab.Android.TextLanguages"
    );

    const tooltipStrings = {
        enabled: {
            text: {
                l10nKey: "PublishTab.Language.Enabled"
            },
            audio: {
                l10nKey: "PublishTab.AudioLanguage.Enabled"
            }
        },
        disabled: {
            text:
                "This situation does not happen; we don't language unless they are enabled or required.",
            audio: {
                l10nKey: "PublishTab.AudioLanguage.Disabled"
            }
        },
        required: {
            text: {
                l10nKey: "PublishTab.Language.Required"
            }
        }
    };

    //: undefined // at the moment isn't ever possible. But maybe in the future we'll have a case where a language is disabled for another reason.
    const languageCheckboxes = props.langCheckboxValues.map(item => (
        <BloomTooltip
            key={item.code}
            showDisabled={!item.isEnabled}
            tip={
                tooltipStrings.enabled[
                    props.forAudioLanguages ? "audio" : "text"
                ]
            }
            tipWhenDisabled={
                item.required
                    ? tooltipStrings.required.text // audio is never required
                    : tooltipStrings.disabled[
                          props.forAudioLanguages ? "audio" : "text"
                      ]
            }
        >
            <BloomCheckbox
                disabled={!item.isEnabled}
                checked={item.isChecked}
                onCheckChanged={newState => {
                    if (props.onChange) {
                        props.onChange(item, newState!);
                    }
                }}
                label={
                    <div>
                        <div>{item.name}</div>
                        {item.warnIncomplete && (
                            <div
                                css={css`
                                    color: grey;
                                    font-size: smaller;
                                `}
                            >
                                {incompleteTag}
                            </div>
                        )}
                    </div>
                }
            />
        </BloomTooltip>
    ));

    return (
        <div className="publishLanguagesGroup">
            <SettingsGroup
                label={props.forAudioLanguages ? talkingLabel : textLabel}
            >
                <FormGroup
                    css={css`
                        // The 'important' overrides the MUI default so we only get one column of checkboxes.
                        flex-wrap: nowrap !important;
                        // These two "cancel out" except that they defeat a bug in the HTML spec that prevents
                        // overflow-x from being visible when overflow-y is hidden. The check box shadows can
                        // 'overflow' from the normal space occupied by the scrollingFeature.
                        // [JH, later] I don't exactly know what the problem was, but we don't have scrolling any more in this
                        // area and this was causing problems. Leaving here in a comment in case we need to revisit.
                        /* padding-left: 11px;
                        margin-left: -11px; */
                    `}
                >
                    {languageCheckboxes}
                </FormGroup>
            </SettingsGroup>
        </div>
    );
};
