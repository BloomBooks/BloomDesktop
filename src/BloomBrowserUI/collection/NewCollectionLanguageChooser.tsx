/** @jsxImportSource @emotion/react */
import {
    LanguageChooser,
    IOrthography,
    defaultSearchResultModifier
} from "@ethnolib/language-chooser-react-mui";
import * as React from "react";

import { WireUpForWinforms } from "../utils/WireUpWinform";
import { postData } from "../utils/bloomApi";
import { getLanguageData } from "./LanguageChooserDialog";

const NewCollectionLanguageChooser: React.FunctionComponent = () => {
    function onSelectionChange(
        languageSelection: IOrthography | undefined,
        languageTag: string | undefined
    ) {
        postData(
            "newCollection/selectLanguage",
            getLanguageData(languageTag, languageSelection)
        );
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
