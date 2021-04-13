import Grid from "@material-ui/core/Grid";
import React = require("react");
import { BloomApi } from "../utils/bloomApi";
import Menu from "@material-ui/core/Menu";
import MenuItem from "@material-ui/core/MenuItem";
import { Button } from "@material-ui/core";
import TruncateMarkup from "react-truncate-markup";
import { IBookTeamCollectionStatus } from "../teamCollection/TeamCollectionBookStatusPanel";
import { BloomAvatar } from "../react_components/bloomAvatar";

const bloomBlue = "#1d94a4"; // enhance we need to export these somewhere

export const BookButton: React.FunctionComponent<{
    book: any;
    selected: boolean;
    onClick: (bookId: string) => void;
}> = props => {
    // const [thumbnailUrl] = BloomApi.useApiString(
    //     `collection/book/thumbnail?book-id=${props.book.id}`,
    //     ""
    // );
    // TODO: the c# had Font = bookInfo.IsEditable ? _editableBookFont : _collectionBookFont,

    const [teamCollectionStatus] = BloomApi.useApiObject<
        IBookTeamCollectionStatus | undefined
    >(
        `teamCollection/bookStatus?folderName=${props.book.folderName}`,
        undefined
    );

    const label =
        props.book.title.length > 20 ? (
            <TruncateMarkup
                // test false positives css={css`color: red;`}
                lines={2}
            >
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
                        radius={32}
                        borderColor={
                            teamCollectionStatus.who ===
                            teamCollectionStatus.currentUser
                                ? "yellow"
                                : bloomBlue
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
                    <div>{label}</div>
                </Button>
            </div>
        </Grid>
    );
};
