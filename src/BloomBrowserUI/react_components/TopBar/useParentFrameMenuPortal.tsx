import * as React from "react";
import createCache from "@emotion/cache";
import { CacheProvider, type EmotionCache } from "@emotion/react";
import { css } from "@emotion/react";
import { createPortal } from "react-dom";

// Manages the process of opening a menu (typically from TopBar) in the parent window,
// so that it can appear above the toolbox and other iframes in the current window.
// It also provides a hook for rendering the menu in the parent frame.

// Helper function makes sure we use cache keys that are valid for emotion.
// (They must be lower-case and only contain letters and hyphens.)
const normalizeEmotionCacheKey = (cacheKey: string): string => {
    const normalized = cacheKey
        .toLowerCase()
        .replace(/[^a-z-]+/g, "-")
        .replace(/-+/g, "-")
        .replace(/^-|-$/g, "");
    return normalized || "menu-parent";
};

export function useParentFrameMenuPortal() {
    const [anchorEl, setAnchorEl] = React.useState<HTMLElement | null>(null);
    const [suppressTooltip, setSuppressTooltip] = React.useState(false);
    const [parentContainer, setParentContainer] =
        React.useState<HTMLElement | null>(null);
    const [parentEmotionCache, setParentEmotionCache] =
        React.useState<EmotionCache | null>(null);
    const parentAnchorElRef = React.useRef<HTMLElement | null>(null);
    const parentEmotionCacheRef = React.useRef<EmotionCache | null>(null);

    const cleanupParentAnchor = React.useCallback(() => {
        if (parentAnchorElRef.current) {
            parentAnchorElRef.current.remove();
            parentAnchorElRef.current = null;
        }
    }, []);

    const cleanupParentEmotionCache = React.useCallback(() => {
        if (parentEmotionCacheRef.current) {
            parentEmotionCacheRef.current.sheet.flush();
            parentEmotionCacheRef.current = null;
        }
    }, []);

    const closeMenu = React.useCallback(() => {
        setAnchorEl(null);
        cleanupParentAnchor();
        cleanupParentEmotionCache();
        setParentContainer(null);
        setParentEmotionCache(null);
    }, [cleanupParentAnchor, cleanupParentEmotionCache]);

    const setMenuAnchor = React.useCallback((anchor: HTMLElement | null) => {
        setAnchorEl(anchor);
    }, []);

    const suppressTooltipUntilPointerReset = React.useCallback(() => {
        setSuppressTooltip(true);
    }, []);

    const clearTooltipSuppression = React.useCallback(() => {
        setSuppressTooltip(false);
    }, []);

    const releaseTooltipSuppressionIfMenuClosed = React.useCallback(() => {
        if (!anchorEl) {
            setSuppressTooltip(false);
        }
    }, [anchorEl]);

    const prepareAnchorAtButton = React.useCallback(
        (buttonId: string, cacheKey: string): HTMLElement | null => {
            const buttonElement = document.getElementById(buttonId);
            if (!buttonElement) {
                return null;
            }

            const parentDocument = window.parent?.document;
            if (!parentDocument || parentDocument === document) {
                cleanupParentAnchor();
                cleanupParentEmotionCache();
                setParentContainer(null);
                setParentEmotionCache(null);
                return buttonElement;
            }

            cleanupParentAnchor();

            const rect = buttonElement.getBoundingClientRect();
            const parentAnchor = parentDocument.createElement("div");
            parentAnchor.style.position = "fixed";
            parentAnchor.style.left = `${rect.left}px`;
            parentAnchor.style.top = `${rect.bottom}px`;
            parentAnchor.style.width = `${rect.width}px`;
            parentAnchor.style.height = "1px";
            parentAnchor.style.pointerEvents = "none";
            parentAnchor.style.zIndex = "2147483647";
            parentDocument.body.appendChild(parentAnchor);

            const cache = createCache({
                key: normalizeEmotionCacheKey(cacheKey),
                container: parentDocument.head,
                prepend: true,
            });

            parentAnchorElRef.current = parentAnchor;
            parentEmotionCacheRef.current = cache as unknown as EmotionCache;
            setParentContainer(parentDocument.body);
            setParentEmotionCache(cache as unknown as EmotionCache);

            return parentAnchor;
        },
        [cleanupParentAnchor, cleanupParentEmotionCache],
    );

    const openMenuAtButton = React.useCallback(
        (buttonId: string, cacheKey: string): HTMLElement | null => {
            const anchor = prepareAnchorAtButton(buttonId, cacheKey);
            if (anchor) {
                setMenuAnchor(anchor);
            }
            return anchor;
        },
        [prepareAnchorAtButton, setMenuAnchor],
    );

    const openMenuAtButtonWithItemsLoader = React.useCallback(
        (
            buttonId: string,
            cacheKey: string,
            existingItemCount: number,
            loadItems: (onLoaded?: (itemCount: number) => void) => void,
        ): boolean => {
            const preparedAnchor = prepareAnchorAtButton(buttonId, cacheKey);
            if (!preparedAnchor) {
                return false;
            }

            if (existingItemCount > 0) {
                setMenuAnchor(preparedAnchor);
                loadItems();
                return true;
            }

            loadItems((itemCount) => {
                if (itemCount > 0) {
                    setMenuAnchor(preparedAnchor);
                } else {
                    closeMenu();
                }
            });

            return true;
        },
        [closeMenu, prepareAnchorAtButton, setMenuAnchor],
    );

    const renderMenuInParentFrame = React.useCallback(
        (menu: React.ReactNode): React.ReactNode => {
            if (parentContainer && parentEmotionCache) {
                return createPortal(
                    <CacheProvider value={parentEmotionCache}>
                        {menu}
                    </CacheProvider>,
                    parentContainer,
                );
            }
            return menu;
        },
        [parentContainer, parentEmotionCache],
    );

    const getRootMenuProps = React.useCallback(
        (
            onClose: () => void,
            options?: {
                minWidth?: number;
                maxWidth?: number;
            },
        ) => {
            const minWidth = options?.minWidth ?? 220;
            const maxWidth = options?.maxWidth ?? 440;
            return {
                open: Boolean(anchorEl),
                anchorEl,
                onClose,
                disablePortal: false,
                keepMounted: false,
                anchorOrigin: {
                    vertical: "bottom" as const,
                    horizontal: "left" as const,
                },
                transformOrigin: {
                    vertical: "top" as const,
                    horizontal: "left" as const,
                },
                container: parentContainer ?? undefined,
                slotProps: {
                    paper: {
                        css: css`
                            min-width: ${minWidth}px;
                            max-width: ${maxWidth}px;
                        `,
                    },
                },
            };
        },
        [anchorEl, parentContainer],
    );

    // This trick returns the closeMenu function as its cleanup, causing the menu to
    // be closed when the component using this hook is unmounted.
    React.useEffect(() => closeMenu, [closeMenu]);

    return {
        anchorEl,
        suppressTooltip,
        closeMenu,
        setMenuAnchor,
        prepareAnchorAtButton,
        openMenuAtButton,
        openMenuAtButtonWithItemsLoader,
        suppressTooltipUntilPointerReset,
        clearTooltipSuppression,
        releaseTooltipSuppressionIfMenuClosed,
        menuContainer: parentContainer ?? undefined,
        renderMenuInParentFrame,
        getRootMenuProps,
    };
}
