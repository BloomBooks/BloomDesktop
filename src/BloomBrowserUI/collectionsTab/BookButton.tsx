/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import Grid from "@material-ui/core/Grid";
import * as React from "react";
import { BloomApi } from "../utils/bloomApi";
import { Button, Menu } from "@material-ui/core";
import TruncateMarkup from "react-truncate-markup";
import {
    IBookTeamCollectionStatus,
    useTColBookStatus
} from "../teamCollection/teamCollectionApi";
import { BloomAvatar } from "../react_components/bloomAvatar";
import {
    kBloomBlue,
    kBloomGold,
    kBloomLightBlue,
    kBloomPurple
} from "../bloomMaterialUITheme.ts";
import { useRef, useState, useEffect } from "react";
import { useSubscribeToWebSocketForEvent } from "../utils/WebSocketManager";
import { BookSelectionManager, useIsSelected } from "./bookSelectionManager";
import {
    IBookInfo,
    ICollection,
    makeMenuItems,
    MenuItemSpec
} from "./BooksOfCollection";
import DeleteIcon from "@material-ui/icons/Delete";

export const bookButtonHeight = 120;
export const bookButtonWidth = 90;

export const BookButton: React.FunctionComponent<{
    book: IBookInfo;
    collection: ICollection;
    //selected: boolean;
    manager: BookSelectionManager;
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
    const bookLabel = BloomApi.useWatchString(
        props.book.title,
        // These must correspond to what BookCommandsApi.UpdateButtonTitle sends
        "book",
        "label-" + props.collection.id + "-" + props.book.id
    );
    const collectionQuery = `collection-id=${encodeURIComponent(
        props.collection.id
    )}`;
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
            BloomApi.post(
                `bookCommand/enhanceLabel?${collectionQuery}&id=${encodeURIComponent(
                    props.book.id
                )}`
            );
        }
        // logically it should also be done if props.collection.id or props.book.id changes, but they don't.
    }, [props.book.folderName, props.book.title]);

    // Don't use useApiStringState to get this function because it does an unnecessary server query
    // to get the value, which we are not using, and this hurts performance.
    const setSelectedBookIdWithApi = value =>
        BloomApi.postString(
            `collections/selected-book-id?${collectionQuery}`,
            value
        );

    const renameDiv = useRef<HTMLElement | null>();

    const teamCollectionStatus = useTColBookStatus(
        props.book.folderName,
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

    const getBookMenuItemsSpecs: () => MenuItemSpec[] = () => {
        return [
            {
                label: "Duplicate Book",
                l10nId: "CollectionTab.BookMenu.DuplicateBook",
                command: "collections/duplicateBook"
            },
            {
                label: "Make Bloom Pack",
                l10nId: "CollectionTab.MakeBloomPackButton",
                command: "bookCommand/makeBloompack"
            },
            {
                label: "Open Folder on Disk",
                l10nId: "CollectionTab.ContextMenu.OpenFolderOnDisk",
                command: "bookCommand/openFolderOnDisk",
                shouldShow: () => true // show for all collections (except factory)
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
                command: "bookCommand/exportToSpreadsheet"
            },
            {
                label: "Import content from Spreadsheet...",
                l10nId: "CollectionTab.BookMenu.ImportContentFromSpreadsheet",
                command: "bookCommand/importSpreadsheetContent",
                requiresSavePermission: true
            },
            {
                label: "Save as single file (.bloomSource)...",
                l10nId: "CollectionTab.BookMenu.SaveAsBloomToolStripMenuItem",
                command: "bookCommand/saveAsDotBloomSource"
            },
            {
                label: "Leveled Reader",
                l10nId: "TemplateBooks.BookName.Leveled Reader", // not the most appropriate ID, but we have it already
                command: "bookCommand/leveled",
                requiresSavePermission: true,
                checkbox: true
            },
            { label: "-" },
            {
                label: "Decodable Reader",
                l10nId: "TemplateBooks.BookName.Decodable Reader", // not the most appropriate ID, but we have it already
                command: "bookCommand/decodable",
                requiresSavePermission: true,
                checkbox: true
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
            },
            {
                label: "Rename",
                l10nId: "CollectionTab.BookMenu.Rename",
                onClick: () => handleRename(),
                requiresSavePermission: true
            },
            { label: "-" },
            {
                label: "Delete Book",
                l10nId: "CollectionTab.BookMenu.DeleteBook",
                command: "collections/deleteBook",
                icon: <DeleteIcon></DeleteIcon>,
                requiresSavePermission: true, // for consistency, but not used since shouldShow is defined
                // Allowed for the downloaded books collection and the editable collection (provided the book is checked out, if applicable)
                shouldShow: () =>
                    props.collection.containsDownloadedBooks ||
                    (props.collection.isEditableCollection &&
                        (props.manager.getSelectedBookInfo()?.saveable ??
                            false))
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

    const label =
        bookLabel.length > 20 ? (
            <TruncateMarkup lines={2}>
                <span>{bookLabel}</span>
            </TruncateMarkup>
        ) : (
            bookLabel
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
        BloomApi.postString(
            `collections/selectAndEditBook?${collectionQuery}`,
            props.book.id
        );
    };

    const handleContextClick = (event: React.MouseEvent<HTMLElement>) => {
        setAdjustedContextMenuPoint(event.clientX - 2, event.clientY - 4);

        handleClick(event);
    };

    const finishRename = (name: string | undefined) => {
        setRenaming(false);
        if (name !== undefined) {
            BloomApi.postString(
                `bookCommand/rename?${collectionQuery}&name=${name}`,
                props.manager.getSelectedBookInfo()!.id!
            );
        }
    };

    return (
        <div
            // This class and data-book-id attribute help the BooksOfCollection class figure out
            // what book (if any) is being right-clicked.

            className="book-button"
            // relative so the absolutely positioned rename div will be relative to this.
            css={css`
                position: relative;
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

            {// contextMenuPoint has a value if this button has been right-clicked.
            // if it wasn't the selected button at the time, however, the menu will not show
            // until we re-render after making it selected.
            // Note that we avoid doing all the work to render the menu except when it is
            // visible. Since there may be a large number of buttons this could be a significant
            // saving.
            contextMousePoint && selected && (
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
                    {makeMenuItems(
                        getBookMenuItemsSpecs(),
                        props.collection.isEditableCollection,
                        props.manager.getSelectedBookInfo()!.saveable,
                        handleClose,
                        props.book.id,
                        props.collection.id
                    )}
                </Menu>
            )}
            {// The down-arrow button, which is equivalent to right-clicking on the button.
            // I tried putting this div inside the button but then...in FF 60 but not 68 or later...
            // the button gets the click even if its inside this div.
            selected && (
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
                        border: 1px solid ${kBloomLightBlue};
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
