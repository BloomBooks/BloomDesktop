import * as React from "react";
import { useL10n } from "../../react_components/l10nHooks";
import { BloomApi } from "../../utils/bloomApi";
import { LanguageSelectionSettingsGroup } from "./LanguageSelectionSettingsGroup";
import "./PublishLanguagesGroup.less";

// NOTE: Must correspond to C#"s NameRec
export interface INameRec {
    code: string;
    name: string;
    complete: boolean;
    includeText: boolean;
    includeAudio: boolean;
}

class NameRec implements INameRec {
    public code: string;
    public name: string;
    public complete: boolean;
    public includeText: boolean;
    public includeAudio: boolean;

    public constructor(other?: INameRec | undefined) {
        if (!other) {
            // Default constructor.
            // Nothing needs to happen right now.
        } else {
            // Copy constructor
            this.code = other.code;
            this.name = other.name;
            this.complete = other.complete;
            this.includeText = other.includeText;
            this.includeAudio = other.includeAudio;
        }
    }
}

// Component that shows a check box for each language in the book, allowing the user to
// control which of them to include in the published book.
export const PublishLanguagesGroup: React.FunctionComponent<{
    onChange?: () => void;
}> = props => {
    const initialValue: INameRec[] = [];
    const [langs, setLangs] = React.useState(initialValue);
    React.useEffect(() => {
        BloomApi.get(
            "publish/android/languagesInBook",

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
                // That's why these are INameRec's (interface) instead of NameRec's (class)
                setLangs(newLangs as INameRec[]);
            }

            // onError
            // Currently just ignoring errors... letting BloomServer take care of reporting anything that comes up
            // () => {
            // }
        );
    }, []);

    return (
        <div>
            <LanguageSelectionSettingsGroup
                label={useL10n(
                    "Text Languages",
                    "PublishTab.Android.TextLanguages"
                )}
                langs={langs}
                setLangs={setLangs}
                shouldBoxBeDisabled={() => false}
                shouldBoxBeChecked={lang => lang.includeText}
                createUpdatedItem={(item: INameRec, newState: boolean) => {
                    const newItem = new NameRec(item);
                    newItem.includeText = newState;
                    return newItem;
                }}
                onChange={props.onChange}
            ></LanguageSelectionSettingsGroup>
            <LanguageSelectionSettingsGroup
                label={useL10n(
                    "Talking Book Languages",
                    "PublishTab.Android.TalkingBookLanguages"
                )}
                langs={langs}
                setLangs={setLangs}
                shouldBoxBeDisabled={lang => !lang.includeText}
                shouldBoxBeChecked={lang =>
                    lang.includeText && lang.includeAudio
                }
                createUpdatedItem={(item: INameRec, newState: boolean) => {
                    const newItem = new NameRec(item);
                    newItem.includeAudio = newState;
                    return newItem;
                }}
                onChange={props.onChange}
            ></LanguageSelectionSettingsGroup>
        </div>
    );
};
