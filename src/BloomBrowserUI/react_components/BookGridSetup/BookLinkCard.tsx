import * as React from "react";
import { Card, CardContent, Typography, IconButton } from "@mui/material";
import CloseIcon from "@mui/icons-material/Close";
import AutoStoriesIcon from "@mui/icons-material/AutoStories";
import ArrowDropDownIcon from "@mui/icons-material/ArrowDropDown";
import { Link } from "./BookLinkTypes";
import { css } from "@emotion/react";
import { BloomTooltip } from "../BloomToolTip";
import { selectedStyle } from "../LinkTargetChooser/sharedStyles";
import { kBloomBlue } from "../../bloomMaterialUITheme";

const pageIconThumbnailUrl =
    "/bloom/bookEdit/pageThumbnailList/pageControls/bookGridPageIcon.svg";

interface BookCardProps {
    link: Link;
    selected?: boolean;
    onClick?: () => void;
    onRemove?: () => void;
    onLabelChange?: (newLabel: string) => void;
    onThumbnailClick?: (event: React.MouseEvent<HTMLDivElement>) => void;
    showThumbnailDropdownAffordance?: boolean;
    matchOnPageStyle?: boolean;
    className?: string;
    style?: React.CSSProperties;
    preferFolderName?: boolean;
    isDragging?: boolean;
    disabled?: boolean;
}

export const BookLinkCard: React.FC<BookCardProps> = (props) => {
    const title = props.preferFolderName
        ? props.link.book.folderName
        : props.link.label !== undefined
          ? props.link.label
          : props.link.book.title;
    const tooltip = props.preferFolderName
        ? props.link.book.title
        : props.link.book.folderName;
    const shouldShowTooltip = !props.isDragging && tooltip !== title;

    const textColor = props.matchOnPageStyle ? "black" : "white";
    const cardWidth = props.matchOnPageStyle ? "96px" : "140px";
    const cardPadding = props.matchOnPageStyle ? "5px" : "0 0 8px 0";
    const thumbnailHeight = props.matchOnPageStyle ? "70px" : "100px";
    const labelHeight = props.matchOnPageStyle ? "31px" : "3em";
    const labelFontSize = props.matchOnPageStyle ? "13px" : "12px";
    const labelLineHeight = props.matchOnPageStyle ? "1.2" : "1.5em";
    const labelMarginTop = props.matchOnPageStyle ? "5px" : "0";

    const usePageIconThumbnail =
        props.link.page?.thumbnailSource === "pageIcon" ||
        props.link.page?.thumbnail === pageIconThumbnailUrl;

    return (
        <Card
            onClick={props.onClick}
            style={props.style}
            css={css`
                width: ${cardWidth};
                margin: ${props.matchOnPageStyle ? "0 auto" : "0"};
                background-color: ${props.matchOnPageStyle
                    ? "white"
                    : "#505050"} !important;
                ${props.selected ? selectedStyle : ""}
                color: ${textColor};
                position: relative;
                padding: ${cardPadding};
                box-sizing: border-box;
                opacity: ${props.disabled ? 0.5 : 1};
                cursor: ${props.disabled
                    ? "default"
                    : props.onClick
                      ? "pointer"
                      : "move"};
                border: ${props.matchOnPageStyle
                    ? "solid 1px #c8c8c8"
                    : "none"};
                box-shadow: ${props.matchOnPageStyle
                    ? "0 2px 4px rgba(0, 0, 0, 0.1)"
                    : "none"};
                border-radius: ${props.matchOnPageStyle ? "5px" : "4px"};
                &:hover .removeLinkButton {
                    display: block;
                }
            `}
        >
            {props.onRemove && !props.isDragging && (
                <IconButton
                    className="removeLinkButton"
                    size="small"
                    aria-label="Remove"
                    data-testid="remove-book-button"
                    onClick={(event) => {
                        event.preventDefault();
                        event.stopPropagation();
                        props.onRemove?.();
                    }}
                    onMouseDown={(event) => {
                        event.stopPropagation();
                    }}
                    css={css`
                        position: absolute;
                        top: 4px;
                        right: 4px;
                        display: none;
                        z-index: 1;
                        color: #222;
                        pointer-events: auto;
                        background-color: rgba(255, 255, 255, 0.9);
                        padding: 4px;
                        width: 24px;
                        height: 24px;
                        border-radius: 50%;
                        svg {
                            pointer-events: none;
                        }
                        &:hover {
                            background-color: rgba(255, 255, 255, 1);
                        }
                    `}
                >
                    <CloseIcon fontSize="small" />
                </IconButton>
            )}

            <BloomTooltip tip={shouldShowTooltip ? tooltip : ""}>
                <CardContent
                    css={css`
                        padding: 0;
                        display: flex;
                        flex-direction: column;
                        height: 100%;
                        &:last-child {
                            padding-bottom: 0;
                        }
                    `}
                >
                    <div
                        className="thumbnailChooserArea"
                        onClick={(event) => {
                            if (props.onThumbnailClick) {
                                event.stopPropagation();
                                props.onThumbnailClick(event);
                            }
                        }}
                        css={css`
                            width: 100%;
                            height: ${thumbnailHeight};
                            margin-bottom: ${props.matchOnPageStyle
                                ? "0"
                                : "8px"};
                            position: relative;
                            display: flex;
                            align-items: center;
                            justify-content: center;
                            cursor: ${props.onThumbnailClick
                                ? "pointer"
                                : "default"};
                            .thumbnailDropdownAffordance {
                                opacity: 0;
                                pointer-events: none;
                            }
                            &:hover .thumbnailDropdownAffordance {
                                opacity: 1;
                            }
                        `}
                    >
                        {usePageIconThumbnail ? (
                            <div
                                css={css`
                                    width: 100%;
                                    height: ${thumbnailHeight};
                                    display: flex;
                                    align-items: center;
                                    justify-content: center;
                                    background-color: #f4f4f4;
                                `}
                            >
                                <AutoStoriesIcon
                                    css={css`
                                        color: ${kBloomBlue};
                                        font-size: ${props.matchOnPageStyle
                                            ? "32px"
                                            : "42px"};
                                    `}
                                />
                            </div>
                        ) : (
                            <img
                                src={
                                    props.link.page?.thumbnail ||
                                    props.link.book.thumbnail
                                }
                                css={css`
                                    width: 100%;
                                    height: ${thumbnailHeight};
                                    object-fit: contain;
                                    display: block;
                                `}
                            />
                        )}

                        {props.showThumbnailDropdownAffordance && (
                            <div
                                className="thumbnailDropdownAffordance"
                                css={css`
                                    position: absolute;
                                    right: 0;
                                    bottom: 0;
                                    width: 18px;
                                    height: 18px;
                                    border-radius: 0;
                                    background: rgba(255, 255, 255, 0.9);
                                    display: flex;
                                    align-items: center;
                                    justify-content: center;
                                    transition: opacity 0.12s ease;
                                `}
                            >
                                <ArrowDropDownIcon
                                    css={css`
                                        color: #444;
                                        font-size: 18px;
                                    `}
                                />
                            </div>
                        )}
                    </div>

                    {props.onLabelChange && !props.preferFolderName ? (
                        <textarea
                            value={title}
                            onChange={(event) =>
                                props.onLabelChange?.(event.target.value)
                            }
                            onClick={(event) => event.stopPropagation()}
                            onKeyDown={(event) => event.stopPropagation()}
                            onKeyUp={(event) => event.stopPropagation()}
                            onKeyPress={(event) => event.stopPropagation()}
                            rows={2}
                            css={css`
                                color: ${textColor};
                                background: transparent;
                                border: none;
                                outline: none;
                                height: ${labelHeight};
                                margin-top: ${labelMarginTop};
                                line-height: ${labelLineHeight};
                                font-size: ${labelFontSize};
                                text-align: center;
                                width: 100%;
                                font-family: ${props.matchOnPageStyle
                                    ? "inherit"
                                    : '"Andika", sans-serif'};
                                padding: 0 2px;
                                box-sizing: border-box;
                                resize: none;
                                overflow: hidden;
                            `}
                        />
                    ) : (
                        <Typography
                            css={css`
                                color: ${textColor};
                                height: ${labelHeight};
                                margin-top: ${labelMarginTop};
                                overflow: hidden;
                                display: -webkit-box;
                                -webkit-line-clamp: 2;
                                -webkit-box-orient: vertical;
                                line-height: ${labelLineHeight};
                                font-size: ${labelFontSize};
                                text-align: center;
                                font-family: ${props.matchOnPageStyle
                                    ? "inherit"
                                    : '"Andika", sans-serif'};
                                padding: 0 2px;
                            `}
                        >
                            {title}
                        </Typography>
                    )}
                </CardContent>
            </BloomTooltip>
        </Card>
    );
};
