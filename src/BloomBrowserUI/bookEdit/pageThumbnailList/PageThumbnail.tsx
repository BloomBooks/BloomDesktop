import * as React from "react";
import { useState, useContext, useEffect } from "react";
import * as ReactDOM from "react-dom";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";

import "errorHandler";
import WebSocketManager from "../../utils/WebSocketManager";
import { Responsive, WidthProvider } from "react-grid-layout";
import { BloomApi } from "../../utils/bloomApi";
import { IPage } from "./pageThumbnailList";

// Global information across all PageThumbnails...see comments in requestPage()
let lastPageRequestTime = 0;
let activePageRequestCount = 0;
let pendingPageRequestCount = 0;

// Component to display the thumbnail of one page, with its caption and possibly
// an overflow indicator.
export const PageThumbnail: React.FunctionComponent<{
    page: IPage;
    left: boolean;
    pageSize: string;
    // PageThumbnail will call this function to provide the client with
    // a callback that the client can call to get the page thumbnail to
    // refresh itself by re-doing the axios call that gets the page content.
    configureReloadCallback: (id: string, callback: () => void) => void;
    onClick: React.MouseEventHandler<HTMLDivElement>;
}> = props => {
    // The initial content here is a blank page. It will be replaced with
    // the real content when we retrieve it from the server.
    const [content, setContent] = useState(
        `<div class="${props.pageSize} bloom-page side-${
            props.left ? "left" : "right"
        }"><div class="marginBox">content</div></div>`
    );
    // This is something of a hack. When the page content needs to be reloaded,
    // an event comes to the parent list. Using the key, it will look up our callback
    // function and call it. This increments the value. A new value in reloadValue
    // causes the useEffect below to run again and request the new page content.
    const [reloadValue, setReloadValue] = useState(1);
    props.configureReloadCallback(props.page.key, () =>
        setReloadValue(reloadValue + 1)
    );
    // Get the actual page content. This is appreciably slow...80ms or so on
    // a fast desktop for a complex page...mainly because of XhtmlToHtml conversion.
    // So we do it lazily after setting up the initial framework of pages.
    const requestPage = () => {
        // We don't want a lot of page requests running at the same time.
        // There are various limits on simultaneous requests, including
        // the number of threads in the BloomServer and the number of active
        // requests the browser allows to the same origin. If we use too
        // many, we may starve the main page or the toolbox, which are often
        // loading at the same time. The activePageRequestCount is used
        // to prevent more than four running at the same time, and the
        // pending count helps us estimate how long to wait if we can't do
        // it at once. Both are global across all instances of PageThumbnail.
        // The current value is quite a bit shorter than the time (80ms) that
        // it was taking to generate a complex page on a developer machine.
        // The timeout doesn't cost much, and we don't want to slow things down
        // for simple pages on fast machines.
        if (activePageRequestCount > 3) {
            window.setTimeout(requestPage, pendingPageRequestCount * 5);
            return;
        }
        pendingPageRequestCount--;
        activePageRequestCount++;
        BloomApi.get(`pageList/pageContent?id=${props.page.key}`, response => {
            activePageRequestCount--;
            setContent(response.data.content); // automatically unJsonified?
        });
    };
    const reForOverflow = /^[^>]*class="[^"]*pageOverflows/;
    const overflowing = reForOverflow.test(content); // enhance: memo?
    useEffect(() => {
        if (Math.abs(Date.now() - lastPageRequestTime) > 5000) {
            activePageRequestCount = 0; // something weird happened, don't block forever
            pendingPageRequestCount = 0;
        }
        lastPageRequestTime = Date.now();
        pendingPageRequestCount++;
        requestPage();
    }, [reloadValue]);
    return (
        <div>
            {props.page.key === "placeholder" || (
                <>
                    <div className={"pageContainer " + props.pageSize}>
                        <div
                            dangerouslySetInnerHTML={{
                                __html: content
                            }}
                        />
                        {/* This div overlays the page and intercepts clicks that might otherwise
                        do something within the HTML of the thumbnail (plus sending those clicks
                        to the appropriate handler for a thumbnail click) */}
                        <div
                            className="invisibleThumbnailCover"
                            onClick={props.onClick}
                        />
                        {overflowing && <div className="pageOverflowsIcon" />}
                    </div>
                    <div className="thumbnailCaption">{props.page.caption}</div>
                </>
            )}
        </div>
    );
};
