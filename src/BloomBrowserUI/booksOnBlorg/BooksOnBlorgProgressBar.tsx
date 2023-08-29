/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useEffect, useState } from "react";
import { get } from "../utils/bloomApi";
import { ProgressBar } from "../react_components/Progress/ProgressBar";
import { kBloomBlue } from "../bloomMaterialUITheme";
import { useL10n } from "../react_components/l10nHooks";

export const BooksOnBlorgProgressBar: React.FunctionComponent = () => {
    const [percentage, setPercentage] = useState(-1); // When calculated this will be [0, 100]

    const [bookCount, setBookCount] = useState(-1);

    const [languageName, setLanguageName] = useState("");
    const [webGoal, setWebGoal] = useState(-1);

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
        languageName
    );

    useEffect(() => {
        get("settings/webGoal", g => {
            setWebGoal(g.data);
        });
        get("settings/languageName", n => {
            setLanguageName(n.data);
        });
        // Get book count from bloomlibrary.org of books in this language.
        get("collections/getBookCountByLanguage", response => {
            const bookCount = response.data;
            if (bookCount > -1) {
                setBookCount(response.data);
            }
        });
    }, []);

    useEffect(() => {
        // Make sure we have all the necessary data to display the progress bar before we
        // set the percentage.
        if (
            webGoal > -1 &&
            bookCount > -1 &&
            languageName !== "" &&
            countOfBooksLabel.indexOf("{0}") === -1 // Verify 'translated' status.
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
    }, [webGoal, bookCount, languageName, countOfBooksLabel]);

    const duration = 600;
    const transitionStyle = `transition: height ${duration}ms;`;
    const height = percentage < 0 ? 0 : 25;

    return (
        <div
            css={css`
                display: flex;
                ${transitionStyle}
                flex-direction: column;
                height: ${height}px;
                width: 260px;
                overflow: hidden;
            `}
        >
            <div
                css={css`
                    display: flex;
                    justify-content: space-between;
                    flex-direction: row;
                    font-size: 10px;
                    color: ${kBloomBlue};
                    margin-top: 2px;
                `}
            >
                <div>{countOfBooksLabel}</div>
                <div>
                    {goalLabel} {webGoal}
                </div>
            </div>
            <ProgressBar percentage={percentage} />
        </div>
    );
};
