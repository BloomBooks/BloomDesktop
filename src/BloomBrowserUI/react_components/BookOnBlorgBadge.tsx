import { css } from "@emotion/react";

import * as React from "react";
import { post } from "../utils/bloomApi";
import { BloomTooltip } from "./BloomToolTip";
import { Link } from "../react_components/link";
import { LocalizedString } from "./l10nComponents";

// What Bloom tells the Collections tab about a book that is on Bloom Library
// (BloomLibraryStatus.GetBadgeInfo in C#).
export interface IBloomLibraryBadgeInfo {
    draft: boolean;
    inCirculation: boolean;
    harvestState: string;
}

enum BadgeType {
    None,
    Harvesting,
    Published,
    Draft,
    Problem,
}

const getBadgeType = (status: IBloomLibraryBadgeInfo | undefined) => {
    if (!status) {
        return BadgeType.None;
    }
    if (
        !status.inCirculation ||
        status.harvestState === "failed" ||
        status.harvestState === "failedindefinitely" ||
        status.harvestState === "multiple"
    ) {
        return BadgeType.Problem;
    }
    if (status.harvestState === "inprogress") {
        return BadgeType.Harvesting;
    }
    if (status.draft) {
        return BadgeType.Draft;
    }
    return BadgeType.Published;
};

export const BookOnBlorgBadge: React.FunctionComponent<{
    book: any;
    // Undefined if the book is not on Bloom Library (or we don't know yet).
    status: IBloomLibraryBadgeInfo | undefined;
}> = (props) => {
    const badge = getBadgeType(props.status);

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
                            <div
                                // The tooltip is rendered in a portal, but React still bubbles its clicks
                                // to the BookButton, which would select the book. Opening the book's page
                                // on Bloom Library should not do that.
                                onClick={(e) => e.stopPropagation()}
                            >
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
                                    // Bloom builds the url only when the link is clicked, since that
                                    // can need a server lookup we don't want while the collection loads.
                                    onClick={() =>
                                        post(
                                            `collections/openBookOnBloomLibrary?book-id=${props.book.id}`,
                                        )
                                    }
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
