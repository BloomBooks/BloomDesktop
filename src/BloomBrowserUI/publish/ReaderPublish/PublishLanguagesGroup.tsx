import * as React from "react";
import { FormGroup, Checkbox, FormControlLabel } from "@material-ui/core";
import { ApiCheckbox } from "../../react_components/ApiCheckbox";
import { SettingsGroup } from "../commonPublish/BasePublishScreen";
import { useL10n } from "../../react_components/l10nHooks";
import { BloomApi } from "../../utils/bloomApi";
import { MuiCheckbox } from "../../react_components/muiCheckBox";

class NameRec {
    public code: string;
    public name: string;
    public complete: boolean;
    public include: boolean;
}

// Component that shows a check box for each language in the book, allowing the user to
// control which of them to include in the published book.
export const PublishLanguagesGroup: React.FunctionComponent<{
    onChange?: () => void;
}> = props => {
    const initialValue: NameRec[] = [];
    const [langs, setLangs] = React.useState(initialValue);
    const incomplete = useL10n(
        "(incomplete translation)",
        "PublishTab.Upload.IncompleteTranslation"
    );
    React.useEffect(() => {
        BloomApi.get("publish/android/languagesInBook", result => {
            let newLangs = result.data;
            // This is for debugging. When all is well, the JSON gets parsed automatically.
            // If there's a syntax error in the JSON, result.data is just the string.
            // Trying to parse it ourselves at least gets the syntax error into our log/debugger.
            if (!newLangs.map) {
                newLangs = JSON.parse(newLangs);
            }
            setLangs(newLangs as NameRec[]);
        });
    }, []);
    const languageCheckboxes = langs.map(item => (
        <FormControlLabel
            key={item.code}
            className={
                "languageLabel" +
                (item.complete ? "" : " incompleteTranslation")
            }
            control={
                <Checkbox
                    checked={item.include}
                    onChange={(e, newState) => {
                        // May not actually need this...currently props.onChange() triggers
                        // a complete regeneration of the page.
                        setLangs(
                            langs.map(lang =>
                                lang.code === item.code
                                    ? {
                                          code: item.code,
                                          name: item.name,
                                          complete: item.complete,
                                          include: newState
                                      }
                                    : lang
                            )
                        );
                        BloomApi.post(
                            "publish/android/includeLanguage?langCode=" +
                                item.code +
                                "&include=" +
                                (newState ? "true" : "false")
                        );
                        if (props.onChange) {
                            props.onChange();
                        }
                    }}
                    color="primary"
                />
            }
            label={item.name + (item.complete ? "" : " " + incomplete)}
        />
    ));
    return (
        <SettingsGroup
            label={useL10n(
                "Text Languages",
                "PublishTab.Android.TextLanguages"
            )}
        >
            <FormGroup className="scrollingFeature">
                {languageCheckboxes}
            </FormGroup>
        </SettingsGroup>
    );
};
