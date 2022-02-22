/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { IFontMetaData } from "../bookEdit/StyleEditor/fontSelectComponent";
import {
    kBloomBlue,
    kBloomGold,
    kErrorColor,
    kDisabledControlGray
} from "../bloomMaterialUITheme";
import { Typography } from "@material-ui/core";
import OkIcon from "@material-ui/icons/CheckCircle";
import UnsuitableIcon from "@material-ui/icons/Error";
import UnknownIcon from "@material-ui/icons/Help";

interface FontDisplayBarProps {
    fontMetadata: IFontMetaData;
    inDropdownList: boolean;
    isPopoverOpen: boolean;
    onHover?: (event: any, metadata: IFontMetaData) => void;
}

const FontDisplayBar: React.FunctionComponent<FontDisplayBarProps> = props => {
    const suitability = props.fontMetadata.determinedSuitability;
    const ariaOwns = props.isPopoverOpen ? "mouse-over-popover" : undefined;

    const commonProps = {
        "aria-owns": ariaOwns,
        "aria-haspop": "true",
        onMouseEnter: event => {
            if (props.onHover) props.onHover(event, props.fontMetadata);
        }
    };

    const getIconForFont = (): JSX.Element => (
        <React.Fragment>
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
            {suitability === "unsuitable" && (
                <UnsuitableIcon
                    htmlColor={
                        props.inDropdownList
                            ? kDisabledControlGray
                            : kErrorColor
                    }
                    {...commonProps}
                />
            )}
        </React.Fragment>
    );

    const shouldGrayOutText = (): boolean => {
        return props.inDropdownList && suitability !== "ok";
    };
    const textColor = `color: ${
        shouldGrayOutText() ? kDisabledControlGray : "black"
    };`;

    const cssString = `font-family: "${props.fontMetadata.name}", "Roboto", "Arial" !important;`;

    return (
        <div
            css={css`
                display: flex;
                flex: 1;
                flex-direction: row;
                justify-content: space-between !important;
            `}
        >
            <Typography
                css={css`
                    ${cssString}
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
