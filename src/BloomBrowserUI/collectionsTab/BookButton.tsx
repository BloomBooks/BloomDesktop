/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { post, postString, useWatchString } from "../utils/bloomApi";
import { Button, Menu } from "@mui/material";
import TruncateMarkup from "react-truncate-markup";
import { useTColBookStatus } from "../teamCollection/teamCollectionApi";
import { BloomAvatar } from "../react_components/bloomAvatar";
import { kBloomBlue, kBloomGold, kBloomPurple } from "../bloomMaterialUITheme";
import { useRef, useState, useEffect } from "react";
import { useSubscribeToWebSocketForEvent } from "../utils/WebSocketManager";
import { BookSelectionManager, useIsSelected } from "./bookSelectionManager";
import { IBookInfo, ICollection } from "./BooksOfCollection";
import { makeMenuItems, MenuItemSpec } from "./CollectionsTabPane";
import DeleteIcon from "@mui/icons-material/Delete";
import { useL10n } from "../react_components/l10nHooks";
import { showBookSettingsDialog } from "../bookEdit/bookSettings/BookSettingsDialog";

export const bookButtonHeight = 120;
export const bookButtonWidth = 90;

export const BookButton: React.FunctionComponent<{
    book: IBookInfo;
    collection: ICollection;
    //selected: boolean;
    manager: BookSelectionManager;
    isSpreadsheetFeatureActive: boolean;
}> = props => {
    // TODO: the c# had Font = bookInfo.IsEditable ? _editableBookFont : _collectionBookFont,

    const [renaming, setRenaming] = useState(false);
    const [contextMousePoint, setContextMousePoint] = React.useState<
        | {
              mouseX: number;
              mouseY: number;
          }
        | undefined
    >();
    const selected = useIsSelected(props.manager, props.book.id);
    const bookLabel = useWatchString(
        props.book.title,
        // These must correspond to what BookCommandsApi.UpdateButtonTitle sends
        "book",
        "label-" + props.collection.id + "-" + props.book.id
    );
    const collectionQuery = `collection-id=${encodeURIComponent(
        props.collection.id
    )}`;
    const folderName = props.book.folderPath.substring(
        props.book.folderPath.lastIndexOf("/") + 1
    );
    useEffect(() => {
        // By requesting this here like this rather than, say, as a side effect of
        // loading the collection, we achieve several things:
        // - there's no danger of the enhanced label message arriving before the button is watching
        // - we don't waste effort computing enhanced labels for buttons we're not yet rendering because of laziness
        // - we can automatically request a new enhanced label if the book's title or folder name changes
        // - typically we won't request a new enhanced label for unchanged books when re-rendering the collection
        // We don't want to try to enhance labels of factory books because their original names
        // are good (already localized) and enhancing them somehow comes up with 'Title Missing'.
        if (!props.book.isFactory) {
            post(
                `bookCommand/enhanceLabel?${collectionQuery}&id=${encodeURIComponent(
                    props.book.id
                )}`
            );
        }
        // logically it should also be done if props.collection.id or props.book.id changes, but they don't.
    }, [folderName, props.book.title]);

    // This is a bit of a hack. For almost all purposes, 'renaming' is a private state of the button,
    // causing the button to render differently when it is true, and controlled by events inside the
    // button. The exception is that, way out at the CollectionsTabPane level, we have a listener watching
    // for F2 to be pressed anywhere, which wants to force the selected-book button into the renaming
    // state.
    // The theoretical way to handle such a thing in React is to force the relevant state up to the
    // highest level component that cares. That's pretty ugly. CollectionsTabPane would be managing an
    // array of states for each button for each collection...involving knowledge it shouldn't need
    // about which collections and buttons we are being lazy about creating. Or else there's just a single
    // renaming state for the whole collection...and they all have to be re-rendered when it changes,
    // as well as pushing the state changes through at least one intermediate level.
    // However, we already have a singleton object shared by the CollectionsTabPane and all its book buttons,
    // so it's easy to notify that object when we need to make the selected button do something, and
    // its easy to have the selected button register itself with that object as the one to receive such
    // messages. So that's what I decided to do.
    useEffect(() => {
        if (selected) {
            props.manager.setRenameCallback(() => {
                if (props.manager.getSelectedBookInfo()?.saveable) {
                    setRenaming(true);
                }
            });
            // Seems it would be good to clean up like this, but if we do,
            // F2 doesn't work after clicking a second button. Hypothesis: somehow the button
            // losing selected status cleanup happens after the button gaining it sets itself up.
            // Anyway, it works better without this, and it doesn't seem to cause any problem
            // when pressing F2 after deleting a book.
            //return () => props.manager.setRenameCallback(undefined);
        }
    }, [selected]);

    // Don't use useApiStringState to get this function because it does an unnecessary server query
    // to get the value, which we are not using, and this hurts performance.
    const setSelectedBookIdWithApi = value =>
        postString(`collections/selected-book-id?${collectionQuery}`, value);

    const renameDiv = useRef<HTMLElement | null>();

    const teamCollectionStatus = useTColBookStatus(
        folderName,
        props.collection.isEditableCollection
    );

    const [reload, setReload] = useState(0);
    // Force a reload when our book's thumbnail image changed
    useSubscribeToWebSocketForEvent("bookImage", "reload", args => {
        if (args.message === props.book.id) {
            setReload(old => old + 1);
        }
    });

    const handleClose = () => {
        setContextMousePoint(undefined);
    };

    const handleRename = () => {
        handleClose();
        setRenaming(true);
    };

    const bookSubMenuItemsSpecs: MenuItemSpec[] = [
        {
            label: "Leveled Reader",
            l10nId: "TemplateBooks.BookName.Leveled Reader", // not the most appropriate ID, but we have it already
            command: "bookCommand/leveled",
            requiresSavePermission: true,
            checkbox: true
        },
        {
            label: "Decodable Reader",
            l10nId: "TemplateBooks.BookName.Decodable Reader", // not the most appropriate ID, but we have it already
            command: "bookCommand/decodable",
            requiresSavePermission: true,
            checkbox: true
        },
        { label: "-" },
        {
            label: "Export to Word or LibreOffice...",
            l10nId: "CollectionTab.BookMenu.ExportToWordOrLibreOffice",
            command: "bookCommand/exportToWord"
        },
        {
            label: "Export to Spreadsheet...",
            l10nId: "CollectionTab.BookMenu.ExportToSpreadsheet",
            command: "bookCommand/exportToSpreadsheet",
            requiresEnterprise: true
        },
        {
            label: "Import Content from Spreadsheet...",
            l10nId: "CollectionTab.BookMenu.ImportContentFromSpreadsheet",
            command: "bookCommand/importSpreadsheetContent",
            requiresSavePermission: true,
            requiresEnterprise: true
        },
        { label: "-" },
        {
            label: "Save as Single File (*.bloomSource)...",
            l10nId: "CollectionTab.BookMenu.SaveAsBloomToolStripMenuItem",
            command: "bookCommand/saveAsDotBloomSource"
        },
        {
            label: "Save as Bloom Pack (*.BloomPack)",
            l10nId: "CollectionTab.BookMenu.SaveAsBloomPackContextMenuItem",
            command: "bookCommand/makeBloompack",
            addEllipsis: true
        },
        { label: "-" },
        {
            label: "Update Thumbnail",
            l10nId: "CollectionTab.BookMenu.UpdateThumbnail",
            command: "bookCommand/updateThumbnail",
            requiresSavePermission: true // marginal, but it does change the content of the book folder
        },
        {
            label: "Update Book",
            l10nId: "CollectionTab.BookMenu.UpdateFrontMatterToolStrip",
            command: "bookCommand/updateBook",
            requiresSavePermission: true // marginal, but it does change the content of the book folder
        }
    ];

    const getBookMenuItemsSpecs: () => MenuItemSpec[] = () => {
        return [
            {
                label: "Rename Book",
                l10nId: "CollectionTab.BookMenu.RenameBook",
                onClick: () => handleRename(),
                requiresSavePermission: true,
                addEllipsis: true
            },
            {
                label: "Duplicate Book",
                l10nId: "CollectionTab.BookMenu.DuplicateBook",
                command: "collections/duplicateBook"
            },
            {
                label: "Show in File Explorer",
                l10nId: "CollectionTab.BookMenu.ShowInFileExplorer",
                command: "bookCommand/openFolderOnDisk",
                shouldShow: () => !props.collection.isFactoryInstalled // show for all collections (except factory)
            },
            // {
            //     label: "Book Settings",
            //     l10nId: "Common.BookSettings",
            //     icon: <SettingsIcon></SettingsIcon>,
            //     addEllipsis: true,
            //     requiresSavePermission: true,
            //     onClick: () => {
            //         handleClose(); // not clear why this is needed on this one, we assume it's because we're doing an onClick
            //         showBookSettingsDialog();
            //     }
            // },
            {
                label: "Delete Book",
                l10nId: "CollectionTab.BookMenu.DeleteBook",
                command: "collections/deleteBook",
                icon: <DeleteIcon></DeleteIcon>,
                requiresSavePermission: true, // for consistency, but not used since shouldShow is defined
                addEllipsis: true,
                // Allowed for the downloaded books collection and the editable collection (provided the book is checked out, if applicable)
                shouldShow: () =>
                    props.collection.containsDownloadedBooks ||
                    (props.collection.isEditableCollection &&
                        (props.manager.getSelectedBookInfo()?.saveable ??
                            false))
            },
            {
                label: "Make a book using this source",
                l10nId: "CollectionTab.MakeBookUsingThisTemplate",
                command: "app/makeFromSelectedBook",
                // Allowed for the downloaded books collection and the editable collection (provided the book is checked out, if applicable)
                shouldShow: () =>
                    props.collection.isEditableCollection &&
                    (props.manager.getSelectedBookInfo()?.isTemplate ?? false)
            },
            { label: "-" },
            {
                label: "More",
                l10nId: "CollectionTab.ContextMenu.More",
                submenu: bookSubMenuItemsSpecs
            }
        ];
    };

    useEffect(() => {
        if (renameDiv.current) {
            // we just turned it on. Select everything.
            const p = renameDiv.current;
            const s = window.getSelection();
            const r = document.createRange();
            r.selectNodeContents(p);
            s!.removeAllRanges();
            s!.addRange(r);
            //
            window.setTimeout(() => {
                // I tried the obvious approach of putting an onBlur in the JSX for the renameDiv,
                // but it gets activated immediately, and I cannot figure out why.
                renameDiv.current?.addEventListener("blur", () => {
                    finishRename(renameDiv.current!.innerText);
                });
                renameDiv.current?.addEventListener("keypress", e => {
                    if (e.key === "Enter") {
                        finishRename(renameDiv.current!.innerText);
                    } else if (e.key === "Escape") {
                        finishRename(undefined);
                    }
                });
                p.focus();
            }, 10);
        }
    }, [renaming, renameDiv.current]);

    // If the label is less than 14 characters, assume it will fit on two lines; this saves some
    // rendering cycles in TruncateMarkup. If it's longer, TruncateMarkup will carefully
    // measure what will fit on two lines and truncate nicely if necessary.
    const label =
        bookLabel.length > 14 ? (
            <TruncateMarkup lines={2}>
                <span className="bookButton-label">{bookLabel}</span>
            </TruncateMarkup>
        ) : (
            <span className="bookButton-label">{bookLabel}</span>
        );

    const renameHeight = 40;
    const downSize = 14; // size of down-arrow icon

    // Given the actual point the user clicked, set our state variable to a slightly adjusted point
    // where we want the popup menu to appear.
    const setAdjustedContextMenuPoint = (x: number, y: number) => {
        setContextMousePoint({
            mouseX: x - 2,
            mouseY: y - 4
        });
    };

    const handleClick = (event: React.MouseEvent<HTMLElement>) => {
        if (props.book.id !== props.manager.getSelectedBookInfo()?.id) {
            // Not only is it useless to select the book that is already selected,
            // it might have side effects. This might have been a contributing factor
            // to the rename box getting blurred when clicked in.
            setSelectedBookIdWithApi(props.book.id);
        }

        // There's a default right-click menu implemented by C# code which we don't want here.
        // Also BooksOfCollection implements a different context menu when a click isn't
        // intercepted here, and we don't want to get it as well.
        event.preventDefault();
        event.stopPropagation();
    };

    const handleDoubleClick = (event: React.MouseEvent<HTMLElement>) => {
        postString(
            `collections/selectAndEditBook?${collectionQuery}`,
            props.book.id
        );
    };

    const handleContextClick = (event: React.MouseEvent<HTMLElement>) => {
        setAdjustedContextMenuPoint(event.clientX, event.clientY);

        handleClick(event);
    };

    const finishRename = (name: string | undefined) => {
        setRenaming(false);
        if (name !== undefined) {
            postString(
                `bookCommand/rename?${collectionQuery}&name=${name}`,
                props.manager.getSelectedBookInfo()!.id!
            );
        }
    };

    const tooltipIfCannotSaveBook = useL10n(
        "This feature requires the book to be checked out to you.",
        "CollectionTab.BookMenu.MustCheckOutTooltip",
        "This tooltip pops up when the user hovers over a disabled menu item."
    );

    // If relevant, compute the menu items for a right-click on this button.
    // contextMenuPoint has a value if this button has been right-clicked.
    // if it wasn't the selected button at the time, however, the menu will not show
    // until we re-render after making it selected.
    // Note that we avoid doing all the work to render the menu except when it is
    // visible and clicked. Since there may be a large number of buttons this could
    // be a significant saving.
    // Note that makeMenuItems may produce no items (currently this is true for
    // factory-installed books). In this case, as well as when we don't call the
    // method at all, the menu does not show. (Showing a menu with no items results
    // in a small white square that is confusing.)
    let items: MenuItemSpec[] = [];
    if (selected) {
        items = makeMenuItems(
            getBookMenuItemsSpecs(),
            props.collection.isEditableCollection,
            props.manager.getSelectedBookInfo()!.saveable,
            handleClose,
            props.book.id,
            props.collection.id,
            props.isSpreadsheetFeatureActive,
            tooltipIfCannotSaveBook
        );
    }

    return (
        <div
            // This class and data-book-id attribute help the BooksOfCollection class figure out
            // what book (if any) is being right-clicked.

            className="book-button"
            // relative so the absolutely positioned rename div will be relative to this.
            // We tweak the padding (Material UI speifies 7px 21px) to be consistent
            // with rules that make the main content of the button 70px and the whole
            // thing 90. With more than 10px here, the numbers don't add up, and the browser
            // sometimes shrinks things too far, making labels not fit well.
            css={css`
                position: relative;
                .MuiButton-outlinedSizeLarge {
                    padding: 7px 10px;
                    border: 0px;
                }
            `}
            // This is the div that looks like the button, so it is the one that counts as
            // this book if clicked.
            data-book-id={props.book.id}
        >
            {teamCollectionStatus?.who && (
                <BloomAvatar
                    email={teamCollectionStatus.who}
                    name={teamCollectionStatus.whoFirstName}
                    avatarSizeInt={32}
                    borderColor={
                        teamCollectionStatus.who ===
                        teamCollectionStatus.currentUser
                            ? teamCollectionStatus.where ===
                              teamCollectionStatus.currentMachine
                                ? kBloomGold
                                : kBloomPurple
                            : kBloomBlue
                    }
                />
            )}
            <Button
                className={
                    "bookButton" +
                    (selected ? " selected " : "") +
                    (teamCollectionStatus?.who ? " checkedOut" : "")
                }
                css={css`
                    height: ${bookButtonHeight}px;
                    width: ${bookButtonWidth}px;
                    border: none;
                    overflow: hidden;
                    padding: 0;
                `}
                variant="outlined"
                size="large"
                title={props.book.folderPath}
                onDoubleClick={handleDoubleClick}
                onClick={e => handleClick(e)}
                onContextMenu={e => handleContextClick(e)}
                startIcon={
                    <div className={"thumbnail-wrapper"}>
                        <img
                            src={`/bloom/api/collections/book/thumbnail?book-id=${props.book.id}&${collectionQuery}&reload=${reload}`}
                        />
                    </div>
                }
            >
                {renaming || label}
            </Button>

            {contextMousePoint && items.length > 0 && (
                <Menu
                    keepMounted={true}
                    open={!!contextMousePoint}
                    onClose={handleClose}
                    anchorReference="anchorPosition"
                    anchorPosition={{
                        top: contextMousePoint!.mouseY,
                        left: contextMousePoint!.mouseX
                    }}
                >
                    {items}
                </Menu>
            )}
            {// The down-arrow button, which is equivalent to right-clicking on the button.
            // I tried putting this div inside the button but then...in FF 60 but not 68 or later...
            // the button gets the click even if its inside this div.
            items.length > 0 && (
                <div
                    css={css`
                        position: absolute;
                        box-sizing: border-box;
                        bottom: -${downSize / 2 - 4}px;
                        right: 3px;
                        height: ${downSize}px;
                        width: ${downSize}px;
                        border: solid transparent ${downSize / 2}px;
                        border-top-color: white;
                    `}
                    onClick={e => {
                        setAdjustedContextMenuPoint(e.clientX, e.clientY);
                        handleClick(e);
                    }}
                ></div>
            )}
            {// I tried putting this div inside the button as an alternate to label.
            // Somehow, this causes the blur to happen when the user clicks in the label
            // to position the IP during editing. I suspect default events are being
            // triggered by the fact that we're in a button. It was very hard to debug,
            // and stopPropagation did not help. I finally decided that letting the
            // edit box be over the button was easier and possibly safer.
            renaming && selected && (
                <div
                    // For some unknown reason, the selection background color was coming out white.
                    // I reset it to what I think is Windows standard. Need -moz- for Gecko60
                    // (until FF62).
                    // Enhance: ideally we'd either figure out why we don't get the usual default
                    // selection background (maybe because the window has a dark background?)
                    // or make it use the user's configured system highlight background (but that
                    // really might not work with white text and a dark background?)

                    // 12px of text height matches the size we're getting in the button.
                    // 18px is more line spacing than we normally use for these labels, but it's
                    // the minimum to avoid descenders in the top line being cut off by the highlight
                    // of selected text in the second line. (Of course other fonts might need
                    // a different value...but it's not a terrible problem if there is some cut off.)

                    // (I think the 6 fudge factor in the top calculation is made up of two 1px
                    // borders and the 4px padding-top.)
                    css={css`
                        width: calc(100% - 4px);
                        height: ${renameHeight}px;
                        margin-left: 1px;
                        border: 1px solid ${kBloomBlue};
                        top: ${bookButtonHeight - renameHeight - 6}px;
                        padding-top: 4px;
                        position: absolute;
                        font-size: 12px;
                        line-height: 18px;
                        text-align: center;
                        &::selection {
                            background: rgb(0, 120, 215);
                        }
                        &::-moz-selection {
                            background: rgb(0, 120, 215);
                        }
                    `}
                    contentEditable={true}
                    tabIndex={0}
                    ref={renderedElement =>
                        (renameDiv.current = renderedElement)
                    }
                    // Note: we want a blur on this element, but putting it here does not work right.
                    // See the comment where we add it in a delayed effect.
                >
                    {bookLabel}
                </div>
            )}
        </div>
    );
};

// A place holder needs to signal somehow when the corresponding book's thumbnail
// image has been updated and the real book button can be displayed.  So we define
// this placeholder component to receive the signal from the server and pass on
// the signal to the collection component.
// See https://issues.bloomlibrary.org/youtrack/issue/BL-12026.
export const BookButtonPlaceHolder: React.FunctionComponent<{
    book: IBookInfo;
    reload: (id: string) => void;
}> = props => {
    const [reload, setReload] = useState(0);
    // Force a reload when the placeholder's book thumbnail image changed
    useSubscribeToWebSocketForEvent("bookImage", "reload", args => {
        if (args.message === props.book.id) {
            setReload(reload + 1);
            props.reload(props.book.id);
        }
    });
    return (
        <div
            className="placeholder"
            style={{
                height: bookButtonHeight.toString(10) + "px",
                width: bookButtonWidth.toString(10) + "px"
            }}
        ></div>
    );
};
