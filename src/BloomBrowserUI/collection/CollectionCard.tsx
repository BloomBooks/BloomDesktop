/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import React = require("react");
import {
    Card,
    CardContent,
    Typography,
    CardActionArea,
    IconButton,
    Menu,
    MenuItem
} from "@mui/material";
import MoreVertIcon from "@mui/icons-material/MoreVert";
import { LocalizableMenuItem } from "../react_components/localizableMenuItem";
import { postString } from "../utils/bloomApi";
import { kBloomYellow } from "../bloomMaterialUITheme";

export interface ICollectionInfo {
    path: string;
    title: string;
    bookCount: number;
    checkedOutCount?: number;
    unpublishedCount?: number;
    isTeamCollection?: boolean;
}

const iconStyle = css`
    width: 24px;
    height: 24px;
    margin-left: 8px;
    flex-shrink: 0;
`;

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
    padding-bottom: 8px; // less than MUI default of 16px
`;

export const CollectionCard: React.FunctionComponent<ICollectionInfo> = props => {
    const [anchorEl, setAnchorEl] = React.useState<null | HTMLElement>(null);
    const open = Boolean(anchorEl);
    const handleClick = (event: React.MouseEvent<HTMLElement>) => {
        setAnchorEl(event.currentTarget);
    };
    const handleMenuClose = () => {
        setAnchorEl(null);
    };
    const handleOpenInFileExplorer = () => {
        postString("collections/openCollectionFolderInExplorer", props.path);
        handleMenuClose();
    };

    const additionalCardTexts: JSX.Element[] = getAdditionalCardTexts(props);

    return (
        <Card variant="outlined" css={cardStyle}>
            <CardActionArea>
                <CardContent css={cardContentStyle}>
                    <div
                        css={css`
                            display: flex;
                            flex-direction: column;
                            justify-content: space-between;
                            height: 100%;
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
                                    padding-bottom: 3px;
                                    white-space: nowrap;
                                    overflow: hidden;
                                    text-overflow: ellipsis;
                                    flex-grow: 1;
                                `}
                            >
                                {props.title}
                            </Typography>
                            {props.isTeamCollection && (
                                <img
                                    src="bloom/teamCollection/Team Collection.svg"
                                    css={iconStyle}
                                    alt="Team Collection"
                                />
                            )}
                        </div>
                        {additionalCardTexts}
                        {/* Guarantee we always have 3 AdditionalCardTexts, but that blanks are last */}
                        {Array.from({
                            length: 3 - additionalCardTexts.length
                        }).map((_, index) => (
                            <AdditionalCardText key={`blank${index}`} />
                        ))}
                    </div>
                    <IconButton
                        aria-label="more"
                        aria-controls="long-menu"
                        aria-haspopup="true"
                        onClick={handleClick}
                        onMouseDown={event => event.stopPropagation()}
                        css={moreButtonStyle}
                        className="more-button"
                    >
                        <MoreVertIcon />
                    </IconButton>
                    <Menu
                        id="long-menu"
                        anchorEl={anchorEl}
                        keepMounted
                        open={open}
                        onClose={handleMenuClose}
                    >
                        <LocalizableMenuItem
                            english={"Show in File Explorer"}
                            l10nId={"CollectionTab.BookMenu.ShowInFileExplorer"}
                            onClick={handleOpenInFileExplorer}
                            dontGiveAffordanceForCheckbox={true}
                        />
                        <hr />
                        <MenuItem disabled>{props.path}</MenuItem>
                    </Menu>
                </CardContent>
            </CardActionArea>
        </Card>
    );

    function getAdditionalCardTexts(collectionInfo: ICollectionInfo) {
        const additionalCardTexts: JSX.Element[] = [];
        additionalCardTexts.push(
            <AdditionalCardText
                key="bookCount"
                text={
                    collectionInfo.bookCount === 1
                        ? `${collectionInfo.bookCount} book`
                        : `${collectionInfo.bookCount} books`
                }
            />
        );
        if (collectionInfo.checkedOutCount) {
            additionalCardTexts.push(
                <AdditionalCardText
                    key="checkedOutCount"
                    text={`${collectionInfo.checkedOutCount} checked out to you`}
                    color={kBloomYellow}
                />
            );
        }
        if (collectionInfo.unpublishedCount) {
            additionalCardTexts.push(
                <AdditionalCardText
                    key="unpublishedCount"
                    text={`${collectionInfo.unpublishedCount} unpublished to bloomlibrary.org`}
                />
            );
        }
        return additionalCardTexts;
    }
};

const AdditionalCardText: React.FunctionComponent<{
    text?: string;
    color?: string;
}> = ({ text, color = "#979797" }) => (
    <Typography
        variant="body2"
        css={css`
            color: ${color};
            padding-bottom: 2px;
            ${text ? "" : "visibility: hidden;"}
        `}
    >
        {text || "invisible"}
    </Typography>
);
