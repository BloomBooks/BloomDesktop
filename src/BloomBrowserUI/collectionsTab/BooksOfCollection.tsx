import Grid from "@material-ui/core/Grid";
import React = require("react");
import "BooksOfCollection.less";
import { BloomApi } from "../utils/bloomApi";
import Menu from "@material-ui/core/Menu";
import MenuItem from "@material-ui/core/MenuItem";
import NestedMenuItem from "material-ui-nested-menu-item";
import { BookButton, bookButtonHeight, bookButtonWidth } from "./BookButton";
import { useMonitorBookSelection } from "../app/selectedBook";
import { element } from "prop-types";
import { useL10n } from "../react_components/l10nHooks";
import { useEffect, useState } from "react";
import { useSubscribeToWebSocketForEvent } from "../utils/WebSocketManager";
import { Divider } from "@material-ui/core";
import { BookSelectionManager, useIsSelected } from "./bookSelectionManager";
import LazyLoad, { forceCheck } from "react-lazyload";

export interface IBookInfo {
    id: string;
    title: string;
    collectionId: string;
    folderName: string;
    factory: boolean;
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

    const books = BloomApi.useWatchApiData<Array<IBookInfo>>(
        `collections/books?${collectionQuery}`,
        [],
        "editableCollectionList",
        "reload:" + props.collectionId
    );

    //const selectedBookInfo = useMonitorBookSelection();
    const collection: ICollection = BloomApi.useApiData(
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

    const [contextMousePoint, setContextMousePoint] = React.useState<
        | {
              mouseX: number;
              mouseY: number;
          }
        | undefined
    >();

    const setAdjustedContextMenuPoint = (x: number, y: number) => {
        setContextMousePoint({
            mouseX: x - 2,
            mouseY: y - 4
        });
    };

    const handleClick = (event: React.MouseEvent<HTMLDivElement>) => {
        if (props.isEditableCollection) {
            setAdjustedContextMenuPoint(event.clientX - 2, event.clientY - 4);
            event.preventDefault();
            event.stopPropagation();
        }
    };

    const handleClose = () => {
        setContextMousePoint(undefined);
    };

    const collectionMenuItemsSpecs: MenuItemSpec[] = [
        {
            label: "Open or Create Another Collection",
            l10nId: "CollectionTab.OpenCreateCollectionMenuItem",
            command: "workspace/openOrCreateCollection"
        },
        {
            label: "Make Reader Template Bloom Pack...",
            l10nId:
                "CollectionTab.AddMakeReaderTemplateBloomPackToolStripMenuItem",
            command: "collections/makeBloompack"
        },
        {
            label: "Advanced",
            l10nId: "CollectionTab.AdvancedToolStripMenuItem",
            shouldShow: () => true, // show for all collections (except factory)
            submenu: [
                {
                    label: "Do Checks of All Books",
                    l10nId: "CollectionTab.CollectionMenu.doChecksOfAllBooks",
                    command: "collections/doChecksOfAllBooks"
                },
                {
                    label: "Rescue Missing Images...",
                    l10nId: "CollectionTab.CollectionMenu.rescueMissingImages",
                    command: "collections/rescueMissingImages"
                },
                {
                    label: "Do Updates of All Books",
                    l10nId:
                        "CollectionTab.CollectionMenu.doChecksAndUpdatesOfAllBooks",
                    command: "collections/doUpdatesOfAllBooks"
                }
            ]
        }
    ];

    //const bookMenuItems = makeMenuItems(bookMenuItemsSpecs);
    const collectionMenuItems = makeMenuItems(
        collectionMenuItemsSpecs,
        props.isEditableCollection,
        props.manager.getSelectedBookInfo()!.saveable,
        handleClose,
        // the collection menu commands don't actually use the ID of
        // a particular book
        "",
        props.collectionId
    );

    // This is an approximation. 5 buttons per line is about what we get in a default
    // layout on a fairly typical screen. We'd get a better approximation if we used
    // the width of a button and knew the width of the container. But I think this is good
    // enough. Worst case, we expand a bit more than we need.
    const collectionHeight = bookButtonHeight * Math.ceil(books.length / 5);

    const content = (
        <div
            key={"BookCollection-" + props.collectionId}
            className="bookButtonPane"
            onContextMenu={e => handleClick(e)}
            style={{ cursor: "context-menu" }}
        >
            {books.length > 0 && (
                <Grid
                    container={true}
                    spacing={3}
                    direction="row"
                    justify="flex-start"
                    alignItems="flex-start"
                >
                    {books?.map(book => {
                        return (
                            <Grid item={true} className="book-wrapper">
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
                                        <div
                                            className="placeholder"
                                            style={{
                                                height:
                                                    bookButtonHeight.toString(
                                                        10
                                                    ) + "px",
                                                width:
                                                    bookButtonWidth.toString(
                                                        10
                                                    ) + "px"
                                            }}
                                        ></div>
                                    }
                                >
                                    <BookButton
                                        key={book.id}
                                        book={book}
                                        collection={collection}
                                        manager={props.manager}
                                    />
                                </LazyLoad>
                            </Grid>
                        );
                    })}
                </Grid>
            )}
            {contextMousePoint && (
                <Menu
                    keepMounted={true}
                    open={contextMousePoint !== undefined}
                    onClose={handleClose}
                    anchorReference="anchorPosition"
                    anchorPosition={{
                        top: contextMousePoint!.mouseY,
                        left: contextMousePoint!.mouseX
                    }}
                >
                    {collectionMenuItems}
                </Menu>
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

export interface MenuItemSpec {
    label: string;
    l10nId?: string;
    // One of these two must be provided. If both are, onClick is used and command is ignored.
    // If only command is provided, the click action is to call handleBookCommand with that argument,
    // which invokes the corresponding API call to C# code.
    command?: string;
    onClick?: React.MouseEventHandler<HTMLElement>;
    // If this is defined (rare), it determines whether the menu item should be shown
    // (except in factory collections, where we never show any).
    // If it's not defined, a menu item is shown if we're in the editable collection and
    // other requirements are satisfied, and not otherwise.
    shouldShow?: () => boolean;
    // Involves making changes to the book; therefore, can only be done in the one editable collection
    // (unless shouldInclude returns true), and if we're in a Team Collection, the book must be checked out.
    requiresSavePermission?: boolean;
    submenu?: MenuItemSpec[];
}

// This function and the associated MenuItem classes want to become a general component for making
// pop-up menus. But at the moment a lot of the logic is specific to making menus about books and
// book collections. I'm not seeing a good way to factor that out. Maybe it will become clear when
// we have a third need for such a menu. For now it is just logic shared with BookButton.
export const makeMenuItems = (
    menuItemsSpecs: MenuItemSpec[],
    isEditableCollection: boolean,
    isBookSavable: boolean,
    close: () => void,
    bookId: string,
    collectionId: string
) => {
    const menuItemsT = menuItemsSpecs
        .map((spec: MenuItemSpec) => {
            if (spec.label === "-") {
                return <Divider />;
            }
            if (spec.submenu) {
                var submenuItems = makeMenuItems(
                    spec.submenu,
                    isEditableCollection,
                    isBookSavable,
                    close,
                    bookId,
                    collectionId
                );
                return (
                    <LocalizableNestedMenuItem
                        english={spec.label}
                        l10nId={spec.l10nId!}
                    >
                        {submenuItems}
                    </LocalizableNestedMenuItem>
                );
            }
            if (spec.shouldShow) {
                if (!spec.shouldShow()) {
                    return undefined;
                }
            } else {
                // default logic for whether to show the command
                if (isEditableCollection) {
                    // eliminate commands that require permission to change the book, if we don't have it
                    if (spec.requiresSavePermission && !isBookSavable) {
                        return undefined;
                    }
                } else {
                    // outside that collection, commands can only be shown if they have a shouldShow function.
                    return undefined;
                }
            }

            // It should be possible to use spec.onClick || () => handleBookCommand(spec.command!) inline,
            // but I can't make Typescript accept it.
            let clickAction: React.MouseEventHandler = () => {
                close();
                BloomApi.postString(
                    `${spec.command!}?collection-id=${encodeURIComponent(
                        collectionId
                    )}`,
                    bookId
                );
            };
            if (spec.onClick) {
                clickAction = spec.onClick;
            }
            return (
                <LocalizableMenuItem
                    english={spec.label}
                    l10nId={spec.l10nId!}
                    onClick={clickAction}
                ></LocalizableMenuItem>
            );
        })
        .filter(x => x); // that is, remove ones where the map function returned undefined

    // Can't find a really good way to tell that an element is a Divider.
    // But we only have Dividers and LocalizableMenuItems in this list,
    // so it's a Dividier if it doesn't have one of the required props of LocalizableMenuItem.
    const isDivider = (element: JSX.Element): boolean => {
        return !element.props.english;
    };
    // filter out dividers if (a) followed by another divider, or (b) at the start or end of the list
    return menuItemsT.filter(
        (elt, index) =>
            !isDivider(elt!) ||
            (index > 0 &&
                index < menuItemsT.length - 1 &&
                !isDivider(menuItemsT[index + 1]!))
    );
};

const LocalizableMenuItem: React.FunctionComponent<{
    english: string;
    l10nId: string;
    onClick: React.MouseEventHandler<HTMLElement>;
}> = props => {
    const label = useL10n(props.english, props.l10nId);
    return (
        <MenuItem key={props.l10nId} onClick={props.onClick}>
            {label}
        </MenuItem>
    );
};

const LocalizableNestedMenuItem: React.FunctionComponent<{
    english: string;
    l10nId: string;
}> = props => {
    const label = useL10n(props.english, props.l10nId);
    return (
        // Can't find any doc on parentMenuOpen. Examples set it to the same value
        // as the open prop of the parent menu. But it seems to work fine just set
        // to true. (If omitted, however, the child menu does not appear when the
        // parent is hovered over.)
        <NestedMenuItem key={props.l10nId} label={label} parentMenuOpen={true}>
            {props.children}
        </NestedMenuItem>
    );
};
