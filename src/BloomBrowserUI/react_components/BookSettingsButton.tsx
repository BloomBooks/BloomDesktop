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

const bookSettingsIconPath = `${getBloomApiPrefix(false)}images/BookAndPageSettings.svg`;

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
                width: 88px;
                white-space: normal;
                line-height: 1.15;

                span {
                    display: inline-block;
                    max-width: 72px;
                }

                // Recolor the (solid black) icon to black at 80% opacity so it matches
                // the other TopBar controls (help, language, zoom). brightness(0) blackens
                // it and opacity(0.8) composites the whole shape at 80%; the gear's center
                // hole stays transparent, so the purple bar shows through.
                img {
                    filter: brightness(0) opacity(0.8);
                }
            `}
        />
    );
};
