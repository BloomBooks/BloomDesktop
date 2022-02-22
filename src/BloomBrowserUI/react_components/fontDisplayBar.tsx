/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { IFontMetaData } from "../bookEdit/StyleEditor/fontSelectComponent";
import { kBloomBlue, kBloomGold } from "../bloomMaterialUITheme";
import { Typography } from "@material-ui/core";
import CheckCircleIcon from "@material-ui/icons/CheckCircle"; // used for 'font is "ok"'
import ErrorIcon from "@material-ui/icons/Error"; // used for 'font is "unsuitable"'
import HelpIcon from "@material-ui/icons/Help"; // used for 'font is "unknown"'

interface FontDisplayBarProps {
    fontMetadata: IFontMetaData;
    inDropdownList: boolean;
    isPopoverOpen: boolean;
    onHover?: (event: any, metadata: IFontMetaData) => void;
}

const FontDisplayBar: React.FunctionComponent<FontDisplayBarProps> = props => {
    const kGray = "#bbb";
    const kBloomRed = "#D65649";

    const suitability = props.fontMetadata.determinedSuitability;

    const getIconForFont = (): JSX.Element => (
        <React.Fragment>
            {suitability === "ok" && (
                <CheckCircleIcon
                    htmlColor={kBloomBlue}
                    aria-owns={
                        props.isPopoverOpen ? "mouse-over-popover" : undefined
                    }
                    aria-haspopup="true"
                    onMouseEnter={event => {
                        if (props.onHover)
                            props.onHover(event, props.fontMetadata);
                    }}
                />
            )}
            {suitability === "unknown" && (
                <HelpIcon
                    htmlColor={props.inDropdownList ? kGray : kBloomGold}
                    aria-owns={
                        props.isPopoverOpen ? "mouse-over-popover" : undefined
                    }
                    aria-haspopup="true"
                    onMouseEnter={event => {
                        if (props.onHover)
                            props.onHover(event, props.fontMetadata);
                    }}
                />
            )}
            {suitability === "unsuitable" && (
                <ErrorIcon
                    htmlColor={props.inDropdownList ? kGray : kBloomRed}
                    aria-owns={
                        props.isPopoverOpen ? "mouse-over-popover" : undefined
                    }
                    aria-haspopup="true"
                    onMouseEnter={event => {
                        if (props.onHover)
                            props.onHover(event, props.fontMetadata);
                    }}
                />
            )}
        </React.Fragment>
    );

    const shouldGrayOutText = (): boolean => {
        return props.inDropdownList && suitability !== "ok";
    };
    const textColor = `color: ${shouldGrayOutText() ? kGray : "black"};`;

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
