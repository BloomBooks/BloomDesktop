import * as React from "react";
import {
    LangCheckboxValue,
    LanguageSelectionSettingsGroup
} from "../ReaderPublish/LanguageSelectionSettingsGroup";
import { get, post } from "../../utils/bloomApi";
import {
    ILanguagePublishInfo,
    LanguagePublishInfo
} from "../ReaderPublish/PublishLanguagesGroup";
import { useL10n } from "../../react_components/l10nHooks";

export const LanguageGroup: React.FunctionComponent = () => {
    const [langs, setLangs] = React.useState<ILanguagePublishInfo[]>([]);
    React.useEffect(() => {
        get(
            "libraryPublish/languagesInBook",

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
            isEnabled: !item.isL1,
            isChecked: item.includeText
        };
    });

    // Copied from similar control in BloomPub screen, this will handle individual controls
    // for narration languages by giving a different fieldToUpdate. Currently we only use
    // one value for that.
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
                        `libraryPublish/includeLanguage?langCode=${newLangObj.code}&${fieldToUpdate}=${newState}`
                    );

                    return newLangObj;
                } else {
                    return lang;
                }
            })
        );
    };
    return (
        <LanguageSelectionSettingsGroup
            label={useL10n("Include Text", "PublishTab.Upload.IncludeText")}
            langCheckboxValues={checkboxValuesForTextLangs}
            onChange={(item, newState: boolean) => {
                onLanguageUpdated(item, newState, "includeText");
            }}
        />
    );
};
