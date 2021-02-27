import Grid from "@material-ui/core/Grid";
import React = require("react");
import "BookListPane.less";
import Paper from "@material-ui/core/Paper";
import { BloomApi } from "../utils/bloomApi";

export const BookListPane: React.FunctionComponent<{}> = () => {
    const [collectionName] = BloomApi.useApiString(
        "collection/name",
        "unknown"
    );
    return (
        <div className="bookButtonPane">
            <h1>{collectionName}</h1>
            <Grid
                container={true}
                spacing={3}
                direction="row"
                justify="flex-start"
                alignItems="flex-start"
            >
                <BookButton />
                <BookButton />
                <BookButton />
                <BookButton />
                <BookButton />
                <BookButton />
                <BookButton />
            </Grid>
        </div>
    );
};

export const BookButton: React.FunctionComponent<{}> = () => {
    return (
        <Grid item={true}>
            <div className="bookButton">
                <div className="imgFake" />
                <div className="bookTitle">The Moon and the Cap3</div>
            </div>
            {/* <div className="BookButton">Book</div>; */}
        </Grid>
    );
};
