import { css } from "@emotion/react";
import * as React from "react";
import BloomButton from "../../bloomButton";
import { BloomTooltip } from "../../BloomToolTip";
import { ArrowDropDown, HelpOutline } from "@mui/icons-material";
import { postJson } from "../../../utils/bloomApi";

export const HelpMenu: React.FunctionComponent = () => {
    const onOpen = () => {
        postJson("workspace/topRight/openHelpMenu", {});
    };

    return (
        <BloomTooltip tip={{ l10nKey: "HelpMenu.Help Menu" }}>
            {/* TODO: dynamic text vs icon */}
            <BloomButton
                l10nKey=""
                alreadyLocalized={true}
                enabled={true}
                onClick={onOpen}
                startIcon={<HelpOutline />}
                endIcon={<ArrowDropDown fontSize="small" />}
                hasText={false}
                variant="text"
                css={css`
                    font-size: 12px;
                    padding-top: 0px;
                    padding-bottom: 0px;
                    text-transform: none;
                `}
            >
                Help
            </BloomButton>
        </BloomTooltip>
    );
};
