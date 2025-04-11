/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useEffect, useState } from "react";
import { get, useWatchApiData } from "../utils/bloomApi";
import { ProgressBar } from "../react_components/Progress/ProgressBar";
import { kBloomBlue } from "../bloomMaterialUITheme";
import { useL10n } from "../react_components/l10nHooks";
import { BloomTooltip } from "../react_components/BloomToolTip";
import { Link } from "../react_components/link";

export const BooksOnBlorgProgressBar: React.FunctionComponent = () => {
    const [percentage, setPercentage] = useState(-1); // When calculated this will be [0, 100]

    const [languageName, setLanguageName] = useState("");
    const [languageCode, setLanguageCode] = useState("");
    const [webGoal, setWebGoal] = useState(-1);

    const bookCount = useWatchApiData(
        "collections/getBookCountByLanguage",
        -1,
        "booksOnBlorg",
        "reload"
    );

    const goalLabel = useL10n(
        "Goal:",
        "BooksOnBlorg.Progress.Goal",
        "Goal label under progress bar on Collection tab."
    );
    const countOfBooksLabel = useL10n(
        "{0} {1} books on BloomLibrary.org",
        "BooksOnBlorg.Progress.CountOfBooksLabel",
        "Books on website label under progress bar on Collection tab.",
        bookCount.toString(),
        languageName.length > 12
            ? languageName.substring(0, 11) + "..."
            : languageName
    );

    useEffect(() => {
        get("settings/webGoal", g => {
            setWebGoal(g.data);
        });
        get("settings/languageData", langData => {
            if (langData?.data) {
                setLanguageName(langData.data.languageName);
                setLanguageCode(langData.data.languageCode);
            }
        });
    }, []);

    // Verify label is ready for display, i.e. `useL10n` hook has completed.
    // It was harder than expected to avoid a momentary "-1 language books..."
    // We don't set the percentage until the label we are going to use no longer has the curly brace
    // placeholder and no longer has a "-1" in it.
    // N.B. The percentage value governs the height of the progress bar, so no percentage,
    // no progress bar.
    //
    // I (Andrew), in review, wanted to suggest that we have a callback from useL10n which could be used
    // to deal with a situation like this. But the hook is designed to allow for the params to be passed
    // in even after the initial result. Thus I couldn't see any way to guarantee such a callback only
    // fires after all the pieces are in place.

    const isLabelReadyForDisplay =
        !countOfBooksLabel.startsWith("{0}") &&
        !countOfBooksLabel.startsWith("-1");

    useEffect(() => {
        // Make sure we have all the necessary data to display the progress bar before we
        // set the percentage.
        if (
            webGoal > -1 &&
            bookCount > -1 &&
            languageName !== "" &&
            isLabelReadyForDisplay
        ) {
            // percentage bounds are 0-100.
            if (webGoal === 0) {
                setPercentage(100);
            } else {
                setPercentage(
                    Math.max(Math.min((bookCount / webGoal) * 100, 100), 0)
                );
            }
        }
    }, [webGoal, bookCount, languageName, isLabelReadyForDisplay]);

    const duration = 600;
    const transitionStyle = `transition: height ${duration}ms;`;
    const height = percentage < 0 ? 0 : 34; // max must be high enough for taller fonts like arabic
    const tooltipHref = `https://bloomlibrary.org/language:${languageCode}`;

    return (
        <div
            css={css`
                width: 312px;
            `}
        >
            <BloomTooltip
                placement={"right"}
                enableClickInTooltip={true}
                tip={
                    <div>
                        <Link
                            l10nKey="CollectionTab.OnBlorgBadge.ViewOnBlorg"
                            href={tooltipHref}
                            css={css`
                                text-decoration: underline;
                            `}
                        >
                            View on BloomLibrary.org
                        </Link>
                    </div>
                }
            >
                <div
                    css={css`
                        display: flex;
                        ${transitionStyle}
                        flex-direction: column;
                        height: ${height}px;
                        overflow: hidden;
                    `}
                >
                    <div
                        css={css`
                            display: flex;
                            justify-content: space-between;
                            flex-direction: row;
                            font-size: 12px;
                            color: ${kBloomBlue};
                            margin-top: 2px;
                            margin-bottom: 2px;
                        `}
                    >
                        <div>{countOfBooksLabel}</div>
                        <div
                            css={css`
                                align-self: center;
                            `}
                        >
                            {goalLabel} {webGoal}
                        </div>
                    </div>
                    <ProgressBar percentage={percentage} />
                </div>
            </BloomTooltip>
        </div>
    );
};
