import * as React from "react";
import { useL10n } from "../../react_components/l10nHooks";
import { get, post } from "../../utils/bloomApi";
import {
    LangCheckboxValue,
    LanguageSelectionSettingsGroup
} from "./LanguageSelectionSettingsGroup";

// NOTE: Must correspond to C#"s LanguagePublishInfo
export interface ILanguagePublishInfo {
    code: string;
    name: string;
    complete: boolean;
    includeText: boolean;
    containsAnyAudio: boolean;
    includeAudio: boolean;
    required: boolean;
}

class LanguagePublishInfo implements ILanguagePublishInfo {
    public code: string;
    public name: string;
    public complete: boolean;
    public includeText: boolean;
    public containsAnyAudio: boolean;
    public includeAudio: boolean;
    public required: boolean;

    public constructor(other?: ILanguagePublishInfo | undefined) {
        if (!other) {
            // Default constructor.
            // Nothing needs to happen right now.
        } else {
            // Copy constructor
            this.code = other.code;
            this.name = other.name;
            this.complete = other.complete;
            this.includeText = other.includeText;
            this.containsAnyAudio = other.containsAnyAudio;
            this.includeAudio = other.includeAudio;
            this.required = other.required;
        }
    }
}

// Component that shows a check box for each language in the book, allowing the user to
// control which of them to include in the published book.
export const PublishLanguagesGroup: React.FunctionComponent<{
    onChange?: () => void;
}> = props => {
    const initialValue: ILanguagePublishInfo[] = [];
    const [langs, setLangs] = React.useState(initialValue);
    React.useEffect(() => {
        get(
            "publish/languagesInBook",

            // onSuccess
            result => {
                let newLangs = result.data;
                // This is for debugging. When all is well, the JSON gets parsed automatically.
                // If there's a syntax error in the JSON, result.data is just the string.
                // Trying to parse it ourselves at least gets the syntax error into our log/debugger.
                if (!newLangs.map) {
                    newLangs = JSON.parse(newLangs);
                }

                // Note that these are just simple objects with fields, not instances of classes with methods.
                // That's why these are ILanguagePublishInfo's (interface) instead of LanguagePublishInfo's (class)
                setLangs(newLangs as ILanguagePublishInfo[]);
            }

            // onError
            // Currently just ignoring errors... letting BloomServer take care of reporting anything that comes up
            // () => {
            // }
        );
    }, []);

    const checkboxValuesForTextLangs = langs.map(item => {
        return {
            code: item.code,
            name: item.name,
            warnIncomplete: !item.complete,
            isEnabled: !item.required,
            isChecked: item.includeText,
            required: item.required
        };
    });

    const checkboxValuesForAudioLangs = langs.map(item => {
        return {
            code: item.code,
            name: item.name,
            warnIncomplete: false, // Only show for text checkboxes
            isEnabled: item.includeText && item.containsAnyAudio,
            isChecked:
                item.includeText && item.containsAnyAudio && item.includeAudio
        };
    });

    const onLanguageUpdated = (
        item: LangCheckboxValue,
        newState: boolean,
        fieldToUpdate: string
    ) => {
        setLangs(
            langs.map(lang => {
                if (lang.code === item.code) {
                    const newLangObj = new LanguagePublishInfo(lang);
                    newLangObj[fieldToUpdate] = newState;

                    post(
                        `publish/includeLanguage?langCode=${newLangObj.code}&${fieldToUpdate}=${newState}`
                    );

                    return newLangObj;
                } else {
                    return lang;
                }
            })
        );

        if (props.onChange) {
            props.onChange();
        }
    };

    const showAudioLanguageCheckboxes =
        checkboxValuesForAudioLangs?.filter(item => item.isEnabled).length > 0;

    return (
        <div>
            <LanguageSelectionSettingsGroup
                forAudioLanguages={false}
                langCheckboxValues={checkboxValuesForTextLangs}
                onChange={(item, newState: boolean) => {
                    onLanguageUpdated(item, newState, "includeText");
                }}
            />
            {showAudioLanguageCheckboxes ? (
                <LanguageSelectionSettingsGroup
                    forAudioLanguages={true}
                    langCheckboxValues={checkboxValuesForAudioLangs}
                    onChange={(item, newState: boolean) => {
                        onLanguageUpdated(item, newState, "includeAudio");
                    }}
                />
            ) : (
                ""
            )}
        </div>
    );
};
