/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import Grid from "@mui/material/Grid";
import React = require("react");
import { useState, useEffect } from "react";
import "BooksOfCollection.less";
import { useApiData, useWatchApiData } from "../utils/bloomApi";
import {
    BookButton,
    bookButtonHeight,
    BookButtonPlaceHolder
} from "./BookButton";
import { BookSelectionManager } from "./bookSelectionManager";
import LazyLoad, { forceVisible } from "react-lazyload";
import { Link } from "../react_components/link";
import { kBloomBlue } from "../bloomMaterialUITheme";

export interface IBookInfo {
    id: string;
    title: string;
    collectionId: string;
    folderPath: string;
    isFactory: boolean;
}

// A very minimal set of collection properties for now
export interface ICollection {
    isEditableCollection: boolean;
    isFactoryInstalled: boolean;
    containsDownloadedBooks: boolean;
    id: string;
}

export const BooksOfCollection: React.FunctionComponent<{
    collectionId: string;
    isEditableCollection: boolean;
    manager: BookSelectionManager;
    isSpreadsheetFeatureActive: boolean;
    // If true, the collection will be wrapped in a LazyLoad so that most of its rendering
    // isn't done until it is visible on screen.
    lazyLoadCollection?: boolean;
}> = props => {
    if (!props.collectionId) {
        window.alert("null collectionId");
    }
    const collectionQuery = `collection-id=${encodeURIComponent(
        props.collectionId
    )}`;

    const books = useWatchApiData<Array<IBookInfo>>(
        `collections/books?${collectionQuery}`,
        [],
        "editableCollectionList",
        "reload:" + props.collectionId
    );

    //const selectedBookInfo = useMonitorBookSelection();
    const collection: ICollection = useApiData(
        `collections/collectionProps?${collectionQuery}`,
        {
            isEditableCollection: props.isEditableCollection,
            isFactoryInstalled: true,
            containsDownloadedBooks: false,
            id: props.collectionId
        }
    );
    // not getting these from the api currently, and I'm not sure the initial defaults will carry over
    // when we get data from the API.
    collection.isEditableCollection = props.isEditableCollection;
    collection.id = props.collectionId;

    const [reload, setReload] = useState(0);
    const [reloadTrigger, setReloadTrigger] = useState("");
    const reloadBooks = (id: string) => {
        setReload(reload + 1);
        setReloadTrigger(id);
    };
    useEffect(() => {
        forceVisible();
    }, [reload, reloadTrigger]);

    // This is an approximation. 5 buttons per line is about what we get in a default
    // layout on a fairly typical screen. We'd get a better approximation if we used
    // the width of a button and knew the width of the container. But I think this is good
    // enough. Worst case, we expand a bit more than we need.
    const collectionHeight = bookButtonHeight * Math.ceil(books.length / 5);

    const content = (
        <div
            key={"BookCollection-" + props.collectionId}
            className="bookButtonPane"
            style={{ cursor: "context-menu" }}
        >
            {books.length > 0 && (
                <Grid
                    container={true}
                    spacing={0}
                    direction="row"
                    justifyContent="flex-start"
                    alignItems="flex-start"
                >
                    {books?.map(book => {
                        return (
                            <Grid
                                item={true}
                                key={book.id}
                                className="book-wrapper"
                            >
                                <LazyLoad
                                    height={bookButtonHeight}
                                    // Tells lazy loader to look for the parent element that has overflowY set to scroll or
                                    // auto. This requires a patch to react-lazyload (as of 3.2.0) because currently it looks for
                                    // a parent that has overflow:scroll or auto in BOTH directions, which is not what we're getting
                                    // from our splitter.
                                    // Note: using this is better than using splitContainer, because that has multiple bugs
                                    // that are not as easy to patch. See https://github.com/twobin/react-lazyload/issues/371.
                                    overflow={true}
                                    resize={true} // expand lazy elements as needed when container resizes
                                    // We need to specify a placeholder because the default one has zero width,
                                    // and therefore the parent grid thinks they will all fit on one line,
                                    // and then they're all visible so we get no laziness.
                                    placeholder={
                                        <BookButtonPlaceHolder
                                            book={book}
                                            reload={reloadBooks}
                                        />
                                    }
                                >
                                    <BookButton
                                        key={book.id}
                                        book={book}
                                        collection={collection}
                                        manager={props.manager}
                                        isSpreadsheetFeatureActive={
                                            props.isSpreadsheetFeatureActive
                                        }
                                    />
                                </LazyLoad>
                            </Grid>
                        );
                    })}
                </Grid>
            )}
            {collection.containsDownloadedBooks && (
                <Link
                    l10nKey="CollectionTab.BloomLibraryLinkLabel"
                    href="https://bloomlibrary.org"
                    // Make this look like "Show another collection..."
                    css={css`
                        text-transform: uppercase;
                        font-size: initial;
                    `}
                >
                    Get more source books at BloomLibrary.org
                </Link>
            )}
        </div>
    );
    // There's no point in lazily loading an empty list of books. But more importantly, on early renders
    // before we actually retrieve the list of books, books is always an empty array. If we render a
    // LazyLoad at that point, it will have height zero, and then all of them fit on the page, and the
    // LazyLoad code determines that they are all visible and expands all of them, and we don't get any
    // laziness at all.
    return props.lazyLoadCollection && books.length > 0 ? (
        <LazyLoad
            height={collectionHeight}
            // See comment in the other LazyLoad above.
            overflow={true}
            resize={true} // expand lazy elements as needed when container resizes
        >
            {content}
        </LazyLoad>
    ) : (
        content
    );
};
