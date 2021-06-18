import { FormGroup, Checkbox, FormControlLabel } from "@material-ui/core";
import * as React from "react";
import { useL10n } from "../../react_components/l10nHooks";
import { BloomApi } from "../../utils/bloomApi";
import { SettingsGroup } from "../commonPublish/BasePublishScreen";
import { INameRec } from "./PublishLanguagesGroup";

export const LanguageSelectionSettingsGroup: React.FunctionComponent<{
    label: string;
    langs: INameRec[];
    setLangs: (value) => void;
    shouldBoxBeDisabled: (lang: INameRec) => boolean;
    shouldBoxBeChecked: (lang: INameRec) => boolean;
    createUpdatedItem: (item: INameRec, newState: boolean) => INameRec;
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
