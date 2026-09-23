import { css } from "@emotion/react";

import * as React from "react";
import { get } from "../utils/bloomApi";
import { useState } from "react";
import { useMountEffect } from "../utils/useMountEffect";
import { BloomTooltip } from "./BloomToolTip";
import { Link } from "../react_components/link";
import { useSubscribeToWebSocketForStringMessage } from "../utils/WebSocketManager";
import { LocalizedString } from "./l10nComponents";

export const BookOnBlorgBadge: React.FunctionComponent<{
    book: any;
}> = (props) => {
    const [bookOnBlorgUrl, setBookOnBlorgUrl] = useState("");

    enum BadgeType {
        None,
        Harvesting,
        Published,
        Draft,
        Problem,
    }

    const [badge, setBadge] = useState<BadgeType>(BadgeType.None);

    const updateBadge = () => {
        get(
            `collections/getBookOnBloomBadgeInfo?book-id=${props.book.id}`,
            (result) => {
                if (result.data.bookUrl) {
                    setBookOnBlorgUrl(result.data.bookUrl);

                    if (
                        // if inCirculation is null or undefined, book is in circulation
                        result.data.inCirculation === false ||
                        result.data.harvestState === "failed" ||
                        result.data.harvestState === "failedindefinitely" ||
                        result.data.harvestState === "multiple"
                    ) {
                        setBadge(BadgeType.Problem);
                    } else if (result.data.harvestState === "inprogress") {
                        setBadge(BadgeType.Harvesting);
                    } else if (result.data.draft) {
                        setBadge(BadgeType.Draft);
                    } else {
                        setBadge(BadgeType.Published);
                    }
                } else {
                    setBadge(BadgeType.None);
                }
            },
            () => {
                // A transient network failure (e.g. offline, server not ready) should not
                // throw an unhandled promise rejection to Sentry (BLOOM-DESKTOP-FFG). Silently
                // leave the badge in its previous state; the websocket subscription above will
                // refresh it when the server next reports a change.
            },
        );
    };

    useSubscribeToWebSocketForStringMessage(
        "bookCollection",
        "updateBookBadge",
        (idMsg) => {
            if (idMsg === props.book.id) {
                updateBadge();
            }
        },
    );

    // Fetch the badge once when the component mounts. Previously this was a bare
    // useEffect with no dependency array, so it re-ran on every render; with many badges
    // visible in a collection, any re-render (websocket broadcast, selection change) refired
    // every badge's network request, amplifying transient network hiccups into bursts of
    // errors (BLOOM-DESKTOP-FFG). The websocket subscription above handles later updates.
    useMountEffect(updateBadge);

    return (
        <div
            css={css`
                position: absolute;
                bottom: -15px;
                right: -3px;
            `}
        >
            <React.Fragment>
                {badge !== BadgeType.None && (
                    <BloomTooltip
                        placement={"right"}
                        enableClickInTooltip={true}
                        tip={
                            <div>
                                <span
                                    css={css`
                                        font-variant: all-small-caps;
                                        display: block;
                                        text-align: center;
                                        line-height: 1.5;
                                    `}
                                >
                                    {badge === BadgeType.Published ? (
                                        <LocalizedString l10nKey="CollectionTab.OnBlorgBadge.Published">
                                            Published
                                        </LocalizedString>
                                    ) : badge === BadgeType.Draft ? (
                                        <LocalizedString l10nKey="CollectionTab.OnBlorgBadge.MarkedAsDraft">
                                            Marked As Draft
                                        </LocalizedString>
                                    ) : badge === BadgeType.Harvesting ? (
                                        <LocalizedString l10nKey="CollectionTab.OnBlorgBadge.Harvesting">
                                            In Progress
                                        </LocalizedString>
                                    ) : (
                                        <LocalizedString l10nKey="CollectionTab.OnBlorgBadge.Problem">
                                            Problem
                                        </LocalizedString>
                                    )}
                                </span>
                                <Link
                                    l10nKey="CollectionTab.OnBlorgBadge.ViewOnBlorg"
                                    href={bookOnBlorgUrl}
                                    css={css`
                                        text-decoration: underline;
                                    `}
                                >
                                    View on BloomLibrary.org
                                </Link>
                            </div>
                        }
                    >
                        <img
                            css={
                                badge === BadgeType.Draft &&
                                css`
                                    width: 50px;
                                `
                            }
                            title="" // overwrite ancestor's title so we don't get two tooltips
                            src={
                                badge === BadgeType.Published
                                    ? "/bloom/images/on-blorg-badges/on-blorg-normal.svg"
                                    : badge === BadgeType.Draft
                                      ? "/bloom/images/on-blorg-badges/on-blorg-draft.svg"
                                      : badge === BadgeType.Harvesting
                                        ? "/bloom/images/on-blorg-badges/on-blorg-harvesting.svg"
                                        : "/bloom/images/on-blorg-badges/on-blorg-problem.svg"
                            }
                        />
                    </BloomTooltip>
                )}
            </React.Fragment>
        </div>
    );
};
