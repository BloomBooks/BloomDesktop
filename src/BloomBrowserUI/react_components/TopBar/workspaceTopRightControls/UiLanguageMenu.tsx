import { css } from "@emotion/react";
import * as React from "react";
import BloomButton from "../../bloomButton";
import { ArrowDropDown } from "@mui/icons-material";
import {
    postJson,
    useApiString,
    useWatchString,
} from "../../../utils/bloomApi";

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
        <BloomButton
            l10nKey=""
            alreadyLocalized={true}
            enabled={true}
            hasText={true}
            variant="text"
            onClick={onOpen}
            endIcon={<ArrowDropDown fontSize="small" />}
            css={css`
                font-size: 12px;
                padding-top: 0px;
                padding-bottom: 0px;
                text-transform: none;
            `}
        >
            {label}
        </BloomButton>
    );
};
