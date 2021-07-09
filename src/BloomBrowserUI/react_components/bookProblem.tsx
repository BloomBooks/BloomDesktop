/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import Typography from "@material-ui/core/Typography";
import * as React from "react";
import { BloomApi } from "../utils/bloomApi";
import { IconHeadingBodyMenu } from "./iconHeadingBodyMenu";
import { StringWithOptionalLink } from "./stringWithOptionalLink";

// A block (currently sized to fit in the TeamCollectionBookStatusPanel, we could generalize)
// that shows an error icon, a heading "There is a problem with the book <currentBook> in the Team Collection system"
// and a subtitle containing the error from props, followed by
// "Please click here to get help from the Bloom support team".
// The clickHereArg is supposed to be the url-encoded path to a file that should be
// included in the problem report.
export const BookProblem: React.FunctionComponent<{
    errorMessage: string;
    clickHereArg: string;
    className?: string; // also supports Emotion
}> = props => {
    const [bookProblemMessage] = BloomApi.useApiString(
        "common/problemWithBookMessage",
        "There was a problem with the current book"
    );

    const [clickHereForHelpMessage] = BloomApi.useApiString(
        "common/clickHereForHelp?problem=" + props.clickHereArg,
        "Click here for help"
    );

    return (
        <IconHeadingBodyMenu
            css={css`
                a {
                    // Trying to match the red in the Attention.svg. Paint says that is (234,0,0)
                    // but subjectively, perhaps because the red in the icon covers a larger area,
                    // it looks much brigher than the red in the link. To my eye, the link
                    // looks darker than the icon even when it's plain red, let alone when it's any
                    // dimmer, so I'm just going with red.
                    color: red;
                }
            `}
            className={props.className}
            title={bookProblemMessage}
            subTitle={props.errorMessage + " " + clickHereForHelpMessage}
            icon={<img src={"Attention.svg"} alt="disconnected" />}
            menu={undefined}
        />
    );
};
