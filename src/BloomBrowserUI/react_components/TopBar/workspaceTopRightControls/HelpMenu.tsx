import * as React from "react";
import { BloomTooltip } from "../../BloomToolTip";
import { ArrowDropDown, HelpOutline } from "@mui/icons-material";
import {
    postJson,
    useApiString,
    useWatchString,
} from "../../../utils/bloomApi";
import { TopRightMenuButton, topRightMenuArrowCss } from "./TopRightMenuButton";
import { useL10n } from "../../l10nHooks";

export const HelpMenu: React.FunctionComponent = () => {
    const helpText = useL10n("?", "HelpMenu.Help Menu");

    const uiLangInitial = useApiString("currentUiLanguage", "en");
    const uiLanguage = useWatchString(
        uiLangInitial,
        "app",
        "uiLanguageChanged",
    );

    const showIconOnly =
        helpText === "?" || ["en", "fr", "de", "es"].includes(uiLanguage);

    const onOpen = () => {
        postJson("workspace/topRight/openHelpMenu", {});
    };

    const button = (
        <TopRightMenuButton
            text={showIconOnly ? "" : helpText}
            onClick={onOpen}
            startIcon={showIconOnly ? <HelpOutline /> : undefined}
            endIcon={<ArrowDropDown css={topRightMenuArrowCss} />}
            hasText={!showIconOnly}
        />
    );

    if (!showIconOnly) {
        return button;
    }

    return (
        <BloomTooltip tip={{ l10nKey: "HelpMenu.Help Menu" }}>
            {button}
        </BloomTooltip>
    );
};
