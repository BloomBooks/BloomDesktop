/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { useState } from "react";
import { BloomApi } from "../../utils/bloomApi";
import { TeamCollectionBookStatusPanel } from "../../teamCollection/TeamCollectionBookStatusPanel";
import { useSelectedBookInfo } from "../../app/selectedBook";
import BloomButton from "../../react_components/bloomButton";
import { kDarkestBackground } from "../../bloomMaterialUITheme";
import { CollectionsTabPane } from "../CollectionsTabPane";
import { WireUpForWinforms } from "../../utils/WireUpWinform";

export const CollectionsTabBookPane: React.FunctionComponent<{}> = props => {
    const [isTeamCollection, setIsTeamCollection] = useState(false);

    const { id: selectedBookId, editable, canMakeBook } = useSelectedBookInfo();

    React.useEffect(
        () => {
            BloomApi.getBoolean(
                "teamCollection/isTeamCollectionEnabled",
                teamCollection => setIsTeamCollection(teamCollection)
            );
        },
        [] /* means once and never again */
    );

    return (
        <div
            css={css`
                height: 100%;
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
                `}
            >
                {canMakeBook || (
                    <BloomButton
                        enabled={editable}
                        variant={"outlined"}
                        l10nKey="CollectionTab.EditBookButton"
                        clickApiEndpoint="app/editSelectedBook"
                        mightNavigate={true}
                        enabledImageFile="EditTab.svg"
                        disabledImageFile="EditTab.svg"
                        hasText={true}
                        color="secondary"
                        css={css`
                            background-color: white !important;
                            color: ${editable
                                ? "black !important"
                                : "rgba(0, 0, 0, 0.26);"};

                            img {
                                height: 2em;
                                margin-right: 10px;
                            }
                        `}
                    >
                        Edit this book
                    </BloomButton>
                )}
                {canMakeBook && (
                    <BloomButton
                        enabled={true}
                        variant={"outlined"}
                        l10nKey="CollectionTab.MakeBookUsingThisTemplate"
                        clickApiEndpoint="app/makeFromSelectedBook"
                        mightNavigate={true}
                        enabledImageFile="newBook.png"
                        hasText={true}
                        color="secondary"
                        css={css`
                            background-color: white !important;
                            color: ${editable
                                ? "black !important"
                                : "rgba(0, 0, 0, 0.26);"};

                            img {
                                height: 2em;
                                margin-right: 10px;
                            }
                        `}
                    >
                        Make a book using this source
                    </BloomButton>
                )}
            </div>
            <div
                css={css`
                    // We want the preview to take up the available space, limiting the Team Collection panel
                    // (if present) to what it needs.
                    flex-grow: 4;
                    width: 100vw;
                    overflow-y: hidden; // inner iframe shows scrollbars as needed
                    overflow-x: hidden;
                `}
            >
                <iframe
                    src={`/book-preview/index.htm?dummy=${selectedBookId}`}
                    height="100%"
                    width="100%"
                    css={css`
                        border: none;
                    `}
                />
            </div>
            {// Currently, canMakeBook is a synonym for 'book is not in the  current TC'
            // If that stops being true we might need another more specialized status flag.
            isTeamCollection && !canMakeBook ? (
                <div id="teamCollection">
                    <TeamCollectionBookStatusPanel />
                </div>
            ) : null}
        </div>
    );
};
