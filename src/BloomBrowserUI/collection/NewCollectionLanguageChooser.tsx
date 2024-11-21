/** @jsxImportSource @emotion/react */
import {
    LanguageChooser,
    IOrthography,
    defaultSearchResultModifier,
    defaultDisplayName
} from "@ethnolib/language-chooser-react-mui";
import * as React from "react";

import { WireUpForWinforms } from "../utils/WireUpWinform";
import { postData } from "../utils/bloomApi";

const NewCollectionLanguageChooser: React.FunctionComponent = () => {
    function onSelectionChange(
        languageSelection: IOrthography | undefined,
        languageTag: string | undefined
    ) {
        postData("newCollection/selectLanguage", {
            LanguageTag: languageTag || null,
            DefaultName:
                // TODO when published from language-chooser-react-mui, we should use
                // defaultDisplayName(languageSelection)
                languageSelection?.language.autonym ||
                languageSelection?.language.exonym ||
                null,
            DesiredName: languageSelection?.customDetails?.displayName || null
        });
    }
    return (
        <LanguageChooser
            searchResultModifier={defaultSearchResultModifier}
            onSelectionChange={onSelectionChange}
        />
    );
};

export default NewCollectionLanguageChooser;

WireUpForWinforms(NewCollectionLanguageChooser);
