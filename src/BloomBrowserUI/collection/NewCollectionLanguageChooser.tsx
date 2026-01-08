import {
    LanguageChooser,
    IOrthography,
    defaultSearchResultModifier,
} from "@ethnolib/language-chooser-react-mui";
import * as React from "react";

import { WireUpForWinforms } from "../utils/WireUpWinform";
import { get, postData } from "../utils/bloomApi";
import { getLanguageData } from "./LanguageChooserDialog";
import { useSubscribeToWebSocketForStringMessage } from "../utils/WebSocketManager";

const NewCollectionLanguageChooser: React.FunctionComponent = () => {
    function onSelectionChange(
        languageSelection: IOrthography | undefined,
        languageTag: string | undefined,
    ) {
        postData(
            "newCollection/selectLanguage",
            getLanguageData(languageTag, languageSelection),
        );
    }

    const [uiLanguage, setUiLanguage] = React.useState("en");
    React.useEffect(() => {
        get("currentUiLanguage", (result) => {
            setUiLanguage(result.data);
        });
    }, []);

    useSubscribeToWebSocketForStringMessage(
        "app",
        "uiLanguageChanged",
        setUiLanguage,
    );

    return (
        <LanguageChooser
            uiLanguage={uiLanguage}
            searchResultModifier={defaultSearchResultModifier}
            onSelectionChange={onSelectionChange}
        />
    );
};

export default NewCollectionLanguageChooser;

WireUpForWinforms(NewCollectionLanguageChooser);
