/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

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
import { ApiBackedCheckbox } from "../react_components/apiBackedCheckbox";
import { useL10n } from "../react_components/l10nHooks";
import ShowAfterDelay from "../react_components/showAfterDelay";
import { forceCheck as convertAnyVisibleLazyLoads } from "react-lazyload";
import IconButton from "@material-ui/core/IconButton";
import Divider from "@material-ui/core/Divider";
import ListItemIcon from "@material-ui/core/ListItemIcon";
import ListItemText from "@material-ui/core/ListItemText";
import Menu from "@material-ui/core/Menu";
import MenuItem from "@material-ui/core/MenuItem";
import NestedMenuItem from "material-ui-nested-menu-item";
import GreyTriangleMenuIcon from "../react_components/icons/GreyTriangleMenuIcon";

const kResizerSize = 10;

export const CollectionsTabPane: React.FunctionComponent<{}> = () => {
    const collections = BloomApi.useApiJson("collections/list");

    const [draggingSplitter, setDraggingSplitter] = useState(false);

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
        var menuBackdrop = (event.target as HTMLElement).closest(
            ".MuiPopover-root"
        );
        if (menuBackdrop) {
            (event.target as HTMLElement).click();
            return;
        }

        setAdjustedContextMenuPoint(event.clientX - 2, event.clientY - 4);
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

    const sourcesCollections = collections.slice(1);
    const collectionComponents = sourcesCollections.map(c => {
        return (
            <div key={"frag:" + c.id}>
                <h2>{c.name}</h2>
                <BooksOfCollection
                    collectionId={c.id}
                    isEditableCollection={false}
                    manager={manager}
                    lazyLoadCollection={true}
                />
            </div>
        );
    });

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
                    split="horizontal"
                    // TODO: the splitter library lets us specify a height, but it doesn't apply it correctly an so the pane that follows
                    // does not get pushed down to make room for the thicker resizer
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
                                <h1>Sources For New Books</h1>

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

            if (spec.checkbox) {
                return (
                    <LocalizableCheckboxMenuItem
                        english={spec.label}
                        l10nId={spec.l10nId!}
                        onClick={() => close()}
                        apiEndpoint={spec.command!}
                        icon={spec.icon}
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
            return (
                <LocalizableMenuItem
                    english={spec.label}
                    l10nId={spec.l10nId!}
                    onClick={clickAction}
                    icon={spec.icon}
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
    icon?: React.ReactNode;
}> = props => {
    const label = useL10n(props.english, props.l10nId);
    return (
        <MenuItem key={props.l10nId} onClick={props.onClick}>
            {props.icon ? (
                <React.Fragment>
                    <ListItemIcon
                        css={css`
                            min-width: 30px !important; // overrides MUI default that leaves way too much space
                        `}
                    >
                        {props.icon}
                    </ListItemIcon>
                    <ListItemText>{label}</ListItemText>
                </React.Fragment>
            ) : (
                label
            )}
        </MenuItem>
    );
};

const LocalizableCheckboxMenuItem: React.FunctionComponent<{
    english: string;
    l10nId: string;
    onClick: React.MouseEventHandler<HTMLElement>;
    apiEndpoint: string;
    icon?: React.ReactNode;
}> = props => {
    const label = useL10n(props.english, props.l10nId);
    return (
        <MenuItem key={props.l10nId} onClick={props.onClick}>
            <ApiBackedCheckbox
                l10nKey={props.l10nId!}
                apiEndpoint={props.apiEndpoint}
            >
                {label}
            </ApiBackedCheckbox>
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

WireUpForWinforms(CollectionsTabPane);
