/** @jsxImportSource @emotion/react */
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
            <BloomButton
                l10nKey=""
                alreadyLocalized={true}
                enabled={true}
                onClick={onOpen}
                startIcon={<HelpOutline />}
                endIcon={<ArrowDropDown />}
                hasText={false}
                variant="text"
                css={css`
                    border: hidden;
                    font-size: 11px;
                    padding-inline: 5px;
                    padding-top: 1px;
                    padding-bottom: 2px;
                    text-transform: none;
                    width: fit-content;
                `}
            >
                Help
            </BloomButton>
        </BloomTooltip>
    );
};
