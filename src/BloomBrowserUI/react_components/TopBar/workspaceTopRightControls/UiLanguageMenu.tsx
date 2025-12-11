import { css } from "@emotion/react";
import * as React from "react";
import BloomButton from "../../bloomButton";
import { ArrowDropDown } from "@mui/icons-material";
import { postJson } from "../../../utils/bloomApi";

interface LanguageMenuProps {
    text: string;
}

export const UiLanguageMenu: React.FunctionComponent<LanguageMenuProps> = (
    props,
) => {
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
            endIcon={<ArrowDropDown />}
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
            {/* Help */}
            {props.text}
        </BloomButton>
    );
};
