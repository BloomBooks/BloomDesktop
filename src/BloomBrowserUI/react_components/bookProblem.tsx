import { css } from "@emotion/react";

import * as React from "react";
import { useApiString } from "../utils/bloomApi";
import { IconHeadingBodyMenuPanel } from "./iconHeadingBodyMenuPanel";

// A block (currently sized to fit in the TeamCollectionBookStatusPanel, we could generalize)
// that shows an error icon, a heading "There is a problem with the book <currentBook> in the Team Collection system"
// and a subtitle containing the error from props, followed by
// "Please click here to get help from the Bloom support team".
// The clickHereArg is supposed to be the url-encoded path to a file that should be
// included in the problem report.
// Note: if, as hoped, this component proves to be more generally useful, not just for TC problems,
// we will need to add some way to control whether the message includes "in the TEam Collection system."
export const BookProblem: React.FunctionComponent<{
    errorMessage: string;
    clickHereArg: string;
    className?: string; // also supports Emotion
}> = (props) => {
    const bookProblemMessage = useApiString(
        "common/problemWithBookMessage",
        "There was a problem with the current book in the Team Collection System",
    );

    const clickHereForHelpMessage = useApiString(
        "common/clickHereForHelp?problem=" + props.clickHereArg,
        "Please click here to get help from the Bloom support team.",
    );

    return (
        <IconHeadingBodyMenuPanel
            css={css`
                a {
                    // Trying to match the red in the Attention.svg. Paint says that is (234,0,0)
                    // but subjectively, perhaps because the red in the icon covers a larger area,
                    // it looks much brighter than the red in the link. To my eye, the link
                    // looks darker than the icon even when it's plain red, let alone when it's any
                    // dimmer, so I'm just going with red.
                    color: red;
                }
            `}
            className={props.className}
            heading={bookProblemMessage}
            body={props.errorMessage + " " + clickHereForHelpMessage}
            icon={<img src={"/bloom/images/Attention.svg"} alt="" />}
            menu={undefined}
        />
    );
};
