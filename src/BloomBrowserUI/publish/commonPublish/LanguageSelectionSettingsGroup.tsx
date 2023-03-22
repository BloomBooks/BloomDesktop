/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import { FormGroup, Checkbox, FormControlLabel } from "@mui/material";
import * as React from "react";
import { useL10n } from "../../react_components/l10nHooks";
import { SettingsGroup } from "../commonPublish/PublishScreenBaseComponents";

export interface LangCheckboxValue {
    code; // the language code
    name; // the language name
    warnIncomplete;
    isEnabled;
    isChecked;
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

    const languageCheckboxes = props.langCheckboxValues.map(item => (
        <FormControlLabel
            key={item.code}
            className="languageLabel"
            control={
                <Checkbox
                    disabled={!item.isEnabled}
                    checked={item.isChecked}
                    onChange={(e, newState) => {
                        if (props.onChange) {
                            props.onChange(item, newState);
                        }
                    }}
                    color="primary"
                />
            }
            label={
                <div className="check-box-label">
                    <div>{item.name}</div>
                    {item.warnIncomplete && (
                        <div className="incompleteTranslation">
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
                        overflow-y: auto;
                        max-height: 150px;
                        // The 'important' overrides the MUI default so we only get one column of checkboxes.
                        flex-wrap: nowrap !important;
                        margin-right: 20px;
                        // These two "cancel out" except that they defeat a bug in the HTML spec that prevents
                        // overflow-x from being visible when overflow-y is hidden. The check box shadows can
                        // 'overflow' from the normal space occupied by the scrollingFeature.
                        padding-left: 11px;
                        margin-left: -11px;
                    `}
                >
                    {languageCheckboxes}
                </FormGroup>
            </SettingsGroup>
        </div>
    );
};
