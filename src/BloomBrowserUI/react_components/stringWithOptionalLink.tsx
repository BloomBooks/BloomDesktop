import * as React from "react";
import { post } from "../utils/bloomApi";

// Display a string which may have embedded a link, in the usual HTML format,
// for example, "There is a problem. Please click <a href='...'>here</a> to report it".
// (Currently the href must use single quotes.)
// If there is no link, the result is a single span, the message.
// If there is a link, we get a span, a link, and another span.
// Currently the href is assumed to be something to send to our API,
// but NOT to actually navigate to. We could support more options as needed.
export const StringWithOptionalLink: React.FunctionComponent<{
    message: string;
}> = (props) => {
    const match = props.message.match(
        /^(.*?)<a[^>]*?href='([^>']+)'[^>]*>(.*?)<\/a>(.*)$/,
    );
    if (match) {
        const href = match[2].replace("/bloom/api/", "");
        return (
            <React.Fragment>
                <span>{match[1]}</span>
                <a
                    // We don't currently use the href, but to get link formatting it
                    // has to be present. May also be helpful for accessibility.
                    href={match[2]}
                    onClick={(e) => {
                        e.preventDefault(); // so it doesn't try to follow the link
                        post(href);
                    }}
                >
                    {match[3]}
                </a>
                <span>{match[4]}</span>
            </React.Fragment>
        );
    } else return <span>{props.message}</span>;
};
