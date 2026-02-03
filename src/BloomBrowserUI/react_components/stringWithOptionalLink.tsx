import * as React from "react";
import { post, postString } from "../utils/bloomApi";

// Display a string which may have embedded a link, in the usual HTML format,
// for example, "There is a problem. Please click <a href='...'>here</a> to report it".
// (Currently the href must use single quotes.)
// If there is no link, the result is a single span, the message.
// If there are links, we interleave spans and anchor tags for each segment.
// Currently the href is assumed to be something to send to our API,
// but NOT to actually navigate to. We could support more options as needed.
export const StringWithOptionalLink: React.FunctionComponent<{
    message: string;
}> = (props) => {
    const linkRegex = /<a[^>]*?href='([^>']+)'[^>]*>(.*?)<\/a>/g;
    const elements: React.ReactNode[] = [];
    let lastIndex = 0;
    let segmentIndex = 0;
    let match: RegExpExecArray | null;

    while ((match = linkRegex.exec(props.message))) {
        const precedingText = props.message.slice(lastIndex, match.index);
        if (precedingText) {
            elements.push(
                <span key={`text-${segmentIndex}`}>{precedingText}</span>,
            );
            segmentIndex++;
        }

        const rawHref = match[1];
        const isExternalLink =
            rawHref.startsWith("http") || rawHref.startsWith("mailto");
        const href = rawHref.replace("/bloom/api/", "");

        elements.push(
            <a
                key={`link-${segmentIndex}`}
                // We don't currently use the href, but to get link formatting it
                // has to be present. May also be helpful for accessibility.
                href={rawHref}
                onClick={(e) => {
                    e.preventDefault(); // so it doesn't try to follow the link
                    if (isExternalLink) {
                        postString("link", rawHref);
                        return;
                    }
                    post(href);
                }}
            >
                {match[2]}
            </a>,
        );
        segmentIndex++;
        lastIndex = linkRegex.lastIndex;
    }

    const trailingText = props.message.slice(lastIndex);
    if (trailingText) {
        elements.push(<span key={`text-${segmentIndex}`}>{trailingText}</span>);
    }

    if (elements.length === 0) {
        return <span>{props.message}</span>;
    }

    return <React.Fragment>{elements}</React.Fragment>;
};
