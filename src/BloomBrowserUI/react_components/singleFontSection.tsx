import { css } from "@emotion/react";

import * as React from "react";
import { postData } from "../utils/bloomApi";
import FontSelectComponent, {
    IFontMetaData,
} from "../bookEdit/StyleEditor/fontSelectComponent";
import { Link } from "./link";
import { useL10n } from "./l10nHooks";
import { Typography } from "@mui/material";

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
        <React.Fragment>
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
                css={css`
                    width: 200px;
                    margin-top: 0 !important;
                `}
            />
            <Link
                css={css`
                    text-decoration: underline !important;
                    margin-bottom: 16px !important;
                `}
                l10nKey="CollectionSettingsDialog.BookMakingTab.SpecialScriptSettingsLink"
                onClick={() => {
                    postData("settings/specialScriptSettings", linkData);
                }}
            >
                Special Script Settings
            </Link>
        </React.Fragment>
    );
};

export default SingleFontSection;
