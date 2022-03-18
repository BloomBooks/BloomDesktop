/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { BloomApi } from "../utils/bloomApi";
import FontSelectComponent, {
    IFontMetaData
} from "../bookEdit/StyleEditor/fontSelectComponent";
import { Div } from "./l10nComponents";
import { Link } from "./link";

const SingleFontSection: React.FunctionComponent<{
    languageNumber: number;
    languageName: string;
    currentFontName: string;
    fontMetadata?: IFontMetaData[];
}> = props => {
    const linkData = {
        languageNumber: props.languageNumber,
        languageName: props.languageName
    };

    const fontChangeHandler = (fontName: string) => {
        BloomApi.postData("settings/setFontForLanguage", {
            languageNumber: props.languageNumber,
            fontName: fontName
        });
    };

    return (
        <React.Fragment>
            <div
                css={css`
                    font-family: "Segoe UI Semibold";
                `}
            >
                <Div
                    l10nKey="CollectionSettingsDialog.BookMakingTab.DefaultFontFor"
                    l10nParam0={props.languageName}
                >
                    Default Font for {0}
                </Div>
            </div>
            <FontSelectComponent
                key={props.languageNumber}
                fontMetadata={props.fontMetadata}
                currentFontName={props.currentFontName}
                // The popover will left-justify itself, since there are blocking controls to the right.
                // Once the whole tab becomes a React control, we can probably do away with this.
                anchorPopoverLeft={true}
                onChangeFont={fontChangeHandler}
                css={css`
                    width: 200px;
                    margin-top: 0 !important;
                `}
            />
            <Link
                css={css`
                    text-decoration: underline !important;
                    color: blue !important;
                    margin-bottom: 16px !important;
                `}
                l10nKey="CollectionSettingsDialog.BookMakingTab.SpecialScriptSettingsLink"
                onClick={() => {
                    BloomApi.postData(
                        "settings/specialScriptSettings",
                        linkData
                    );
                }}
            >
                Special Script Settings
            </Link>
        </React.Fragment>
    );
};

export default SingleFontSection;
