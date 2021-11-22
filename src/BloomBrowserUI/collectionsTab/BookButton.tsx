/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import Grid from "@material-ui/core/Grid";
import React = require("react");
import { BloomApi } from "../utils/bloomApi";
import { Button } from "@material-ui/core";
import TruncateMarkup from "react-truncate-markup";
import {
    IBookTeamCollectionStatus,
    useBookStatus
} from "../teamCollection/teamCollectionUtils";
import { BloomAvatar } from "../react_components/bloomAvatar";
import { kBloomBlue, kBloomGold } from "../bloomMaterialUITheme.js";

export const BookButton: React.FunctionComponent<{
    book: any;
    isInEditableCollection: boolean;
    selected: boolean;
    onClick: (bookId: string) => void;
}> = props => {
    // TODO: the c# had Font = bookInfo.IsEditable ? _editableBookFont : _collectionBookFont,

    const teamCollectionStatus = useBookStatus(
        props.book.folderName,
        props.isInEditableCollection
    );

    const label =
        props.book.title.length > 20 ? (
            <TruncateMarkup lines={2}>
                <span>{props.book.title}</span>
            </TruncateMarkup>
        ) : (
            props.book.title
        );

    return (
        <Grid item={true}>
            <div>
                {teamCollectionStatus?.who && (
                    <BloomAvatar
                        email={teamCollectionStatus.who}
                        name={teamCollectionStatus.whoFirstName}
                        avatarSizeInt={32}
                        borderColor={
                            teamCollectionStatus.who ===
                            teamCollectionStatus.currentUser
                                ? kBloomGold
                                : kBloomBlue
                        }
                    />
                )}
                <Button
                    className={
                        "bookButton" +
                        (props.selected ? " selected " : "") +
                        (teamCollectionStatus?.who ? " checkedOut" : "")
                    }
                    variant="outlined"
                    size="large"
                    onClick={() => props.onClick(props.book.id)}
                    startIcon={
                        <div className={"thumbnail-wrapper"}>
                            <img
                                src={`/bloom/api/collections/book/thumbnail?book-id=${
                                    props.book.id
                                }&collection-id=${encodeURIComponent(
                                    props.book.collectionId
                                )}`}
                            />
                        </div>
                    }
                >
                    {label}
                </Button>
            </div>
        </Grid>
    );
};
