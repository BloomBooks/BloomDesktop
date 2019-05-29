import { Card, CardContent, Typography } from "@material-ui/core";
import React = require("react");

// NOTE: this is something of a placeholder for future work

export const LibraryPreview: React.FunctionComponent = () => {
    return (
        <Card style={{ width: "400px" }}>
            <CardContent style={{ display: "flex", flexDirection: "row" }}>
                <img src="thumbnail.png" />
                <div>
                    <Typography>
                        <div>Foobar and me</div>
                        <div>Pages</div>
                        <div>Languages</div>
                        <div>Copyright</div>
                        <div>Uploaded by</div>
                        <div>Tags</div>
                    </Typography>
                </div>
            </CardContent>
        </Card>
    );
};
