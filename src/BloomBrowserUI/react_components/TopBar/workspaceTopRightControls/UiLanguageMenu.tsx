import { css } from "@emotion/react";
import * as React from "react";
import { ArrowDropDown } from "@mui/icons-material";
import {
    postJson,
    useApiString,
    useWatchString,
} from "../../../utils/bloomApi";
import { TopRightMenuButton, topRightMenuArrowCss } from "./TopRightMenuButton";

export const UiLanguageMenu: React.FunctionComponent = () => {
    const labelInitial = useApiString("workspace/topRight/uiLanguageLabel", "");
    const label = useWatchString(
        labelInitial,
        "workspaceTopRightControls",
        "uiLanguageLabel",
    );

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
