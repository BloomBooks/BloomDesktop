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
} from "@mui/material";
import MoreVertIcon from "@mui/icons-material/MoreVert";
import { LocalizableMenuItem } from "../react_components/localizableMenuItem";
import { get, postString } from "../utils/bloomApi";
import { kBloomBlue, kBloomRed } from "../bloomMaterialUITheme";
import { useL10n } from "../react_components/l10nHooks";
import { TeamCollectionIcon } from "../teamCollection/TeamCollectionIcon";

export interface ICollectionInfo {
    path: string;
    title: string;
    bookCount: number;
    checkedOutCount?: number;
    unpublishedCount?: number;
    isTeamCollection?: boolean;
    // The fields below are set only for a "join card" (dogfood batch 1, item 6): a cloud Team
    // Collection the signed-in user belongs to but has no local copy of yet (see
    // CollectionChooserApi.HandleGetJoinCards). A join card has no local folder, so none of the
    // fields above (bookCount/checkedOutCount/unpublishedCount) are meaningful for it and none are
    // fetched; clicking it invites the user to join instead of opening a local collection.
    isJoinCard?: boolean;
    collectionId?: string;
    onJoinClick?: (collectionId: string, title: string) => void;
}

const moreButtonStyle = css`
    position: absolute;
    bottom: 8px;
    right: 8px;
    color: #979797;
    opacity: 0;
    transition: opacity 0.3s;
`;

const cardStyle = css`
    position: relative;
    box-shadow: #b5b5b5 0px 3px 5px;
    &:hover .more-button {
        opacity: 1;
    }
`;

const cardContentStyle = css`
    position: relative;
    display: flex;
    flex-direction: row;
    justify-content: space-between;
    padding: 6px 10px;
    &:last-child {
        padding-bottom: 6px;
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
    // card is rendered. A join card has no local folder to ask about, so it never fetches this
    // (or anything else per-card -- see the batch plan's "no per-card fetches for join cards").
    React.useEffect(() => {
        if (props.isJoinCard || props.unpublishedCount !== undefined) return;
        get(
            `collections/getUnpublishedCount?collectionPath=${encodeURIComponent(props.path)}`,
            (r) => setUnpublishedCount(r.data?.count),
        );
    }, [props.isJoinCard, props.path, props.unpublishedCount]);

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
    const unpublishedText = useL10n(
        "{0} unpublished to bloomlibrary.org",
        "CollectionChooser.UnpublishedToBloomLibrary",
        undefined,
        String(unpublishedCount ?? 0),
    );
    // Reuses the same "Get" wording the old "Get my Team Collections" sidebar used for its
    // pull-down button, as the join cue for a join card (batch decision: "e.g. the existing
    // 'Get'/join affordance").
    const joinCueText = useL10n("Get", "CollectionChooser.PullDown", undefined);

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

    const additionalCardTexts: JSX.Element[] = getAdditionalCardTexts();

    return (
        <Card
            variant="outlined"
            css={cardStyle}
            data-testid={props.isJoinCard ? "join-collection-card" : undefined}
        >
            <CardActionArea
                onClick={() => {
                    if (moreMenuAnchorEl) return;
                    if (props.isJoinCard) {
                        props.onJoinClick?.(
                            props.collectionId ?? "",
                            props.title,
                        );
                        return;
                    }
                    postString("workspace/openCollection", props.path);
                }}
            >
                <CardContent css={cardContentStyle}>
                    <div
                        css={css`
                            display: flex;
                            flex-direction: column;
                            gap: 3px;
                            width: 100%;
                        `}
                    >
                        <div
                            css={css`
                                display: flex;
                                justify-content: space-between;
                                align-items: center;
                                width: 100%;
                            `}
                        >
                            <Typography
                                variant="h5"
                                css={css`
                                    color: #263238;
                                    line-height: 1.2;
                                    margin: 0;
                                    white-space: nowrap;
                                    overflow: hidden;
                                    text-overflow: ellipsis;
                                    flex-grow: 1;
                                `}
                            >
                                {props.title}
                            </Typography>
                            {props.isTeamCollection && (
                                <TeamCollectionIcon
                                    css={css`
                                        margin-left: 6px;
                                    `}
                                />
                            )}
                        </div>
                        {additionalCardTexts}
                        {/* Guarantee we always have 3 AdditionalCardTexts, but that blanks are last */}
                        {Array.from({
                            length: 3 - additionalCardTexts.length,
                        }).map((_, index) => (
                            <AdditionalCardText key={`blank${index}`} />
                        ))}
                    </div>
                </CardContent>
            </CardActionArea>
            {/* Outside CardActionArea so clicks don't bubble up and open the collection.
                Omitted entirely for a join card: there is no local folder yet, so "Show in File
                Explorer" and the path display below are both meaningless before joining. */}
            {!props.isJoinCard && (
                <React.Fragment>
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
                            {props.path}
                        </MenuItem>
                    </Menu>
                </React.Fragment>
            )}
        </Card>
    );

    function getAdditionalCardTexts() {
        const additionalCardTexts: JSX.Element[] = [];
        // A join card has no local book count/checked-out/unpublished info to show (it isn't
        // joined yet); show only the join cue instead.
        if (props.isJoinCard) {
            additionalCardTexts.push(
                <AdditionalCardText
                    key="joinCue"
                    text={joinCueText}
                    color={kBloomBlue}
                />,
            );
            return additionalCardTexts;
        }
        additionalCardTexts.push(
            <AdditionalCardText
                key="bookCount"
                text={
                    props.bookCount === 1 ? bookCountSingular : bookCountPlural
                }
            />,
        );
        if (props.checkedOutCount) {
            additionalCardTexts.push(
                <AdditionalCardText
                    key="checkedOutCount"
                    text={checkedOutText}
                    color={kBloomRed}
                />,
            );
        }
        if (unpublishedCount) {
            additionalCardTexts.push(
                <AdditionalCardText
                    key="unpublishedCount"
                    text={unpublishedText}
                />,
            );
        }
        return additionalCardTexts;
    }
};

const AdditionalCardText: React.FunctionComponent<{
    text?: string;
    color?: string;
}> = (props) => (
    <Typography
        variant="body2"
        css={css`
            color: ${props.color ?? "#606060"};
            line-height: 1.1;
            margin-block-end: 2px !important;
            ${props.text ? "" : "visibility: hidden;"}
        `}
    >
        {props.text || "invisible"}
    </Typography>
);
