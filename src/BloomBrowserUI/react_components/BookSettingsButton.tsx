import { css } from "@emotion/react";
import * as React from "react";
import { TopBarButton } from "./TopBarButton";
import { getBloomApiPrefix } from "../utils/bloomApi";
import { showBookSettingsDialog } from "../bookEdit/bookAndPageSettings/BookAndPageSettingsDialog";
import { getCurrentPageElement } from "../bookEdit/bookAndPageSettings/PageSettingsConfigrPages";
import {
    kBloomPurple,
    kDisabledTextOnPurple,
    kTextOnPurple,
} from "../bloomMaterialUITheme";

const bookSettingsIconPath = `${getBloomApiPrefix(false)}images/book-settings.png`;

export const getInitialBookSettingsPageKey = (): string => {
    try {
        return getCurrentPageElement().classList.contains("cover")
            ? "cover"
            : "themeAndLayout";
    } catch {
        return "themeAndLayout";
    }
};

export const BookSettingsButton: React.FunctionComponent = (props) => {
    const handleClick = React.useCallback(() => {
        const pageKey = getInitialBookSettingsPageKey();
        showBookSettingsDialog(pageKey);
    }, []);

    return (
        <TopBarButton
            iconPath={bookSettingsIconPath}
            labelL10nKey="BookAndPageSettings.Title"
            labelEnglish="Book and Page Settings"
            onClick={handleClick}
            backgroundColor={kBloomPurple}
            textColor={kTextOnPurple}
            disabledTextColor={kDisabledTextOnPurple}
            cssOverrides={css`
                min-width: 88px;
                max-width: 124px;
                white-space: normal;
                line-height: 1.15;

                span {
                    display: -webkit-box;
                    -webkit-box-orient: vertical;
                    -webkit-line-clamp: 2;
                    overflow: hidden;
                    max-width: 108px;
                }
            `}
        />
    );
};
