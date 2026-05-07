///<reference path="../../typings/toastr/toastr.d.ts"/>
/// <reference path="../../lib/localizationManager/localizationManager.ts" />

// This is one of the root files for our webpack build, the root from which
// pageThumbnailListBundle.js is built. Currently, contrary to our usual practice,
// this bundle is one of two loaded by pageThumbnailList.pug. It is imported last,
// so things it exports are accessible from outside the bundle using workspaceBundle.

import $ from "jquery";
import { css } from "@emotion/react";
import ContentCopyIcon from "@mui/icons-material/ContentCopy";
import ContentPasteIcon from "@mui/icons-material/ContentPaste";

import * as React from "react";
import { useState, useEffect } from "react";
import * as ReactDOM from "react-dom";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";

import * as toastr from "toastr";
import "errorHandler";
import WebSocketManager from "../../utils/WebSocketManager";
import { Responsive } from "react-grid-layout";
import {
    get,
    getAsync,
    postJson,
    postString,
    useApiData,
} from "../../utils/bloomApi";
import { PageThumbnail } from "./PageThumbnail";
import LazyLoad, { forceCheck } from "react-lazyload";
import { useL10n } from "../../react_components/l10nHooks";
import { callOnBlur } from "../../utils/menuCloseOnBlur";

// We're using the Responsive version of react-grid-layout because
// (1) the previous version of the page thumbnails, which this replaces,
// could be resized, and at one point...I discovered later that it
// was disabled...it could switch between one and two columns.
// So I started out that way. We decided not to support this in
// the new version, but it seemed fairly harmless to leave in some of
// the code needed to handle it.
// (2) I can't figure out the import command to get the non-responsive
// version.
// To make it actually responsive again, (switch to single column when narrow):
// - add WidthProvider to react-grid-layout import
// - uncomment the following line:
//const ResponsiveGridLayout = WidthProvider(Responsive);
// - Change the root element from Responsive to ResponsiveGridLayout
// - remove the width property (WidthProvider will set it)
// - the lg breakpoint should be about 170
// - figure out something to do to make it a little narrower
// when there's plenty of width so we don't get an unnecessary
// horizontal scroll bar.

const kWebsocketContext = "pageThumbnailList";
const kDeletePageMenuIconPath =
    "/bloom/bookEdit/pageThumbnailList/pageControls/deletePage.svg";

// A function configured once to listen for events coming from C# over the websocket.
let webSocketListenerFunction;

const rowHeight = 105; // px
const kPageThumbnailMenuIconSize = 16; // px
const pageThumbnailMenuIconStyle: React.CSSProperties = {
    width: kPageThumbnailMenuIconSize,
    height: kPageThumbnailMenuIconSize,
    color: "#000",
    flex: "0 0 auto",
};

// the objects we get from C#. Typically content is an empty string,
// and we later retrieve the real content.
export interface IPage {
    key: string;
    caption: string;
    content: string;
}

interface IPageMenuItem {
    id: string;
    label: string;
    l10nId: string;
    enabled?: boolean;
    icon?: React.ReactNode;
    isDivider?: boolean;
    addEllipsis?: boolean;
}

interface IContextMenuPoint {
    mouseX: number;
    mouseY: number;
    pageId: string;
}

// The page thumbnail list runs inside its own iframe. A normal popup rendered inside that iframe
// is clipped by the narrow sidebar, so this menu is portaled into the parent document instead.
// A portal means React still owns the component, but its DOM is mounted somewhere else.
const DeletePageMenuIcon: React.FunctionComponent = () => (
    <span
        style={{
            width: kPageThumbnailMenuIconSize,
            height: kPageThumbnailMenuIconSize,
            display: "inline-block",
            backgroundColor: "#000",
            WebkitMaskImage: `url(${kDeletePageMenuIconPath})`,
            maskImage: `url(${kDeletePageMenuIconPath})`,
            WebkitMaskRepeat: "no-repeat",
            maskRepeat: "no-repeat",
            WebkitMaskPosition: "center",
            maskPosition: "center",
            WebkitMaskSize: "contain",
            maskSize: "contain",
            flex: "0 0 auto",
        }}
    />
);

const PageThumbnailContextMenuItem: React.FunctionComponent<{
    item: IPageMenuItem;
    onItemClick: (commandId: string) => void;
    isActive: boolean;
    onActivate: (commandId: string) => void;
    onDeactivate: () => void;
    buttonRef: (element: HTMLButtonElement | null) => void;
}> = (props) => {
    const localizedLabel = useL10n(props.item.label, props.item.l10nId);
    const label = props.item.addEllipsis
        ? `${localizedLabel}...`
        : localizedLabel;
    const isDisabled = !props.item.enabled;
    const backgroundColor =
        props.isActive && !isDisabled ? "rgba(0, 0, 0, 0.08)" : "transparent";

    return (
        <button
            type="button"
            disabled={isDisabled}
            onClick={() => props.onItemClick(props.item.id)}
            onMouseEnter={() => props.onActivate(props.item.id)}
            onMouseLeave={props.onDeactivate}
            onFocus={() => props.onActivate(props.item.id)}
            onBlur={props.onDeactivate}
            ref={props.buttonRef}
            role="menuitem"
            aria-disabled={isDisabled}
            tabIndex={props.isActive && !isDisabled ? 0 : -1}
            style={{
                appearance: "none",
                WebkitAppearance: "none",
                display: "flex",
                alignItems: "center",
                boxSizing: "border-box",
                width: "100%",
                minHeight: "24px",
                padding: "4px 8px",
                border: "none",
                borderRadius: 0,
                background: backgroundColor,
                color: isDisabled ? "rgba(0, 0, 0, 0.38)" : "#000",
                fontSize: "10pt",
                lineHeight: "normal",
                textAlign: "left",
                cursor: isDisabled ? "default" : "pointer",
                outline: "none",
                boxShadow: "none",
                fontFamily: "inherit",
            }}
        >
            <span
                style={{
                    display: "inline-flex",
                    alignItems: "center",
                    width: "28px",
                    minWidth: "28px",
                    opacity: isDisabled ? 0.38 : 1,
                }}
            >
                {props.item.icon}
            </span>
            <span
                style={{
                    display: "block",
                    flex: "1 1 auto",
                    whiteSpace: "normal",
                }}
            >
                {label}
            </span>
        </button>
    );
};

const PageThumbnailContextMenu: React.FunctionComponent<{
    contextMenuPoint: IContextMenuPoint;
    contextMenuItems: IPageMenuItem[];
    onClose: () => void;
    onItemClick: (commandId: string) => void;
}> = (props) => {
    const menuRef = React.useRef<HTMLDivElement>(null);
    const menuButtonRefs = React.useRef<
        Record<string, HTMLButtonElement | null>
    >({});
    const [activeItemId, setActiveItemId] = useState<string>();
    // Render into the parent document so the menu can extend beyond the page-list iframe.
    const portalDocument =
        window.parent && window.parent !== window
            ? window.parent.document
            : document;
    const portalWindow = portalDocument.defaultView ?? window;
    const enabledMenuItems = React.useMemo(
        () =>
            props.contextMenuItems.filter(
                (item) => !item.isDivider && item.enabled !== false,
            ),
        [props.contextMenuItems],
    );
    const menuMinWidth = 220;
    const menuMaxWidth = Math.min(360, portalWindow.innerWidth - 16);
    const estimatedMenuHeight = props.contextMenuItems.reduce(
        (total, item) => total + (item.isDivider ? 9 : 32),
        8,
    );
    const left = Math.max(
        8,
        Math.min(
            props.contextMenuPoint.mouseX,
            portalWindow.innerWidth - menuMinWidth - 8,
        ),
    );
    const top = Math.max(
        8,
        Math.min(
            props.contextMenuPoint.mouseY,
            portalWindow.innerHeight - estimatedMenuHeight - 8,
        ),
    );

    // This effect is necessary because the menu is portaled into the parent document,
    // so closing it depends on document-level events outside this iframe's normal React tree.
    // We listen in the parent document and same-origin Bloom iframes so clicks on pages,
    // thumbnails, or top-level tabs all dismiss the menu.
    useEffect(() => {
        // The menu can be dismissed from several different same-origin documents: this iframe,
        // the parent workspace document where the portal is rendered, and sibling Bloom iframes
        // such as the main page frame. Collect them once so we can wire the same close behavior
        // everywhere a click or Escape key might happen.
        const documentsThatCanDismissTheMenu = Array.from(
            new Set([
                document,
                portalDocument,
                ...Array.from(portalDocument.querySelectorAll("iframe"))
                    .map((iframeElement) => iframeElement.contentDocument)
                    .filter(
                        (iframeDocument): iframeDocument is Document =>
                            !!iframeDocument,
                    ),
            ]),
        );

        // Handle any pointer or click that lands outside the menu itself. We use capture-phase
        // pointerdown plus a click fallback because focus changes, iframe boundaries, and tab
        // switches can otherwise prevent a normal bubbling click handler from running reliably.
        const handlePointerDown = (event: Event) => {
            const target = event.target;
            if (!target || !("nodeType" in target)) {
                return;
            }

            if (!menuRef.current?.contains(target as Node)) {
                props.onClose();
            }
        };

        // Escape should dismiss the menu no matter which same-origin Bloom document currently has focus.
        const handleKeyDown = (event: KeyboardEvent) => {
            if (event.key === "Escape") {
                props.onClose();
            }
        };

        documentsThatCanDismissTheMenu.forEach((currentDocument) => {
            currentDocument.addEventListener(
                "pointerdown",
                handlePointerDown,
                true,
            );
            currentDocument.addEventListener("click", handlePointerDown, true);
            currentDocument.addEventListener("keydown", handleKeyDown);
        });

        return () => {
            documentsThatCanDismissTheMenu.forEach((currentDocument) => {
                currentDocument.removeEventListener(
                    "pointerdown",
                    handlePointerDown,
                    true,
                );
                currentDocument.removeEventListener(
                    "click",
                    handlePointerDown,
                    true,
                );
                currentDocument.removeEventListener("keydown", handleKeyDown);
            });
        };
    }, [props, portalDocument]);

    // This effect is necessary because the menu is portaled into the parent document,
    // so React does not move keyboard focus into the external overlay unless we do it explicitly.
    useEffect(() => {
        if (enabledMenuItems.length === 0) {
            setActiveItemId(undefined);
            return;
        }

        setActiveItemId((currentActiveItemId) => {
            if (
                currentActiveItemId &&
                enabledMenuItems.some((item) => item.id === currentActiveItemId)
            ) {
                return currentActiveItemId;
            }

            return enabledMenuItems[0].id;
        });
    }, [enabledMenuItems]);

    // This effect is necessary because keyboard navigation updates React state,
    // but the browser focus itself must still be synchronized to the active menu item element.
    useEffect(() => {
        if (!activeItemId) {
            return;
        }

        menuButtonRefs.current[activeItemId]?.focus();
    }, [activeItemId]);

    const handleMenuKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
        if (enabledMenuItems.length === 0) {
            return;
        }

        const currentIndex = enabledMenuItems.findIndex(
            (item) => item.id === activeItemId,
        );
        const normalizedIndex = currentIndex >= 0 ? currentIndex : 0;

        switch (event.key) {
            case "ArrowDown":
                event.preventDefault();
                setActiveItemId(
                    enabledMenuItems[
                        (normalizedIndex + 1) % enabledMenuItems.length
                    ].id,
                );
                break;
            case "ArrowUp":
                event.preventDefault();
                setActiveItemId(
                    enabledMenuItems[
                        (normalizedIndex - 1 + enabledMenuItems.length) %
                            enabledMenuItems.length
                    ].id,
                );
                break;
            case "Home":
                event.preventDefault();
                setActiveItemId(enabledMenuItems[0].id);
                break;
            case "End":
                event.preventDefault();
                setActiveItemId(
                    enabledMenuItems[enabledMenuItems.length - 1].id,
                );
                break;
            case "Tab":
                props.onClose();
                break;
        }
    };

    return ReactDOM.createPortal(
        <div
            ref={menuRef}
            role="menu"
            aria-orientation="vertical"
            onKeyDown={handleMenuKeyDown}
            style={{
                position: "fixed",
                top: `${top}px`,
                left: `${left}px`,
                width: "max-content",
                minWidth: `${menuMinWidth}px`,
                maxWidth: `${menuMaxWidth}px`,
                padding: "6px 0",
                background: "#fff",
                borderRadius: "4px",
                boxShadow: "0 3px 16px rgba(0, 0, 0, 0.24)",
                zIndex: 1500,
            }}
        >
            {props.contextMenuItems.map((item) =>
                item.isDivider ? (
                    <div
                        key={item.id}
                        role="separator"
                        style={{
                            height: "1px",
                            margin: "4px 0",
                            background: "rgba(0, 0, 0, 0.12)",
                        }}
                    />
                ) : (
                    <PageThumbnailContextMenuItem
                        key={item.id}
                        item={item}
                        onItemClick={props.onItemClick}
                        isActive={activeItemId === item.id}
                        onActivate={setActiveItemId}
                        onDeactivate={() => setActiveItemId(undefined)}
                        buttonRef={(element) => {
                            menuButtonRefs.current[item.id] = element;
                        }}
                    />
                ),
            )}
        </div>,
        portalDocument.body,
    );
};

// Thumbnails render only the page div, but many Bloom layout rules are written against
// body[data-*] selectors. Copy the book body's data-* attributes onto a wrapper here so
// those selectors still match in the thumbnail pane. Lowercase them first because React
// only forwards custom DOM attributes reliably when the prop name is lowercase.
const normalizeBookDisplayAttributes = (
    attributes: Record<string, string>,
): Record<string, string> => {
    const normalized: Record<string, string> = {};
    Object.entries(attributes).forEach(([key, value]) => {
        normalized[key.startsWith("data-") ? key.toLowerCase() : key] = value;
    });
    return normalized;
};

// This map goes from page ID to a callback that we get from the page thumbnail
// which should be called when the main Bloom program informs us that
// the thumbnail needs to be updated.
// It really has to be the same object on different calls to the function,
// because some things that use it only happen once while others happen
// more often. We'll need another solution in the unlikely event of
// there ever being more than one instance of pageThumbnailList.
const pageIdToRefreshMap = new Map<string, () => void>();

const PageList: React.FunctionComponent<{ initialPageLayout: string }> = (
    props,
) => {
    const [realPageList, setRealPageList] = useState<IPage[]>([]);
    const [pageLayout, setPageLayout] = useState(props.initialPageLayout);
    // a value to be bumped to force a reload of page content when the websocket detects
    // a request for this.
    const [reloadValue, setReloadValue] = useState(0);
    // a value to be bumped to force the pages to be reset to their original positions
    // when the websocket detects a request for this.
    const [resetValue, setResetValue] = useState(1);
    const [twoColumns, setTwoColumns] = useState(true);
    const [contextMenuPoint, setContextMenuPoint] =
        useState<IContextMenuPoint>();
    const [contextMenuItems, setContextMenuItems] = useState<IPageMenuItem[]>(
        [],
    );

    const [selectedPageId, setSelectedPageId] = useState("");
    const bookAttributesThatMayAffectDisplay = useApiData<any>(
        "pageList/bookAttributesThatMayAffectDisplay",
        {},
    );
    const normalizedBookDisplayAttributes = React.useMemo(
        () =>
            normalizeBookDisplayAttributes(bookAttributesThatMayAffectDisplay),
        [bookAttributesThatMayAffectDisplay],
    );

    const pageMenuDefinition: IPageMenuItem[] = [
        {
            id: "copyPage",
            label: "Copy Page",
            l10nId: "EditTab.CopyPage",
            icon: <ContentCopyIcon style={pageThumbnailMenuIconStyle} />,
        },
        {
            id: "pastePage",
            label: "Paste Page",
            l10nId: "EditTab.PastePage",
            icon: <ContentPasteIcon style={pageThumbnailMenuIconStyle} />,
        },
        {
            id: "duplicatePage",
            label: "Duplicate Page",
            l10nId: "EditTab.DuplicatePageButton",
        },
        {
            id: "duplicatePageManyTimes",
            label: "Duplicate Page Many Times...",
            l10nId: "EditTab.DuplicatePageMultiple",
        },
        {
            id: "dividerBeforeChooseDifferentLayout",
            label: "",
            l10nId: "-",
            isDivider: true,
        },
        {
            id: "chooseDifferentLayout",
            label: "Choose Different Layout",
            l10nId: "EditTab.ChooseLayoutButton",
            addEllipsis: true,
        },
        {
            id: "dividerBeforeRemovePage",
            label: "",
            l10nId: "-",
            isDivider: true,
        },
        {
            id: "removePage",
            label: "Remove Page",
            l10nId: "EditTab.DeletePageButton",
            icon: <DeletePageMenuIcon />,
        },
    ];

    // All the code in this useEffect is one-time initialization.
    useEffect(() => {
        let localizedNotification = "";

        // This function will be hooked up (after we set localizedNotification properly)
        // to be called when C# sends messages through the web socket.
        // We need a named function because it looks cleaner.
        webSocketListenerFunction = (event) => {
            switch (event.id) {
                case "saving": {
                    toastr.info(localizedNotification, "", {
                        positionClass: "toast-top-left",
                        preventDuplicates: true,
                        showDuration: 300,
                        hideDuration: 300,
                        timeOut: 1000,
                        extendedTimeOut: 1000,
                        showEasing: "swing",
                        showMethod: "fadeIn",
                        hideEasing: "linear",
                        hideMethod: "fadeOut",
                        messageClass: "toast-for-saved-message",
                        iconClass: "",
                    });
                    break;
                }
                case "selecting":
                    setSelectedPageId(event.message);
                    break;
                case "pageNeedsRefresh": {
                    const problemPageId = event.message;
                    const callback = pageIdToRefreshMap.get(problemPageId);
                    if (callback) callback();
                    break;
                }
                case "pageListNeedsRefresh":
                    // pass function so we're not incrementing a stale value captured
                    // when we set up this function. Bumping this number triggers
                    // re-running a useEffect.
                    setReloadValue((oldReloadValue) => oldReloadValue + 1);
                    break;
                case "pageListNeedsReset":
                    // Here we want to force a re-render to put the objects back in
                    // their 'original' positions. That is difficult since nothing
                    // has changed in the data we send to the react grid component, and
                    // it is designed NOT to re-render when its props don't change, so
                    // that dragged positions are not too easily lost. See the trick
                    // below that uses resetValue for something we don't care about.
                    setResetValue((oldResetValue) => oldResetValue + 1);
                    break;
            }
        };

        theOneLocalizationManager
            .asyncGetText("EditTab.SavingNotification", "Saving...", "")
            .done((savingNotification) => {
                localizedNotification = savingNotification;
                WebSocketManager.addListener(
                    kWebsocketContext,
                    webSocketListenerFunction,
                );
            });
        theOneLocalizationManager
            .asyncGetText("EditTab.PageList.Heading", "Pages", "")
            .done((heading) => {
                const label = document.getElementById("pageThumbnailListLabel");
                if (label) label.textContent = heading;
            });
    }, []);

    // Initially we have an empty page list. Then we run this once and get a list
    // of pages that have the right ID and caption (and the right number of pages)
    // but the content is empty. The individual thumbnail objects do their own API
    // calls to fill in the page content. Then this runs again when the sequence
    // of pages changes (e.g., adding a page or re-ordering them).
    useEffect(() => {
        get("pageList/pages", (response) => {
            // We're using a double approach here. The WebThumbnailList actually gets
            // notified a few times of the initial selected page. Each time, it sends
            // a message to the listener above. But, there's async stuff involved
            // in when the listener starts listening. So we might miss all of them.
            // Therefore, the actual 'source of truth' for which page is selected
            // is a property of the PageListApi, and that is also set as soon as the
            // WebThumbnailList hears about it, and we get it along with the page list
            // in case we miss a notification.
            setSelectedPageId(response.data.selectedPageId);
            setPageLayout(response.data.pageLayout || "A5Portrait");
            setRealPageList(response.data.pages);
            // The current page may need to be refreshed as well for new or retitled books.
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-9039.
            const callback = pageIdToRefreshMap.get(
                response.data.selectedPageId,
            );
            if (callback) callback();

            // auto walk for experiment
            //ContinueAutomatedPageClicking(realPageList);
        });
    }, [reloadValue]);

    // Ensure that the thumbnail of the selected page is scrolled into view when a book
    // is opened for editing.  See https://issues.bloomlibrary.org/youtrack/issue/BL-8701.
    useEffect(() => {
        if (selectedPageId) {
            const pageElement = window.document.getElementById(selectedPageId);
            // nearest causes the minimum possible scroll to make it visible,
            // importantly including not scrolling at all if it's already visible.
            if (pageElement)
                pageElement.scrollIntoView({
                    block: "nearest",
                });
        }
        // Make LazyLoad component re-check for elements in viewport
        // to make visible. (See this article that says forceCheck() should be in a 'useEffect':
        // https://stackoverflow.com/questions/61191496/why-is-my-react-lazyload-component-not-working)
        // This actually runs each time a page is deleted.
        forceCheck();
    }, [realPageList, selectedPageId]);

    // this is embedded so that we have access to realPageList
    const handleGridItemClick = (e: React.MouseEvent<HTMLDivElement>) => {
        e.stopPropagation();
        e.preventDefault();
        closeContextMenu();

        // for manual testing
        if (e.getModifierState("Control") && e.getModifierState("Alt")) {
            ContinueAutomatedPageClicking(realPageList);
        } else {
            if (e.currentTarget) {
                // Here we are handling a click on a div.invisibleThumbnailCover element.
                // We actually have the same ID on 2 different divs here (assuming the lazy load
                // process has actually finished filling in this page)! The call to ".closest()" below
                // looks at ancestors, so here we find the outer div.gridItem.
                // However, the prior sibling of the clicked div.invisibleThumbnailCover is the actual
                // div.bloom-page (with the same ID).
                // If at some point we want to do something regarding the 'data-tool-id' attribute for
                // a page when switching to that page in Edit mode, this is the place.
                const pageElt = e.currentTarget.closest("[id]")!;
                const pageId = pageElt.getAttribute("id");
                const caption = pageElt.getAttribute("data-caption");
                postJson("pageList/pageClicked", {
                    pageId,
                    detail: caption,
                });
            }
        }
    };

    const openContextMenuCount = React.useRef(0);

    const closeContextMenu = () => {
        openContextMenuCount.current = 0;
        setContextMenuPoint(undefined);
        setContextMenuItems([]);
    };

    const openContextMenuNearElement = (
        pageId: string,
        element: HTMLElement,
    ) => {
        const rect = element.getBoundingClientRect();
        openContextMenu(pageId, rect.left, rect.bottom);
    };

    const openContextMenu = (pageId: string, x: number, y: number) => {
        openContextMenuCount.current++;
        const currentCount = openContextMenuCount.current;
        // If the user right-clicks on a page that is not selected, ignore the click and
        // close any existing context menu.
        if (pageId !== selectedPageId) {
            closeContextMenu();
            return;
        }

        Promise.all(
            pageMenuDefinition.map(async (item) => {
                if (item.isDivider) {
                    return item;
                }
                const response = await getAsync(
                    `pageList/contextMenuItemEnabled?commandId=${encodeURIComponent(item.id)}&pageId=${encodeURIComponent(pageId)}`,
                );
                return {
                    id: item.id,
                    label: item.label,
                    l10nId: item.l10nId,
                    enabled: !!response.data,
                    icon: item.icon,
                    isDivider: item.isDivider,
                    addEllipsis: item.addEllipsis,
                };
            }),
        ).then((menuItems) => {
            if (currentCount !== openContextMenuCount.current) {
                // Either the menu was closed or a newer context menu has been opened since this async
                // call was made, so ignore the results.
                return;
            }
            // Mouse coordinates are reported relative to this iframe. Convert them to the parent
            // document's coordinate space because the menu itself is rendered in the parent document.
            const hostFrameRect = (
                window.frameElement as HTMLElement | null
            )?.getBoundingClientRect();
            setContextMenuItems(menuItems);
            setContextMenuPoint({
                mouseX: (hostFrameRect?.left ?? 0) + x - 2,
                mouseY: (hostFrameRect?.top ?? 0) + y - 4,
                pageId,
            });
            // Close the menu if the user clicks outside Bloom altogether.
            callOnBlur(closeContextMenu);
        });
    };

    const onContextMenuItemClick = (commandId: string) => {
        if (!contextMenuPoint) return;

        postJson("pageList/contextMenuItemClicked", {
            pageId: contextMenuPoint.pageId,
            commandId,
        });
        closeContextMenu();
    };

    const handleContextMenu = (e: React.MouseEvent<HTMLDivElement>) => {
        e.stopPropagation();
        e.preventDefault();
        if (e.currentTarget) {
            const pageElt = e.currentTarget.closest("[id]")!;
            const pageId = pageElt.getAttribute("id");
            if (pageId) {
                openContextMenu(pageId, e.clientX, e.clientY);
            }
        }
    };

    // We insert a dummy invisible page to make the outside cover a 'right' page
    // and all the others correctly paired. (Probably should remove if we ever fully
    // support single-column.)
    const pageList: IPage[] = [
        {
            key: "placeholder",
            caption: "",
            content: "",
        },
        ...realPageList,
    ];
    const pages = pageList.map((pageContent, index) => {
        return (
            <div
                key={pageContent.key} // for efficient react manipulation of list
                id={pageContent.key} // used by C# code to identify page
                data-caption={pageContent.caption}
                className={
                    "gridItem " +
                    (pageContent.key === "placeholder" ? " placeholder" : "") +
                    (selectedPageId === pageContent.key ? " gridSelected" : "")
                }
                css={css`
                    .lazyload-wrapper {
                        height: 100%;
                    }
                `}
            >
                <LazyLoad
                    height={rowHeight}
                    scrollContainer="#pageGridWrapper"
                    resize={true} // expand lazy elements as needed when container resizes
                >
                    <PageThumbnail
                        page={pageContent}
                        left={!(index % 2)}
                        pageLayout={pageLayout}
                        configureReloadCallback={(id, callback) =>
                            pageIdToRefreshMap.set(id, callback)
                        }
                        onClick={handleGridItemClick}
                        onContextMenu={handleContextMenu}
                    />
                    {selectedPageId === pageContent.key && (
                        <button
                            id="menuIconHolder"
                            type="button"
                            className="menuHolder"
                            aria-label="Page options"
                            aria-haspopup="menu"
                            aria-expanded={!!contextMenuPoint}
                            onClick={(e) => {
                                e.stopPropagation();
                                e.preventDefault();
                                openContextMenuNearElement(
                                    pageContent.key,
                                    e.currentTarget,
                                );
                            }}
                            onKeyDown={(e) => {
                                if (
                                    e.key === "ArrowDown" ||
                                    e.key === "ContextMenu"
                                ) {
                                    e.stopPropagation();
                                    e.preventDefault();
                                    openContextMenuNearElement(
                                        pageContent.key,
                                        e.currentTarget,
                                    );
                                }
                            }}
                            css={css`
                                border: none;
                                padding: 0;
                                background: transparent;
                                cursor: pointer;
                            `}
                        >
                            <svg
                                xmlns="http://www.w3.org/2000/svg"
                                width="18"
                                height="18"
                                viewBox="0 0 18 18"
                            >
                                <path d="M5 8l4 4 4-4z" fill="white" />
                            </svg>
                        </button>
                    )}
                </LazyLoad>
            </div>
        );
    });

    // Set up some objects and functions we need as params for our main element.
    // Some of them come in sets "lg" and "sm". Currently the "lg" (two-column)
    // version is always used; the other would be for single column.

    // not currently used.
    const singleColLayout = pageList.map((page, index) => {
        return {
            i: page.key,
            x: 0,
            y: index,
            w: 1,
            h: 1,
        };
    });
    const twoColLayout = pageList.map((page, index) => {
        const left = !(index % 2);
        // review: should we make all xmatter pages non-draggable? Or stick with giving an
        // error message if they make an inappropriate drag?
        const draggable = index !== 0;
        return {
            i: page.key,
            x: left ? 0 : 1,
            y: Math.floor(index / 2),
            w: 1,
            h: 1,
            // We don't ever mess with widths other than 1, so maxW is insignificant.
            // However, passing it as resetValue means that when we change resetValue,
            // the react-grid component detects that we are passing in a differnt layout,
            // even though none of the props we care about has changed. This forces
            // the re-render we need when forcing objects back to their original positions
            // (e.g., after a forbidden drag).
            maxW: resetValue,
            draggable, // todo: not working.
        };
    });
    const layouts = {
        lg: twoColLayout,
        sm: singleColLayout,
    };

    // Useful if we get responsive...figures out whether the responsive grid has
    // decided to be single-column or double-column.
    const onLayoutChange = (layouts: ReactGridLayout.Layout[]) => {
        setTwoColumns(layouts && layouts.length > 1 && layouts[1].x > 0);
    };

    return (
        <div id="wrapperForBodyAttributes" {...normalizedBookDisplayAttributes}>
            <Responsive
                className="page-thumbnail-list"
                width={180}
                layouts={layouts}
                // lg (two-column) if it's more than 90px wide. That's barely enough for one column,
                // so may want to increase it if we really go responsive; but currently, single column
                // looks strange if there's any extra white space, with the thumbnails staggered
                // left and right. So for now we've fixed the width of the thumbnail pane, making
                // it big enough for two full columns always.
                breakpoints={{
                    lg: 90,
                    sm: 0,
                }}
                rowHeight={rowHeight}
                compactType="wrap"
                cols={{
                    lg: 2,
                    sm: 1,
                }}
                onLayoutChange={onLayoutChange}
                onDragStop={onDragStop}
                // override react-grid-layout sticking a fixed height on us.  It can occasionally be wrong and
                // cause a scroll bar to appear when it shouldn't.  (BL-16021)
                css={css`
                    height: 100% !important;
                `}
            >
                {pages}
            </Responsive>
            {contextMenuPoint && (
                <PageThumbnailContextMenu
                    contextMenuPoint={contextMenuPoint}
                    contextMenuItems={contextMenuItems}
                    onClose={closeContextMenu}
                    onItemClick={onContextMenuItemClick}
                />
            )}
        </div>
    );
};

$(window).ready(() => {
    const pageLayout =
        document.body.getAttribute("data-pageSize") || "A5Portrait";
    ReactDOM.render(
        <PageList initialPageLayout={pageLayout} />,
        document.getElementById("pageGridWrapper"),
    );
});

// Function invoked when dragging a page ends. Note that it is often
// called when all the user intended was to click the page, presumably
// because some tiny movement was made while the mouse is down.
function onDragStop(
    layout: ReactGridLayout.Layout[],
    // oldItem and newItem are the same page, but with the old position (x, y props)
    // and new position. It's quite possible with a small drag that they are the same.
    oldItem: ReactGridLayout.Layout,
    newItem: ReactGridLayout.Layout,
    placeholder: ReactGridLayout.Layout,
    e: MouseEvent,
    element: HTMLElement,
) {
    const movedPageId = newItem.i;

    // do nothing if it didn't move (this seems to get fired on any click,
    // even just closing a popup menu)
    if (oldItem.y == newItem.y && oldItem.x == newItem.x) {
        // It didn't move. But of course the user did click on it, and perhaps the drag
        // was tiny and unintentional. In any case it seems appropriate to consider
        // the page clicked. (Note however that this seems to get fired on any click,
        // even just closing a popup menu, so it's possible that we might get more
        // click events than we really want.)
        postJson("pageList/pageClicked", {
            pageId: movedPageId,
            detail: "unknown",
        });
        return;
    }
    // Needs more smarts if we ever do other than two columns.
    const newIndex = newItem.y * 2 + newItem.x;

    postJson("pageList/pageMoved", { movedPageId, newIndex });
}

function ContinueAutomatedPageClicking(
    pagesRemaining: IPage[],
    count: number = 0,
) {
    const kHowManyPages = 1000; // no way other than code to change this at the moment
    if (count > kHowManyPages) return;

    if (count === 0) {
        postString(
            "common/logger/writeEvent",
            "**  pageThumbnailList: user initiated Automated Page Clicking test function",
        );
    }
    postJson(
        "pageList/pageClicked",
        {
            pageId: pagesRemaining[0].key,
            detail: pagesRemaining[0].caption,
        },
        () => {
            const remaining = pagesRemaining.slice(1);
            if (remaining.length > 0)
                window.setTimeout(
                    () => {
                        ContinueAutomatedPageClicking(remaining, count + 1);
                    },
                    8 * 1000, // leave time for the browser to redraw
                );
            else window.alert("Done with automated page clicking");
        },
    );
}
