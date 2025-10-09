import * as React from "react";
import { Card, CardContent, Typography, IconButton } from "@mui/material";
import DeleteIcon from "@mui/icons-material/Delete";
import { Link } from "./BookLinkTypes";
import { css } from "@emotion/react";
import { BloomTooltip } from "../../react_components/BloomToolTip";

interface BookCardProps {
    link: Link;
    selected?: boolean;
    onClick?: () => void;
    onRemove?: () => void;
    className?: string;
    style?: React.CSSProperties;
    displayRealTitle?: boolean;
}

export const LinkCard: React.FC<BookCardProps> = ({
    link,
    selected,
    onClick,
    onRemove,
    style,
    displayRealTitle
}) => {
    const title = displayRealTitle ? link.book.title : link.book.folderName;
    const tooltip = displayRealTitle ? link.book.folderName : link.book.title;
    return (
        <Card
            onClick={onClick}
            style={style}
            css={css`
                width: 140px;
                cursor: ${onClick ? "pointer" : "move"};
                background-color: ${selected
                    ? "rgb(25, 118, 210)"
                    : "#505050"} !important;
                outline: ${selected ? "3px solid rgb(25, 118, 210)" : "none"};
                color: white;
                position: relative;
                padding: 0 0 8px 0;
                &:hover .removeLinkButton {
                    display: block;
                }
            `}
        >
            {onRemove && (
                <IconButton
                    className="removeLinkButton"
                    size="small"
                    onClick={e => {
                        e.preventDefault();
                        e.stopPropagation();
                        onRemove();
                    }}
                    onMouseDown={e => {
                        e.stopPropagation();
                    }}
                    css={css`
                        position: absolute;
                        top: 4px;
                        right: 4px;
                        display: none;
                        z-index: 1;
                        color: white;
                        pointer-events: auto;
                        background-color: rgba(0, 0, 0, 0.6);
                        padding: 4px;
                        width: 32px;
                        height: 32px;
                        border-radius: 50%;
                        svg {
                            pointer-events: none;
                        }
                        &:hover {
                            background-color: rgba(0, 0, 0, 0.8);
                        }
                    `}
                >
                    <DeleteIcon fontSize="small" />
                </IconButton>
            )}
            <BloomTooltip
                // If we are displaying the title, add a tooltip with the foldername
                tip={tooltip}
            >
                <CardContent
                    css={css`
                        padding: 0;
                        padding-bottom: 0;
                        display: flex;
                        flex-direction: column;
                        height: 100%;
                        &:last-child {
                            padding-bottom: 0;
                        }
                    `}
                >
                    <img
                        src={link.page?.thumbnail || link.book.thumbnail}
                        alt={`${title}${
                            link.page ? ` - Page ${link.page.pageId}` : ""
                        }`}
                        css={css`
                            width: 100%;
                            aspect-ratio: 16/9;
                            height: 100px;
                            object-fit: cover;
                            object-position: center top;
                            margin-bottom: 8px;
                        `}
                    />
                    <Typography
                        css={css`
                            color: white;
                            height: 3em;
                            overflow: hidden;
                            display: -webkit-box;
                            -webkit-line-clamp: 2;
                            -webkit-box-orient: vertical;
                            line-height: 1.5em;
                            font-size: 12px;
                            text-align: center;
                        `}
                    >
                        {title}
                        {link.page && ` (Page ${link.page.pageId})`}
                    </Typography>
                </CardContent>
            </BloomTooltip>
        </Card>
    );
};
