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
import WarningIcon from "@mui/icons-material/Warning";
import { useDebouncedCallback } from "use-debounce";
import { useL10n } from "./l10nHooks";

interface FontDisplayBarProps {
    fontMetadata: IFontMetaData;
    inDropdownList: boolean;
    isPopoverOpen: boolean;
    onHover?: (hoverTarget: HTMLElement, metadata: IFontMetaData) => void;
    isMissingFont?: boolean;
}

const FontDisplayBar: React.FunctionComponent<FontDisplayBarProps> = (
    props,
) => {
    const isMissingFont = props.isMissingFont === true;
    const suitability = props.fontMetadata.determinedSuitability;

    const missingFontTooltip = useL10n(
        'The font "{0}" is not available on this computer. Another font is being used instead.',
        isMissingFont
            ? "EditTab.FormatDialog.MissingFontIndicatorToolTip"
            : null,
        undefined,
        props.fontMetadata.name,
    );

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
        "aria-haspopup": true,
        onMouseEnter: handleMouseEnter,
        onMouseLeave: handleMouseLeave,
    };

    const getRightSideContent = (): JSX.Element => (
        <div
            css={css`
                padding-top: 3px !important;
                padding-right: 3px !important;
            `}
        >
            {isMissingFont && (
                <span title={missingFontTooltip}>
                    <WarningIcon htmlColor={kErrorColor} />
                </span>
            )}
            {!isMissingFont && suitability === "ok" && (
                <OkIcon htmlColor={kBloomBlue} {...commonProps} />
            )}
            {!isMissingFont && suitability === "unknown" && (
                <UnknownIcon
                    htmlColor={
                        props.inDropdownList ? kDisabledControlGray : kBloomGold
                    }
                    {...commonProps}
                />
            )}
            {!isMissingFont &&
                (suitability === "unsuitable" || suitability === "invalid") && (
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
        if (isMissingFont) {
            return false;
        }
        return props.inDropdownList && suitability !== "ok";
    };
    const textColor = isMissingFont
        ? `color: ${kErrorColor};`
        : `color: ${shouldGrayOutText() ? kDisabledControlGray : "black"};`;

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
                    flex: 0 1 auto;
                    min-width: 0;
                `}
            >
                {props.fontMetadata.name}
            </Typography>
            {getRightSideContent()}
        </div>
    );
};

export default FontDisplayBar;
