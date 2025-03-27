/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { get, post, postString } from "../utils/bloomApi";
import { BooksOfCollection, IBookInfo } from "./BooksOfCollection";
import { Transition } from "react-transition-group";
import { SplitPane } from "react-collapse-pane";
import { kPanelBackground, kDarkestBackground } from "../bloomMaterialUITheme";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import { CollectionsTabBookPane } from "./collectionsTabBookPane/CollectionsTabBookPane";
import { useEffect, useMemo, useState } from "react";
import useEventListener from "@use-it/event-listener";
import { BookSelectionManager } from "./bookSelectionManager";
import ShowAfterDelay from "../react_components/showAfterDelay";
import { forceCheck as convertAnyVisibleLazyLoads } from "react-lazyload";
import { IconButton, Divider, Menu } from "@mui/material";
import GreyTriangleMenuIcon from "../react_components/icons/GreyTriangleMenuIcon";
import {
    LocalizableCheckboxMenuItem,
    LocalizableMenuItem,
    LocalizableNestedMenuItem
} from "../react_components/localizableMenuItem";
import { TeamCollectionDialogLauncher } from "../teamCollection/TeamCollectionDialog";
import { SpreadsheetExportDialogLauncher } from "./spreadsheet/SpreadsheetExportDialog";
import { H1 } from "../react_components/l10nComponents";
import { useL10n } from "../react_components/l10nHooks";
import { useSubscribeToWebSocketForEvent } from "../utils/WebSocketManager";
import { EmbeddedProgressDialog } from "../react_components/Progress/ProgressDialog";
import { useSubscribeToWebSocketForObject } from "../utils/WebSocketManager";
import CloseIcon from "@mui/icons-material/Close";
import FolderOpenOutlinedIcon from "@mui/icons-material/FolderOpenOutlined";
import { kBloomBlue } from "../bloomMaterialUITheme";
import { BloomTooltip } from "../react_components/BloomToolTip";
import { Link } from "../react_components/link";
import { ForumInvitationDialogLauncher } from "../react_components/forumInvitationDialog";
import { CollectionSettingsDialog } from "../collection/CollectionSettingsDialog";
import { BooksOnBlorgProgressBar } from "../booksOnBlorg/BooksOnBlorgProgressBar";
import { SubscriptionStatus } from "./SubscriptionStatus";

const kResizerSize = 10;

type CollectionInfo = {
    id: string;
    key?: string; // React key, defaults to id
    name: string;
    shouldLocalizeName: boolean;
    isLink: boolean;
    isRemovableFolder: boolean;
    filter?: (book: IBookInfo) => boolean;
};

export const CollectionsTabPane: React.FunctionComponent = () => {
    // This sort of duplicates useApiJson, but allows us to use the underlying state variable.
    // Which we really need.
    const [collections, setCollections] = useState<
        [CollectionInfo] | undefined
    >();
    useEffect(() => {
        get("collections/list", c => {
            setCollections(c.data);
        });
    }, []);

    // Setting the collectionCount to a new value causes a refresh.  Even though it's
    // not explicitly referenced anywhere except for being set, not having it results
    // in no refreshes when collections are removed.
    const [collectionCount, setCollectionCount] = useState<number>(
        collections?.length ?? 0
    );

    const removeSourceCollection = (id: string) => {
        postString("collections/removeSourceCollection", id).then(() => {
            finishDeletingCollection(id);
        });
    };

    const [newCollection, setNewCollection] = useState<
        CollectionInfo | undefined
    >();

    const finishAddingNewSourceCollection = (collection: CollectionInfo) => {
        if (!collections) return;
        const currentIndex = collections.findIndex(value => {
            return value.id && value.id === collection.id;
        });
        if (currentIndex >= 0) {
            // Scroll the already existing collection into view.
            const element = document.getElementById(sanitize(collection.id));
            if (element) element.scrollIntoView();
        } else {
            // I have no idea how to control the scrolling of this newly added collection.
            collections.push(collection);
            setCollectionCount(collections.length);
        }
    };

    const addSourceCollection = () => {
        post("collections/addSourceCollection");
    };

    function finishDeletingCollection(id: string) {
        if (!collections) return;
        let newIndex = -1;
        collections.filter((value, index, array) => {
            if (value.id === id) {
                array.splice(index, 1);
                newIndex = index;
                return true;
            }
            return false;
        });
        if (newIndex === collections.length) --newIndex;
        if (newIndex >= 0) {
            const element = document.getElementById(
                sanitize(collections[newIndex].id)
            );
            if (element) element.scrollIntoView();
        }
        setCollectionCount(collections.length);
    }

    // Since a user will take as much time as they want to deal with the dialog,
    // we can't just wait for the api call to return. Instead we get called back
    // via web socket iff they select a folder and close the dialog.
    useSubscribeToWebSocketForObject<{
        success: boolean;
        collection: CollectionInfo;
    }>("collections", "addSourceCollection-results", results => {
        if (results.success) {
            setNewCollection(results.collection);
        }
    });

    const [deletedCollection, setDeletedCollection] = useState<
        CollectionInfo | undefined
    >();

    const removeSourceFolder = (id: string) => {
        // This opens a file explorer on the given folder, giving the user
        // the option of deleting it.  We can't depend on waiting long enough
        // so we just ignore the return from the post and listen on a socket
        // for any update information.
        postString("collections/removeSourceFolder", id);
    };

    useSubscribeToWebSocketForObject<{
        success: boolean;
        list: [any];
    }>("collections", "updateCollectionList", results => {
        setCollections(results.list);
    });

    const [draggingSplitter, setDraggingSplitter] = useState(false);

    // Initially (when Bloom first starts, until we persist splitter settings) the vertical
    // splitter between the editable collection and the others is set to give them equal space.
    // When the user drags the splitter, we use a callback to update this, so it will be right
    // if we need to use it again.
    // It is passed to the vertical splitter as the "initialSizes", which is ignored except
    // for the very first render of a particular SplitPane. So to get it to take effect later,
    // we have to modify the key of the SplitPane, forcing React to create a whole new one.
    // The only time we currently need to do this is when Bloom is restored from being
    // minimized, which somehow puts the vertical splitter into a weird state where apparently
    // both panes are collapsed and there is nothing to see. As far as I can tell, all other
    // changes to window size are handled nicely by the SplitPane.
    const [splitHeights, setSplitHeights] = useState([1, 1]);
    const [generation, setGeneration] = useState(0);
    useSubscribeToWebSocketForEvent("window", "restored", () => {
        setGeneration(old => old + 1);
    });

    const manager: BookSelectionManager = useMemo(() => {
        const manager = new BookSelectionManager();
        manager.initialize();
        return manager;
    }, []);

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

    // There's no event built into the splitter that will tell us when drag is done.
    // So to tell that it's over, we have a global listener for mouseup.
    // useEventListener handles cleaning up the listener when this component is disposed.
    // The small delay somehow prevents a problem where the drag would continue after
    // mouse up. My hypothesis is that causing a re-render during mouse-up handling
    // interferes with the implementation of the splitter and causes it to miss
    // mouse up events, perhaps especially if outside the splitter control itself.
    useEventListener("mouseup", () => {
        if (draggingSplitter) {
            // LazyLoad isn't smart enough to notice that the size of the parent scrolling box changed.
            // We help out by telling it to check for anything that might have become visible
            // because of the drag.
            convertAnyVisibleLazyLoads();
        }
        setTimeout(() => setDraggingSplitter(false), 0);
    });

    useEffect(() => {
        window.addEventListener("keyup", ev => {
            if (ev.code == "F2") {
                // We want to make the selected button show the renaming state.
                manager.setRenaming();
            }
        });
    }, []);
    const haveNoSources = collections && collections.length === 1;
    useEffect(() => {
        // If we've got the list of collections and there are no source collections,
        // we want to set the splitter to hide the bottom pane.
        if (haveNoSources) {
            setSplitHeights([1, 0]);
            setGeneration(old => old + 1);
        }
    }, [haveNoSources]);

    if (!collections) {
        return <div />;
    }

    const handleCollectionMenuClick = (
        event: React.MouseEvent<HTMLButtonElement>
    ) => {
        // If there's already a Material-UI menu open somewhere (probably one of the book buttons),
        // we don't want to open our own menu, and it's tricky to make it open the menu appropriate
        // to the place clicked, which might not even be what the user wanted. Instead, just
        // simulate a regular click on the backdrop, which will close the menu. If necessary the
        // user can then right-click again.
        // This is a known bug in Material-UI, fixed in version 5, so we may be able to do better
        // when we switch to that. See https://github.com/mui/material-ui/issues/19145.
        const menuBackdrop = (event.target as HTMLElement).closest(
            ".MuiPopover-root"
        );
        if (menuBackdrop) {
            (event.target as HTMLElement).click();
            return;
        }

        setAdjustedContextMenuPoint(event.clientX, event.clientY);
        event.preventDefault();
        event.stopPropagation();
    };

    const handleClose = () => {
        setContextMousePoint(undefined);
    };

    const collectionMenuItemsSpecs: MenuItemSpec[] = [
        {
            label: "Open or Create Another Collection",
            l10nId: "CollectionTab.OpenCreateCollectionMenuItem",
            command: "workspace/openOrCreateCollection",
            addEllipsis: true
        },
        {
            label: "Make Bloom Pack of Shell Books",
            l10nId: "CollectionTab.MakeBloomPackOfShellBooks",
            command: "collections/makeShellBooksBloompack",
            addEllipsis: true
        },
        {
            label: "Make Reader Template Bloom Pack...",
            l10nId:
                "CollectionTab.AddMakeReaderTemplateBloomPackToolStripMenuItem",
            command: "collections/makeBloompack"
        },
        {
            label: "Troubleshooting",
            l10nId: "CollectionTab.ContextMenu.Troubleshooting",
            submenu: [
                {
                    label: "Do Checks of All Books",
                    l10nId: "CollectionTab.CollectionMenu.doChecksOfAllBooks",
                    command: "collections/doChecksOfAllBooks",
                    addEllipsis: true
                },
                {
                    label: "Do Updates of All Books",
                    l10nId:
                        "CollectionTab.CollectionMenu.doChecksAndUpdatesOfAllBooks",
                    command: "collections/doUpdatesOfAllBooks",
                    addEllipsis: true
                },
                {
                    label: "Rescue Missing Images...",
                    l10nId: "CollectionTab.CollectionMenu.rescueMissingImages",
                    command: "collections/rescueMissingImages"
                }
            ]
        }
    ];

    const collectionMenuItems = makeMenuItems(
        collectionMenuItemsSpecs,
        true,
        manager.getSelectedBookInfo()?.saveable ?? false,
        handleClose,
        // the collection menu commands don't actually use the ID of
        // a particular book
        "",
        collections[0].id
    );

    if (newCollection) {
        finishAddingNewSourceCollection(newCollection);
        setNewCollection(undefined);
    }
    if (deletedCollection) {
        finishDeletingCollection(deletedCollection.id);
        setDeletedCollection(undefined);
    }
    let sourcesCollections = collections.slice(1); // remove the one editable collection
    if (sourcesCollections.length > 0) {
        // when we're in the "download for editing" mode, there are no other collections
        sourcesCollections = [
            ...processTemplatesCollection(sourcesCollections[0]),
            ...sourcesCollections.slice(1)
        ];
    }
    // Enhance: may want to sort these by local name, though probably keeping Templates
    // and possibly Sample Shells at the top.
    const collectionComponents = sourcesCollections.map(c => {
        return (
            <BooksOfCollectionWithHeading
                key={c.key ?? c.id}
                name={c.name}
                id={c.id}
                shouldLocalizeName={c.shouldLocalizeName}
                isLink={c.isLink}
                isRemovableFolder={c.isRemovableFolder}
                manager={manager}
                onRemoveSourceCollection={removeSourceCollection}
                onRemoveSourceFolder={removeSourceFolder}
                filter={c.filter}
            />
        );
    });

    const collectionsHeaderKey = "CollectionTab.BookSourceHeading";
    const collectionsHeaderText = "Sources For New Books";

    const lockedToOneDownloadedBook = sourcesCollections.length === 0;

    return (
        <div
            css={css`
                height: 100vh; // I don't understand why 100% doesn't do it (nor why 100vh over-does it)
                background-color: ${kPanelBackground};
                color: white;

                h1 {
                    font-size: 20px;
                    margin: 0;
                }
                h2 {
                    font-size: 16px;
                    margin: 0;
                }

                .handle-bar,
                .handle-bar:hover {
                    background-color: ${kDarkestBackground};
                }

                .SplitPane {
                    position: relative !important; // we may find this messes things up... but the "absolute" positioning default is ridiculous.

                    .Pane.horizontal {
                        overflow-y: auto;
                        // TODO: this should only be applied to the bottom pane. As is, it pushes the top one away from the top of the screen.
                        margin-top: ${kResizerSize}px; // have to push down to make room for the resizer! Ughh.
                    }
                    .Pane.vertical {
                    }
                }
            `}
        >
            <SplitPane
                split="vertical"
                // This gives the two panes about the same ratio as in the Legacy view.
                // Enhance: we'd like to save the user's chosen width.
                initialSizes={[31, 50]}
                resizerOptions={{
                    css: {
                        width: `${kResizerSize}px`,
                        background: `${kDarkestBackground}`
                    },
                    hoverCss: {
                        width: `${kResizerSize}px`,
                        background: `${kDarkestBackground}`
                    },
                    grabberSize: `${kResizerSize / 2}px`
                }}
                hooks={{
                    onDragStarted: () => {
                        setDraggingSplitter(true);
                    }
                }}
                // onDragFinished={() => {
                //     alert("stopped dragging");
                //     setDraggingVSplitter(false);
                // }}
            >
                <SplitPane
                    key={"gen" + generation}
                    split="horizontal"
                    // TODO: the splitter library lets us specify a height, but it doesn't apply it correctly an so the pane that follows
                    // does not get pushed down to make room for the thicker resizer
                    initialSizes={splitHeights}
                    resizerOptions={{
                        css: {
                            height: `${kResizerSize}px`,
                            background: `${kDarkestBackground}`
                        },
                        hoverCss: {
                            height: `${kResizerSize}px`,
                            background: `${kDarkestBackground}`
                        }
                    }}
                    hooks={{
                        onDragStarted: () => {
                            setDraggingSplitter(true);
                        },
                        onSaveSizes: sizes => {
                            setSplitHeights(sizes);
                        }
                    }}
                >
                    <div
                        css={css`
                            margin: 10px;
                        `}
                    >
                        <SubscriptionStatus />
                        <BooksOnBlorgProgressBar />
                        <h1>
                            {collections[0].name}
                            <IconButton
                                css={css`
                                    // Use && to get enough specificity to beat out .MuiButtonRoot. Alternatively, you can slap !important on inline-style them
                                    && {
                                        // Triangle positioning.

                                        // Match historical 15-16 px of padding between the text and left edge of triangle
                                        padding-left: 15px;

                                        // Match historical behavior that the triangle bottom is 3-4 px below the baseline of the font.
                                        // (note that in terms of html elements, the bottom of the <h1> element is below the baseline of the text)
                                        padding-top: 0px;
                                        padding-bottom: 0px;
                                        bottom: -1px; // This was experimentally determined, there's no sound theoretical justification for this particular number.
                                    }
                                `}
                                onClick={handleCollectionMenuClick}
                                size="large"
                                disableRipple
                            >
                                <GreyTriangleMenuIcon
                                    css={css`
                                        // Use && to get enough specificity to beat out MaterialUI-generated rules
                                        && {
                                            width: 10px;
                                            height: unset; // Lets it auto-proportion the height
                                        }
                                    `}
                                />
                            </IconButton>
                        </h1>
                        <BooksOfCollection
                            collectionId={collections[0].id}
                            isEditableCollection={true}
                            manager={manager}
                            lazyLoadCollection={false}
                            lockedToOneDownloadedBook={
                                lockedToOneDownloadedBook
                            }
                        />
                    </div>

                    {lockedToOneDownloadedBook || (
                        <Transition in={true} appear={true} timeout={2000}>
                            {state => (
                                <div
                                    css={css`
                                        margin: 10px;
                                    `}
                                    className={`group fade-${state}`}
                                >
                                    <H1
                                        l10nKey={collectionsHeaderKey}
                                        css={css`
                                            padding-bottom: 20px;
                                        `}
                                    >
                                        {collectionsHeaderText}
                                    </H1>

                                    <ShowAfterDelay
                                        waitBeforeShow={100} // REview: we really want to wait for an event that indicates the main collection is mostly painted
                                    >
                                        {collectionComponents}
                                    </ShowAfterDelay>
                                    <Link
                                        l10nKey="CollectionTab.AddSourceCollection"
                                        css={css`
                                            text-transform: uppercase;
                                            padding-bottom: 10px;
                                        `}
                                        onClick={() => addSourceCollection()}
                                    >
                                        Show another collection...
                                    </Link>
                                </div>
                            )}
                        </Transition>
                    )
                    // Enhance:possibly if we're NOT showing the Sources for new Books stuff,
                    // we could have a message saying why and to pick an Enterprise subscription to fix it.
                    }
                </SplitPane>
                {/* This wrapper is used to... fix up some margin/color stuff I was having trouble with from SplitPane */}
                <div
                    css={css`
                        height: 100%;
                    `}
                >
                    <ShowAfterDelay
                        waitBeforeShow={500} // Review: we really want an event that indicates the collection panes are mostly painted.
                    >
                        <CollectionsTabBookPane
                            // While we're dragging the splitter, we need to overlay the iframe book preview
                            // so it doesn't steal the mouse events we need for dragging the splitter.
                            disableEventsInIframe={draggingSplitter}
                        />
                    </ShowAfterDelay>
                </div>
            </SplitPane>
            {contextMousePoint && (
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
                    {collectionMenuItems}
                </Menu>
            )}
            <TeamCollectionDialogLauncher />
            <SpreadsheetExportDialogLauncher />
            <ForumInvitationDialogLauncher />
            <CollectionSettingsDialog />
            <EmbeddedProgressDialog id="collectionTab" />
        </div>
    );
};

export interface MenuItemSpec {
    label: string;
    l10nId?: string;
    l10nParam0?: string;
    // One of these two must be provided. If both are, onClick is used and command is ignored.
    // If only command is provided, the click action is to call handleBookCommand with that argument,
    // which invokes the corresponding API call to C# code.
    command?: string;
    onClick?: React.MouseEventHandler<HTMLElement>;
    hide?: () => boolean; // if not provided, always show
    // Involves making changes to the book; therefore, can only be done in the one editable collection
    // and if we're in a Team Collection, the book must be checked out.
    requiresSavePermission?: boolean;
    submenu?: MenuItemSpec[];
    icon?: React.ReactNode;
    // if true, menu item is rendered as an ApiCheckbox with the command as its api.
    checkbox?: boolean;
    featureName?: string;
    addEllipsis?: boolean;
}

// This function and the associated MenuItem classes want to become a general component for making
// pop-up menus. But at the moment a lot of the logic is specific to making menus about books and
// book collections. I'm not seeing a good way to factor that out. Maybe it will become clear when
// we have a third need for such a menu. For now it is just logic shared with BookButton.
// If 'includeSpreadsheetItems' is true, then the pane has determined (api call) that the Advanced
// checkbox for import/export spreadsheet is checked. When spreadsheet is no longer an experimental
// feature, we can either remove the parameter or just make it always true.
// This parameter causes menu items with "Spreadsheet" in their localization Id to be included in the
// menu, otherwise they aren't.
export const makeMenuItems = (
    menuItemsSpecs: MenuItemSpec[],
    isEditableCollection: boolean,
    isBookSavable: boolean,
    close: () => void,
    bookId: string,
    collectionId: string,
    tooltipIfCannotSaveBook?: string
) => {
    const menuItemsT = menuItemsSpecs
        .map((spec: MenuItemSpec, index: number) => {
            if (spec.label === "-") {
                return <Divider key={index} />;
            }
            if (spec.submenu) {
                const submenuItems = makeMenuItems(
                    spec.submenu,
                    isEditableCollection,
                    isBookSavable,
                    close,
                    bookId,
                    collectionId,
                    tooltipIfCannotSaveBook
                );
                return submenuItems.length ? (
                    <LocalizableNestedMenuItem
                        english={spec.label}
                        l10nId={spec.l10nId!}
                        l10nParam0={spec.l10nParam0}
                    >
                        {submenuItems}
                    </LocalizableNestedMenuItem>
                ) : (
                    undefined
                );
            }

            if (spec.hide && spec.hide()) {
                return undefined;
            }
            // If we have determined that a command should be shown, this logic determines whether it should be
            // disabled or not.
            // We disable commands that require permission to change the book, if we don't have such permission.
            const disabled =
                isEditableCollection &&
                spec.requiresSavePermission &&
                !isBookSavable;

            if (spec.checkbox) {
                return (
                    <LocalizableCheckboxMenuItem
                        key={index}
                        english={spec.label}
                        l10nId={spec.l10nId!}
                        l10nParam0={spec.l10nParam0}
                        onClick={() => {
                            // We deliberately do NOT close the menu, so the user can see it really got checked.
                        }}
                        apiEndpoint={spec.command!}
                        disabled={disabled}
                        tooltipIfDisabled={tooltipIfCannotSaveBook}
                    ></LocalizableCheckboxMenuItem>
                );
            }
            // It should be possible to use spec.onClick || () => handleBookCommand(spec.command!) inline,
            // but I can't make Typescript accept it.
            let clickAction: React.MouseEventHandler = () => {
                close();
                postString(
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
                    key={spec.l10nId}
                    english={spec.label}
                    l10nId={spec.l10nId!}
                    l10nParam0={spec.l10nParam0}
                    onClick={clickAction}
                    icon={spec.icon}
                    addEllipsis={spec.addEllipsis}
                    featureName={spec.featureName}
                    disabled={disabled}
                    tooltipIfDisabled={tooltipIfCannotSaveBook}
                ></LocalizableMenuItem>
            );
        })
        .filter(x => x); // that is, remove ones where the map function returned undefined

    // Can't find a really good way to tell that an element is a Divider.
    // But we only have Dividers and LocalizableMenuItems in this list,
    // so it's a Divider if it doesn't have one of the required props of LocalizableMenuItem.
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

const BooksOfCollectionWithHeading: React.FunctionComponent<{
    name: string;
    id: string;
    shouldLocalizeName: boolean;
    isLink: boolean;
    isRemovableFolder: boolean;
    manager: BookSelectionManager;
    onRemoveSourceCollection: (id: string) => void;
    onRemoveSourceFolder: (id: string) => void;
    filter?: (book: IBookInfo) => boolean;
}> = props => {
    // Using a null l10nId lets us not make a server call when we don't want to.
    // (We can't call useL10n conditionally.)
    const collectionName = useL10n(
        props.name,
        props.shouldLocalizeName ? `CollectionTab.${props.name}` : null
    );

    return (
        <div key={"frag:" + props.id} id={sanitize(props.id)}>
            {props.isLink ? (
                getRemovableCollectionHeaderDiv(
                    collectionName,
                    props.onRemoveSourceCollection,
                    props.id,
                    "DoNotShowCollection",
                    "CollectionTab.DoNotShowCollection",
                    "Do not show this collection here.",
                    <CloseIcon />
                )
            ) : props.isRemovableFolder ? (
                getRemovableCollectionHeaderDiv(
                    collectionName,
                    props.onRemoveSourceFolder,
                    props.id,
                    "RemoveThisGroup",
                    "CollectionTab.RemoveThisGroup",
                    "To remove this group, you will need to delete the folder. Click here to view the folder on your drive.",
                    <FolderOpenOutlinedIcon />
                )
            ) : (
                <h2>{collectionName}</h2>
            )}
            <BooksOfCollection
                collectionId={props.id}
                isEditableCollection={false}
                manager={props.manager}
                lazyLoadCollection={true}
                lockedToOneDownloadedBook={false}
                filter={props.filter}
            />
        </div>
    );
};

function getRemovableCollectionHeaderDiv(
    collectionName: string,
    clickFunction: (id: string) => void,
    collectionId: string,
    tooltipId: string,
    tooltipL10nKey: string,
    tooltipText: string,
    icon: JSX.Element
): JSX.Element {
    // Make the book list semi-transparent when hovering over the
    // "remove collection" button.  This function is called with true
    // for onMouseEnter and false for onMouseLeave.
    function setBooklistTransparency(
        ev: React.MouseEvent<HTMLDivElement, MouseEvent>,
        makeTransparent: boolean
    ): void {
        let div: HTMLElement | null = ev.target as HTMLElement;
        if (div) {
            while (
                (div = div.parentElement) &&
                !div.classList.contains("removable-collection-header")
            );
            const dest = div?.nextElementSibling;
            if (dest) {
                if (makeTransparent) dest.classList.add("largely-transparent");
                else dest.classList.remove("largely-transparent");
            }
        }
    }

    return (
        <div
            className="removable-collection-header"
            css={css`
                display: flex;
                flex-flow: row;
                &:hover div {
                    display: block;
                    cursor: pointer;
                }
            `}
        >
            <h2>{collectionName}</h2>
            <div
                css={css`
                    margin-left: 30px;
                    margin-bottom: -10px; // prevent wiggle when appearing/disappearing
                    display: none;
                    color: ${kBloomBlue};
                    background-color: transparent;
                `}
                onClick={() => clickFunction(collectionId)}
                onMouseEnter={ev => setBooklistTransparency(ev, true)}
                onMouseLeave={ev => setBooklistTransparency(ev, false)}
            >
                <BloomTooltip
                    id={tooltipId}
                    placement="right"
                    tip={{ english: tooltipText, l10nKey: tooltipL10nKey }}
                >
                    {icon}
                </BloomTooltip>
            </div>
        </div>
    );
}

function sanitize(id: string): string {
    return encodeURIComponent(id).replace(/./g, "_");
}

// Divide the one "templates/" collection into separate collections and sort them
// TODO: sort them according to some criteria TBD
function processTemplatesCollection(
    templatesCollection: CollectionInfo
): CollectionInfo[] {
    const simpleTemplates = {
        ...templatesCollection,
        key: templatesCollection.id + "/Simple"
    };

    // this "f" garbage is because TS refused to see that simpleTemplates.filter is never undefined
    const f = (book: IBookInfo) => {
        return (
            book.folderName.startsWith("Basic Book") ||
            book.folderName.startsWith("eBook")
        );
    };
    simpleTemplates.filter = f;
    const specializedTemplates = {
        ...templatesCollection,
        key: templatesCollection.id + "/Specialized",
        name: "Specialized Templates"
    };
    specializedTemplates.filter = (book: IBookInfo) => {
        return !f(book);
    };

    return [simpleTemplates, specializedTemplates];
}
WireUpForWinforms(CollectionsTabPane);
