import * as React from "react";
import { Card, CardContent, Typography, IconButton } from "@mui/material";
import CloseIcon from "@mui/icons-material/Close";
import { Link } from "./BookLinkTypes";
import { css } from "@emotion/react";
import { BloomTooltip } from "../BloomToolTip";
import { kBloomGold } from "../../bloomMaterialUITheme";

interface BookCardProps {
    link: Link;
    selected?: boolean;
    onClick?: () => void;
    onRemove?: () => void;
    className?: string;
    style?: React.CSSProperties;
    preferFolderName?: boolean;
    isDragging?: boolean;
    disabled?: boolean;
}

const BookLinkCardComponent: React.FC<BookCardProps> = ({
    link,
    selected,
    onClick,
    onRemove,
    style,
    preferFolderName,
    isDragging,
    disabled = false,
}) => {
    // Allow caller to decide between showing folder name or title as the primary text.
    // The source list shows folder names (technical identifier), while the target list
    // shows titles (user-friendly display). The tooltip shows the alternate value.
    const title = preferFolderName ? link.book.folderName : link.book.title;
    const tooltip = preferFolderName ? link.book.title : link.book.folderName;
    // Don't show tooltip while dragging (to avoid visual clutter) or if it's identical to the title
    const shouldShowTooltip = !isDragging && tooltip !== title;
    return (
        <Card
            onClick={onClick}
            style={style}
            css={css`
                width: 140px;
                background-color: #505050 !important;
                outline: ${selected
                    ? `3px solid ${kBloomGold}`
                    : "3px solid transparent"};
                color: white;
                position: relative;
                padding: 0 0 8px 0;
                opacity: ${disabled ? 0.5 : 1};
                cursor: ${disabled ? "default" : onClick ? "pointer" : "move"};
                &:hover .removeLinkButton {
                    display: block;
                }
            `}
        >
            {onRemove && !isDragging && (
                <IconButton
                    className="removeLinkButton"
                    size="small"
                    aria-label="Remove"
                    data-testid="remove-book-button"
                    onClick={(e) => {
                        e.preventDefault();
                        e.stopPropagation();
                        onRemove();
                    }}
                    onMouseDown={(e) => {
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
                    <CloseIcon fontSize="small" />
                </IconButton>
            )}
            <BloomTooltip
                // Only show tooltip if it differs from the displayed title
                tip={shouldShowTooltip ? tooltip : ""}
            >
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
                    <img
                        src={link.page?.thumbnail || link.book.thumbnail}
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
                            /* enhance: use the actual language font */
                            font-family: "Andika", sans-serif;
                        `}
                    >
                        {title}
                    </Typography>
                </CardContent>
            </BloomTooltip>
        </Card>
    );
};

export const BookLinkCard = React.memo(
    BookLinkCardComponent,
    (prevProps, nextProps) => {
        // Only re-render if book ID or selection state changes
        return (
            prevProps.link.book.id === nextProps.link.book.id &&
            prevProps.selected === nextProps.selected &&
            prevProps.onClick === nextProps.onClick &&
            prevProps.onRemove === nextProps.onRemove &&
            prevProps.style === nextProps.style &&
            prevProps.preferFolderName === nextProps.preferFolderName &&
            prevProps.isDragging === nextProps.isDragging &&
            prevProps.disabled === nextProps.disabled
        );
    },
);
