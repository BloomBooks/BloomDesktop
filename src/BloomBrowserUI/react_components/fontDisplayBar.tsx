import { css } from "@emotion/react";
import * as React from "react";
import { IFontMetaData } from "../bookEdit/StyleEditor/fontSelectComponent";
import {
    kBloomBlue,
    kBloomGold,
    kErrorColor,
    kDisabledControlGray,
} from "../bloomMaterialUITheme";
import { Typography } from "@mui/material";
import OkIcon from "@mui/icons-material/CheckCircle";
import UnsuitableIcon from "@mui/icons-material/Error";
import UnknownIcon from "@mui/icons-material/Help";
import { useDebouncedCallback } from "use-debounce";

interface FontDisplayBarProps {
    fontMetadata: IFontMetaData;
    inDropdownList: boolean;
    isPopoverOpen: boolean;
    onHover?: (hoverTarget: HTMLElement, metadata: IFontMetaData) => void;
}

const FontDisplayBar: React.FunctionComponent<FontDisplayBarProps> = (
    props,
) => {
    const suitability = props.fontMetadata.determinedSuitability;
    const ariaOwns = props.isPopoverOpen ? "mouse-over-popover" : undefined;

    const kHoverDelay = 700; // milliseconds; default MUI tooltip delay
    const debouncedPopover = useDebouncedCallback((target: HTMLElement) => {
        props.onHover!(target, props.fontMetadata);
    }, kHoverDelay);

    const handleMouseEnter = (event: React.MouseEvent) => {
        if (!props.onHover) return;
        debouncedPopover.callback(event.currentTarget as HTMLElement);
    };

    const handleMouseLeave = () => {
        if (!props.onHover) return;
        debouncedPopover.cancel();
    };

    const commonProps = {
        "aria-owns": ariaOwns,
        "aria-haspopup": true,
        onMouseEnter: handleMouseEnter,
        onMouseLeave: handleMouseLeave,
    };

    const getIconForFont = (): JSX.Element => (
        <div
            css={css`
                padding-top: 3px !important;
                padding-right: 3px !important;
            `}
        >
            {suitability === "ok" && (
                <OkIcon htmlColor={kBloomBlue} {...commonProps} />
            )}
            {suitability === "unknown" && (
                <UnknownIcon
                    htmlColor={
                        props.inDropdownList ? kDisabledControlGray : kBloomGold
                    }
                    {...commonProps}
                />
            )}
            {(suitability === "unsuitable" || suitability === "invalid") && (
                <UnsuitableIcon
                    htmlColor={
                        props.inDropdownList
                            ? kDisabledControlGray
                            : kErrorColor
                    }
                    {...commonProps}
                />
            )}
        </div>
    );

    const shouldGrayOutText = (): boolean => {
        return props.inDropdownList && suitability !== "ok";
    };
    const textColor = `color: ${
        shouldGrayOutText() ? kDisabledControlGray : "black"
    };`;

    const cssFontFamily = `font-family: "${props.fontMetadata.name}", "Roboto", "Arial" !important;`;

    return (
        <div
            className="font-display-bar"
            css={css`
                display: flex !important;
                flex: 1 !important;
                flex-direction: row !important;
                justify-content: space-between !important;
                align-items: center !important;
            `}
        >
            <Typography
                css={css`
                    ${cssFontFamily}
                    ${textColor}
                `}
            >
                {props.fontMetadata.name}
            </Typography>
            {getIconForFont()}
        </div>
    );
};

export default FontDisplayBar;
