import { css } from "@emotion/react";
import * as React from "react";
import {
    Card,
    CardContent,
    Typography,
    CardActionArea,
    IconButton,
    Menu,
    MenuItem,
    Chip,
} from "@mui/material";
import MoreVertIcon from "@mui/icons-material/MoreVert";
import { LocalizableMenuItem } from "../react_components/localizableMenuItem";
import { get, postString } from "../utils/bloomApi";
import { kBloomBlue, kBloomRed } from "../bloomMaterialUITheme";
import { useL10n } from "../react_components/l10nHooks";
import { TeamCollectionIcon } from "../teamCollection/TeamCollectionIcon";

export interface ICollectionInfo {
    path: string;
    // The folder that contains the .bloomCollection file. Shown (rather than the
    // file path itself) in the "..." menu. Optional so stories/tests can omit it;
    // when absent we derive it from path.
    folderPath?: string;
    title: string;
    bookCount: number;
    checkedOutCount?: number;
    unpublishedCount?: number;
    isTeamCollection?: boolean;
    // True for the collection currently open in Bloom; that card is highlighted.
    isCurrentCollection?: boolean;
}

// The "..." menu button sits in the top-right corner of the card. The title row
// reserves matching space on its right (kMoreButtonReservedSpace) so a long
// collection name truncates with an ellipsis instead of sliding under the button.
const kMoreButtonReservedSpace = "34px";
const moreButtonStyle = css`
    position: absolute;
    top: 8px;
    right: 8px;
    color: #979797;
    opacity: 0;
    transition: opacity 0.3s;
`;

// Outlined cards with a very subtle resting shadow; on hover the border tints to
// Bloom blue and the shadow lifts a little to signal the whole card is clickable.
const cardStyle = css`
    position: relative;
    border-radius: 8px;
    box-shadow: 0 1px 2px rgba(0, 0, 0, 0.06);
    transition:
        box-shadow 0.15s,
        border-color 0.15s;
    &:hover {
        border-color: ${kBloomBlue};
        box-shadow:
            0 2px 4px -1px rgba(0, 0, 0, 0.16),
            0 4px 10px 0 rgba(0, 0, 0, 0.1);
    }
    &:hover .more-button {
        opacity: 1;
    }
`;

// Applied in addition to cardStyle for the collection currently open in Bloom:
// a faint tinted background.
const currentCardStyle = css`
    background-color: ${kBloomBlue}0d;
`;

// The unpublished count is now plain gray text pushed to the right edge of the
// metadata row (margin-left:auto), rather than a chip.
const unpublishedTextStyle = css`
    margin-left: auto;
    font-size: 13px;
    color: #6b6b6b;
    white-space: nowrap;
`;

const cardContentStyle = css`
    position: relative;
    display: flex;
    flex-direction: row;
    justify-content: space-between;
    padding: 16px 18px;
    &:last-child {
        padding-bottom: 16px;
    }
`;

const cardTitleStyle = css`
    font-size: 16px;
    font-weight: 500;
    letter-spacing: 0.1px;
    color: #262626;
    line-height: 1.3;
    margin: 0;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    // Grow to fill the row (up to the space reserved for the "..." button) and
    // truncate a long name with an ellipsis before it reaches that button.
    min-width: 0;
    flex: 1 1 auto;
`;

// The book count, an optional checked-out chip, and the right-aligned unpublished
// text share a single row beneath the title.
const metadataRowStyle = css`
    display: flex;
    align-items: center;
    gap: 10px;
    width: 100%;
`;

const bookCountStyle = css`
    font-size: 13px;
    color: #6b6b6b;
    line-height: 1.1;
    white-space: nowrap;
`;

// Pill-shaped status "tag" chip, currently used only for the outlined-red
// checked-out chip (see the color/border override where used). Keep the label at
// a normal weight; Bloom's font fallback renders 500 heavier than the mockup's
// Roboto.
const chipBaseStyle = css`
    height: 24px;
    border-radius: 12px;
    letter-spacing: 0.2px;
    .MuiChip-label {
        padding-left: 10px;
        padding-right: 10px;
        font-size: 12px;
        font-weight: 400;
    }
`;

export const CollectionCard: React.FunctionComponent<ICollectionInfo> = (
    props,
) => {
    const [moreMenuAnchorEl, setMoreMenuAnchorEl] =
        React.useState<null | HTMLElement>(null);
    const [unpublishedCount, setUnpublishedCount] = React.useState<
        number | undefined
    >(props.unpublishedCount);
    const moreMenuIsOpen = Boolean(moreMenuAnchorEl);

    // The unpublished count is somewhat expensive to calculate, so it's not likely to be
    // included in the initial data we get. Instead, we use an effect to fetch it when the
    // card is rendered.
    React.useEffect(() => {
        if (props.unpublishedCount !== undefined) return;
        get(
            `collections/getUnpublishedCount?collectionPath=${encodeURIComponent(props.path)}`,
            (r) => setUnpublishedCount(r.data?.count),
        );
    }, [props.path, props.unpublishedCount]);

    const bookCountSingular = useL10n(
        "{0} book",
        "CollectionChooser.BookCountSingular",
        undefined,
        String(props.bookCount),
    );
    const bookCountPlural = useL10n(
        "{0} books",
        "CollectionChooser.BookCountPlural",
        undefined,
        String(props.bookCount),
    );
    const checkedOutText = useL10n(
        "{0} checked out to you",
        "CollectionChooser.CheckedOutCount",
        undefined,
        String(props.checkedOutCount ?? 0),
    );
    // Full phrase, used as the chip's hover tooltip so the short label stays clear.
    const unpublishedText = useL10n(
        "Books not yet published to BloomLibrary.org",
        "CollectionChooser.UnpublishedToBloomLibrary",
    );
    // Short label shown on the right of the metadata row.
    const unpublishedShortText = useL10n(
        "{0} unpublished",
        "CollectionChooser.UnpublishedShort",
        undefined,
        String(unpublishedCount ?? 0),
    );
    const handleClick = (event: React.MouseEvent<HTMLElement>) => {
        event.stopPropagation();
        setMoreMenuAnchorEl(event.currentTarget);
    };
    const handleMenuClose = () => {
        setMoreMenuAnchorEl(null);
    };
    const handleOpenInFileExplorer = () => {
        postString("collections/openCollectionFolderInExplorer", props.path);
        handleMenuClose();
    };

    const bookCountText =
        props.bookCount === 1 ? bookCountSingular : bookCountPlural;

    // Show the collection's folder, not the .bloomCollection file inside it. The
    // backend supplies folderPath; fall back to stripping the file name from path
    // (e.g. for stories that only set path).
    const folderPath =
        props.folderPath ?? props.path.replace(/[\\/][^\\/]*$/, "");

    return (
        <Card
            variant="outlined"
            css={[cardStyle, props.isCurrentCollection && currentCardStyle]}
        >
            <CardActionArea
                onClick={() => {
                    if (!moreMenuAnchorEl)
                        postString("workspace/openCollection", props.path);
                }}
            >
                <CardContent css={cardContentStyle}>
                    <div
                        css={css`
                            display: flex;
                            flex-direction: column;
                            width: 100%;
                        `}
                    >
                        <div
                            css={css`
                                display: flex;
                                align-items: center;
                                width: 100%;
                                margin-bottom: 10px;
                                // Keep the title clear of the top-right "..." button.
                                padding-right: ${kMoreButtonReservedSpace};
                            `}
                        >
                            <Typography variant="body1" css={cardTitleStyle}>
                                {props.title}
                            </Typography>
                        </div>
                        <div css={metadataRowStyle}>
                            {props.isTeamCollection && (
                                <TeamCollectionIcon
                                    css={css`
                                        flex: none;
                                    `}
                                />
                            )}
                            <span css={bookCountStyle}>{bookCountText}</span>
                            {props.checkedOutCount ? (
                                <Chip
                                    size="small"
                                    variant="outlined"
                                    label={checkedOutText}
                                    css={css`
                                        ${chipBaseStyle};
                                        color: ${kBloomRed};
                                        border-color: ${kBloomRed}73;
                                        background-color: transparent;
                                    `}
                                />
                            ) : null}
                            {unpublishedCount ? (
                                <span
                                    css={unpublishedTextStyle}
                                    title={unpublishedText}
                                >
                                    {unpublishedShortText}
                                </span>
                            ) : null}
                        </div>
                    </div>
                </CardContent>
            </CardActionArea>
            {/* Outside CardActionArea so clicks don't bubble up and open the collection */}
            <IconButton
                aria-label="more"
                aria-controls="long-menu"
                aria-haspopup="true"
                onClick={handleClick}
                css={[
                    moreButtonStyle,
                    moreMenuIsOpen &&
                        css`
                            opacity: 1;
                        `,
                ]}
                className="more-button"
            >
                <MoreVertIcon />
            </IconButton>
            <Menu
                id="long-menu"
                anchorEl={moreMenuAnchorEl}
                keepMounted
                open={moreMenuIsOpen}
                onClose={handleMenuClose}
                slotProps={{ paper: { sx: { maxWidth: 280 } } }}
            >
                <LocalizableMenuItem
                    english={"Show in File Explorer"}
                    l10nId={"CollectionTab.BookMenu.ShowInFileExplorer"}
                    onClick={handleOpenInFileExplorer}
                    hasLeadingIconSpace={false}
                />
                <hr />
                <MenuItem
                    disabled
                    css={css`
                        white-space: normal;
                        word-break: break-all;
                    `}
                >
                    {folderPath}
                </MenuItem>
            </Menu>
        </Card>
    );
};
