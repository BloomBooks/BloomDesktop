import * as React from "react";
import { ArrowDropDown } from "@mui/icons-material";
import { postJson, useApiString } from "../../../utils/bloomApi";
import { TopRightMenuButton, topRightMenuArrowCss } from "./TopRightMenuButton";

export const UiLanguageMenu: React.FunctionComponent = () => {
    const label = useApiString("workspace/topRight/uiLanguageLabel", "");

    const onOpen = () => {
        postJson("workspace/topRight/openLanguageMenu", {});
    };

    return (
        <TopRightMenuButton
            text={label}
            onClick={onOpen}
            endIcon={<ArrowDropDown css={topRightMenuArrowCss} />}
        />
    );
};
