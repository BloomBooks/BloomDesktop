import { FormGroup, Checkbox, FormControlLabel } from "@material-ui/core";
import * as React from "react";
import { useL10n } from "../../react_components/l10nHooks";
import { BloomApi } from "../../utils/bloomApi";
import { SettingsGroup } from "../commonPublish/BasePublishScreen";
import { INameRec } from "./PublishLanguagesGroup";

export const LanguageSelectionSettingsGroup: React.FunctionComponent<{
    // The label (heading) of this settings group.
    label: string;
    // An array containing the state of the languages for this book
    langs: INameRec[];
    // The setter for langs from the useState hook.
    setLangs: (value) => void;
    // A function which, given the lang's state, returns whether the checkbox should be enabled or not
    shouldBoxBeDisabled: (lang: INameRec) => boolean;
    // A function which, given the lang's state, returns whether the checkbox should be checked or not
    shouldBoxBeChecked: (lang: INameRec) => boolean;
    // A function which, given an old item and the checkbox's new value, creates a modified copy of the item with the new state applied appropriately
    // (e.g. to whatever field the checkbox is supposed to represent)
    createUpdatedItem: (item: INameRec, newState: boolean) => INameRec;
    // If defined, will be invoked after the checkbox changes its value.
    onChange?: () => void;
}> = props => {
    const incomplete = useL10n(
        "(incomplete translation)",
        "PublishTab.Upload.IncompleteTranslation"
    );

    const languageCheckboxes = props.langs.map(item => (
        <FormControlLabel
            key={item.code}
            className="languageLabel"
            control={
                <Checkbox
                    disabled={props.shouldBoxBeDisabled(item)}
                    checked={props.shouldBoxBeChecked(item)}
                    onChange={(e, newState) => {
                        const newItem = props.createUpdatedItem(item, newState);
                        // May not actually need this...currently props.onChange() triggers
                        // a complete regeneration of the page.
                        props.setLangs(
                            props.langs.map(lang =>
                                lang.code === item.code ? newItem : lang
                            )
                        );

                        BloomApi.post(
                            `publish/android/includeLanguage?langCode=${newItem.code}&includeText=${newItem.includeText}&includeAudio=${newItem.includeAudio}`
                        );

                        if (props.onChange) {
                            props.onChange();
                        }
                    }}
                    color="primary"
                />
            }
            label={
                <div className="check-box-label">
                    <div>{item.name}</div>
                    {item.complete || (
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
                <FormGroup className="scrollingFeature">
                    {languageCheckboxes}
                </FormGroup>
            </SettingsGroup>
        </div>
    );
};
