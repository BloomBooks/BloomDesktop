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
            labelL10nKey="Common.BookSettings"
            labelEnglish="Book Settings"
            onClick={handleClick}
            backgroundColor={kBloomPurple}
            textColor={kTextOnPurple}
            disabledTextColor={kDisabledTextOnPurple}
        />
    );
};
