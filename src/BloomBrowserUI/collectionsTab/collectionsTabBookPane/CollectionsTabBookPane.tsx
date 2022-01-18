/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { useRef, useState } from "react";
import { BloomApi } from "../../utils/bloomApi";
import { TeamCollectionBookStatusPanel } from "../../teamCollection/TeamCollectionBookStatusPanel";
import {
    IBookTeamCollectionStatus,
    initialBookStatus
} from "../../teamCollection/teamCollectionApi";
import { useMonitorBookSelection } from "../../app/selectedBook";
import BloomButton from "../../react_components/bloomButton";
import { kDarkestBackground } from "../../bloomMaterialUITheme";
import { useSubscribeToWebSocketForEvent } from "../../utils/WebSocketManager";

export const CollectionsTabBookPane: React.FunctionComponent<{
    // If false, as it usually is, the overlay above the preview iframe
    // is set to pointer-events:none which allows events through to the iframe.
    // In this state, the iframe captures those events and dragging a splitter
    // in the parent won't work. When this is true (typically while dragging the splitter),
    // the overlay does not allow the events through to be captured, and the
    // splitter works.
    disableEventsInIframe: boolean;
}> = props => {
    const [isTeamCollection, setIsTeamCollection] = useState(false);
    const [bookStatus, setBookStatus] = useState(initialBookStatus);
    const [reload, setReload] = useState(0);
    // Force a reload when told the book changed, even if it's the same book [id]
    useSubscribeToWebSocketForEvent("bookStatus", "reload", () =>
        setReload(old => old + 1)
    );

    const {
        id: selectedBookId,
        saveable,
        collectionKind
    } = useMonitorBookSelection();

    React.useEffect(() => {
        BloomApi.get(
            "teamCollection/selectedBookStatus",
            data => {
                setBookStatus(data.data as IBookTeamCollectionStatus);
            },
            err => {
                // Something went wrong. Maybe not registered. Already reported to Sentry, we don't need
                // another 'throw' here, with less information. Displaying the message may tell the user
                // something. I don't think it's worth localizing the fallback message here, which is even
                // less likely to be seen.
                // Enhance: we could display a message telling them to register and perhaps a link to the
                // registration dialog.
                const errorMessage =
                    err?.response?.statusText ??
                    "Bloom could not determine the status of this book";
                setBookStatus({ ...bookStatus, error: errorMessage });
            }
        );
    }, [selectedBookId, saveable, reload]);

    const canMakeBook = collectionKind != "main";

    const iframeRef = useRef<HTMLIFrameElement>(null);

    React.useEffect(() => {
        BloomApi.getBoolean(
            "teamCollection/isTeamCollectionEnabled",
            teamCollection => setIsTeamCollection(teamCollection)
        );
        // This code SHOULD suppress mousedown events in the iframe, except in the scroll bar,
        // thus preventing the user from doing anything much and allowing us to retire the
        // old code and CSS that we inject into the preview document. But the onload event
        // never fires, and if I try adding the mousedown handler at once, THAT never fires.
        // Sticking with the old previewDom for now, but keeping this code as we may try again.
        // console.log(
        //     "body is " + iframeRef.current?.contentWindow?.document.body
        // );
        // iframeRef.current?.contentWindow?.addEventListener("onload", () => {
        //     console.log("added mousdown listener");
        //     iframeRef.current?.contentWindow?.document.body.addEventListener(
        //         "mousedown",
        //         event => {
        //             if (
        //                 event.clientX <
        //                 (event.currentTarget as any).clientWidth - 20
        //             ) {
        //                 console.log("preventing event");
        //                 event.preventDefault();
        //                 event.stopPropagation();
        //             }
        //         },
        //         { capture: true }
        //     );
        // });
    }, [
        // This could change if initial (or later) selection is a source book.
        // But it only triggers the useEffect if the collectionKind actually changes.
        collectionKind
    ]);

    // Note: If canMakeBook is true, then saveable is probably false (the source book is likely not in the editable collection),
    // but you still want the button to be enabled
    const isButtonEnabled = canMakeBook || saveable;
    const editOrMakeButton: JSX.Element | boolean = collectionKind !==
        "error" && (
        <BloomButton
            enabled={isButtonEnabled}
            variant={"outlined"}
            l10nKey={
                canMakeBook
                    ? "CollectionTab.MakeBookUsingThisTemplate"
                    : "CollectionTab.EditBookButton"
            }
            clickApiEndpoint={
                canMakeBook
                    ? "app/makeFromSelectedBook"
                    : "app/editSelectedBook"
            }
            mightNavigate={true}
            enabledImageFile={canMakeBook ? "newBook.png" : "EditTab.svg"}
            disabledImageFile={canMakeBook ? undefined : "EditTab.svg"}
            hasText={true}
            color="secondary"
            css={css`
                background-color: white !important;
                color: ${isButtonEnabled
                    ? "black"
                    : "rgba(0, 0, 0, 0.26)"} !important;
                img {
                    height: 2em;
                    margin-right: 10px;
                }
            `}
        >
            {canMakeBook ? "Make a book using this source" : "Edit this book"}
        </BloomButton>
    );

    return (
        <div
            css={css`
                height: 100%;
                box-sizing: border-box;
                display: flex;
                flex: 1;
                flex-direction: column;
                padding: 10px;
                background-color: ${kDarkestBackground};
            `}
            {...props} // allows defining more css rules from container
        >
            <div
                css={css`
                    margin-bottom: 10px;
                    flex-grow: 0;
                `}
            >
                {editOrMakeButton}
            </div>
            <div
                css={css`
                    // We want the preview to take up the available space, limiting the Team Collection panel
                    // (if present) to what it needs.
                    flex-grow: 4;
                    width: 100%;
                    overflow-y: hidden; // inner iframe shows scrollbars as needed
                    overflow-x: hidden;
                    position: relative; // makes it the parent from which the overlay takes its size
                `}
            >
                <div
                    // This div overlays the book preview iframe.
                    // Without it, react-split-pane does not work properly with iframes:
                    // https://github.com/tomkp/react-split-pane/issues/30. Things go badly haywire
                    // any time you drag the splitter and move into the iframe.
                    // With the commented-out hover rule, it could also replace the CSS we inject
                    // into the preview to discourage users from trying to edit the preview.
                    // However, I can't yet succeed in disabling editing in the preview iframe
                    // from outside it (except when this overlay doesn't have pointer-events:none,
                    // but then mouse wheel events are disabled in the preview).
                    css={css`
                        position: absolute;
                        left: 0;
                        top: 0;
                        height: 100%;
                        width: calc(100% - 20px);
                        z-index: 10;
                        pointer-events: ${
                            props.disableEventsInIframe ? "auto" : "none"
                        };

                        /* by subtly darkening the iframe when the mouse moves over it and setting
                    // the not-allowed cursor, we give the user a hint that this is not where to edit.
                    This works, but we're not ready to disable the old preview-mode stuff until we can
                    actually prevent user editing in the preview.
                     :hover {
                            opacity: 0.4;
                            background-color: ${kDarkestBackground};
                            cursor: not-allowed;
                        } */
                    `}
                ></div>
                <iframe
                    src={`/book-preview/index.htm?dummy=${(selectedBookId ??
                        "") + reload}`}
                    height="100%"
                    width="100%"
                    css={css`
                        border: none;
                    `}
                    ref={iframeRef}
                />
            </div>
            {// Currently, canMakeBook is a synonym for 'book is not in the current TC'
            // If that stops being true we might need another more specialized status flag.
            isTeamCollection && !canMakeBook ? (
                <div id="teamCollection">
                    <TeamCollectionBookStatusPanel {...bookStatus} />
                </div>
            ) : null}
        </div>
    );
};
