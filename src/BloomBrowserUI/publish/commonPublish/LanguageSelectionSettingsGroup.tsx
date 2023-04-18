/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import { FormGroup, Checkbox, FormControlLabel } from "@mui/material";
import * as React from "react";
import { useL10n } from "../../react_components/l10nHooks";
import { SettingsGroup } from "../commonPublish/PublishScreenBaseComponents";
import { BloomCheckbox } from "../../react_components/BloomCheckBox";

export interface LangCheckboxValue {
    code; // the language code
    name; // the language name
    warnIncomplete;
    isEnabled;
    isChecked;
    required?: boolean;
}

export const LanguageSelectionSettingsGroup: React.FunctionComponent<{
    // The label (heading) of this settings group.
    label: string;

    // The state of the checkboxes.
    langCheckboxValues: LangCheckboxValue[];

    // If defined, will be invoked after the checkbox changes its value.
    onChange?: (item: LangCheckboxValue, newState: boolean) => void;
}> = props => {
    const incomplete = useL10n(
        "(incomplete translation)",
        "PublishTab.Upload.IncompleteTranslation"
    );
    const required = useL10n(
        "This language is required because it is currently shown in the book",
        "PublishTab.RequiredLanguage"
    );

    const languageCheckboxes = props.langCheckboxValues.map(item => (
        <BloomCheckbox
            key={item.code}
            tooltipContents={item.required ? required : undefined}
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
                            {incomplete}
                        </div>
                    )}
                </div>
            }
        />
    ));

    return (
        <div className="publishLanguagesGroup">
            <SettingsGroup label={props.label}>
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
