/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { storiesOf } from "@storybook/react";
import FontSelectComponent from "./fontSelectComponent";
import FontInformationPane from "../../react_components/fontInformationPane";
import fontTestData from "../../utils/fontTestData";
import { Typography } from "@material-ui/core";

const Frame: React.FunctionComponent = ({ children }) => (
    <div
        css={css`
            width: 320px;
            height: 320px;
            border: 1px solid green;
            padding: 20px;
        `}
    >
        {children}
    </div>
);

storiesOf("Format dialog", module)
    .add("Font Select-current ok", () => {
        return React.createElement(() => (
            <Frame>
                <FontSelectComponent
                    fontMetadata={fontTestData}
                    currentFontName={fontTestData[0].name}
                />
            </Frame>
        ));
    })
    .add("Font Select-current unknown", () => {
        return React.createElement(() => (
            <Frame>
                <FontSelectComponent
                    fontMetadata={fontTestData}
                    currentFontName={fontTestData[1].name}
                />
            </Frame>
        ));
    })
    .add("Font Select-current unsuitable-pop left side", () => {
        return React.createElement(() => (
            <Frame>
                <FontSelectComponent
                    fontMetadata={fontTestData}
                    currentFontName={fontTestData[2].name}
                    anchorPopoverLeft={true}
                />
            </Frame>
        ));
    })
    .add("FontInformationPane unsuitable", () => {
        return React.createElement(() => (
            <Frame>
                <FontInformationPane metadata={fontTestData[2]} />
                <Typography variant="h6">
                    For some reason, this test needs refreshing to size itself
                    correctly.
                </Typography>
            </Frame>
        ));
    });
