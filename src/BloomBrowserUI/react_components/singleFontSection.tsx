import { css } from "@emotion/react";

import * as React from "react";
import { postData } from "../utils/bloomApi";
import FontSelectComponent, {
    IFontMetaData,
} from "../bookEdit/StyleEditor/fontSelectComponent";
import { Link } from "./link";
import { useL10n } from "./l10nHooks";
import { Typography } from "@mui/material";
import KeyboardSection from "./keyboardSection";
import {
    bookMakingSelectCss,
    kBookMakingSectionGap,
} from "../collection/commonTabSettings";

const SingleFontSection: React.FunctionComponent<{
    languageNumber: number;
    languageName: string;
    currentFontName: string;
    fontMetadata?: IFontMetaData[];
}> = (props) => {
    const linkData = {
        languageNumber: props.languageNumber,
        languageName: props.languageName,
    };

    const fontChangeHandler = (fontName: string) => {
        postData("settings/setFontForLanguage", {
            languageNumber: props.languageNumber,
            fontName: fontName,
        });
    };

    const defaultFontMessage = useL10n(
        "Default Font for {0}",
        "CollectionSettingsDialog.BookMakingTab.DefaultFontFor",
        "{0} is a language name.",
        props.languageName,
    );

    return (
        // The bottom gap separates this language's block from the next language
        // (and the last block from the Page Numbering Style below the list).
        <div
            css={css`
                margin-bottom: ${kBookMakingSectionGap};
            `}
        >
            <Typography
                css={css`
                    font-weight: 700 !important;
                `}
            >
                {defaultFontMessage}
            </Typography>
            <FontSelectComponent
                languageNumber={props.languageNumber}
                fontMetadata={props.fontMetadata}
                currentFontName={props.currentFontName}
                onChangeFont={fontChangeHandler}
                css={[
                    bookMakingSelectCss,
                    css`
                        margin-top: 0 !important;
                    `,
                ]}
            />
            {/* Keyboard comes right under the font select; Special Script
                Settings is the last item in the block.
                L4+ languages don't get a row here (v1); this component is only
                instantiated for languages 1-3 (see fontScriptSettingsControl.tsx). */}
            <KeyboardSection
                languageNumber={props.languageNumber}
                languageName={props.languageName}
            />
            <Link
                css={css`
                    display: block;
                    text-decoration: underline !important;
                    margin-top: 4px !important;
                `}
                l10nKey="CollectionSettingsDialog.BookMakingTab.SpecialScriptSettingsLink"
                onClick={() => {
                    postData("settings/specialScriptSettings", linkData);
                }}
            >
                Special Script Settings
            </Link>
        </div>
    );
};

export default SingleFontSection;
