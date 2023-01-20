/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import React = require("react");
import { BloomApi } from "../utils/bloomApi";
import { BooksOfCollection } from "./BooksOfCollection";
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
import { IconButton, Divider, Menu } from "@material-ui/core";
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

const kResizerSize = 10;

export const CollectionsTabPane: React.FunctionComponent<{}> = () => {
    const collections = BloomApi.useApiJson("collections/list");

    const [draggingSplitter, setDraggingSplitter] = useState(false);
    const [
        isSpreadsheetFeatureActive,
        setIsSpreadsheetFeatureActive
    ] = useState(false);

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

    useEffect(() => {
        BloomApi.get("app/enabledExperimentalFeatures", result => {
            const features: string = result.data; // This is a string containing the experimental feature names
            const featureIsActive = Boolean(
                features.includes("spreadsheet-import-export")
            );
            setIsSpreadsheetFeatureActive(featureIsActive);
        });
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
            // BL-11761: Always show this command
            // shouldShow: () => collections[0].isSourceCollection
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
            shouldShow: () => true, // show for all collections (except factory)
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
        collections[0].id,
        // Shouldn't be any at this level, but it works better to include this here too.
        isSpreadsheetFeatureActive
    );

    const sourcesCollections = collections.slice(1);
    // Enhance: may want to sort these by local name, though probably keeping Templates
    // and possibly Sample Shells at the top.
    const collectionComponents = sourcesCollections.map(c => {
        return (
            <BooksOfCollectionWithHeading
                key={c.id}
                name={c.name}
                id={c.id}
                shouldLocalizeName={c.shouldLocalizeName}
                manager={manager}
                isSpreadsheetFeatureActive={isSpreadsheetFeatureActive}
            />
        );
    });

    const editingSourceCollection =
        collections.length > 0 ? collections[0].isSourceCollection : false;
    const collectionsHeaderKey = editingSourceCollection
        ? "CollectionTab.SourcesForNewShellsHeading"
        : "CollectionTab.BookSourceHeading";
    const collectionsHeaderText = editingSourceCollection
        ? "Sources For New Shells"
        : "Sources For New Books";

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
                    }
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
                            isSpreadsheetFeatureActive={
                                isSpreadsheetFeatureActive
                            }
                        />
                    </div>

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
                            </div>
                        )}
                    </Transition>
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
            <EmbeddedProgressDialog id="collectionTab" />
        </div>
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
    icon?: React.ReactNode;
    // if true, menu item is rendered as an ApiCheckbox with the command as its api.
    checkbox?: boolean;
    // if true, menu item is rendered with a Bloom Enterprise icon on the right
    requiresEnterprise?: boolean;
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
    includeSpreadsheetItems: boolean
) => {
    const menuItemsT = menuItemsSpecs
        .map((spec: MenuItemSpec) => {
            if (spec.label === "-") {
                return <Divider />;
            }
            if (spec.submenu) {
                const submenuItems = makeMenuItems(
                    spec.submenu,
                    isEditableCollection,
                    isBookSavable,
                    close,
                    bookId,
                    collectionId,
                    includeSpreadsheetItems
                );
                return submenuItems.length ? (
                    <LocalizableNestedMenuItem
                        english={spec.label}
                        l10nId={spec.l10nId!}
                    >
                        {submenuItems}
                    </LocalizableNestedMenuItem>
                ) : (
                    undefined
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

            if (spec.checkbox) {
                return (
                    <LocalizableCheckboxMenuItem
                        english={spec.label}
                        l10nId={spec.l10nId!}
                        onClick={() => {
                            // We deliberately do NOT close the menu, so the user can see it really got checked.
                        }}
                        apiEndpoint={spec.command!}
                    ></LocalizableCheckboxMenuItem>
                );
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
            if (
                !includeSpreadsheetItems &&
                Boolean(spec.l10nId!.includes("Spreadsheet"))
            )
                return undefined;
            return (
                <LocalizableMenuItem
                    key={spec.l10nId}
                    english={spec.label}
                    l10nId={spec.l10nId!}
                    onClick={clickAction}
                    icon={spec.icon}
                    addEllipsis={spec.addEllipsis}
                    requiresEnterprise={spec.requiresEnterprise}
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
    manager: BookSelectionManager;
    isSpreadsheetFeatureActive: boolean;
}> = props => {
    const localizedName = useL10n(props.name, "CollectionTab." + props.name);
    const localName = props.shouldLocalizeName
        ? localizedName // putting the useL10n() hook here violates rules of hooks
        : props.name;
    return (
        <div key={"frag:" + props.id}>
            <h2>{localName}</h2>
            <BooksOfCollection
                collectionId={props.id}
                isEditableCollection={false}
                manager={props.manager}
                lazyLoadCollection={true}
                isSpreadsheetFeatureActive={props.isSpreadsheetFeatureActive}
            />
        </div>
    );
};

WireUpForWinforms(CollectionsTabPane);
