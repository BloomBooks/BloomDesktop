import { css } from "@emotion/react";
import * as React from "react";
import { TopBarButton } from "./TopBarButton";
import { getBloomApiPrefix, post } from "../utils/bloomApi";
import {
    kBloomPurple,
    kDisabledTextOnPurple,
    kTextOnPurple,
} from "../bloomMaterialUITheme";

const bookSettingsIconPath = `${getBloomApiPrefix(false)}images/book-settings.png`;

export const BookSettingsButton: React.FunctionComponent = (props) => {
    const handleClick = React.useCallback(() => {
        post("editView/showBookSettingsDialog");
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
            `}
        />
    );
};
