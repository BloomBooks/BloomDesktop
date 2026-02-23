import * as React from "react";
import { BloomTooltip } from "../../BloomToolTip";
import { ArrowDropDown, HelpOutline } from "@mui/icons-material";
import { get, postJson, useApiString } from "../../../utils/bloomApi";
import { TopRightMenuButton, topRightMenuArrowCss } from "./TopRightMenuButton";
import { useL10n } from "../../l10nHooks";
import Menu from "@mui/material/Menu";
import MenuItem from "@mui/material/MenuItem";
import Divider from "@mui/material/Divider";
import createCache, { EmotionCache } from "@emotion/cache";
import { CacheProvider } from "@emotion/react";
import { createPortal } from "react-dom";

interface IMenuItem {
    id?: string;
    label?: string;
    enabled?: boolean;
    separator?: boolean;
}

const normalizeMenuItems = (items: unknown): IMenuItem[] => {
    if (!Array.isArray(items)) {
        return [];
    }

    return items.map((item) => {
        const menuItem = item as any;
        return {
            id:
                typeof menuItem.id === "string"
                    ? menuItem.id
                    : menuItem.id === undefined
                      ? undefined
                      : String(menuItem.id),
            label:
                typeof menuItem.label === "string"
                    ? menuItem.label
                    : menuItem.label === undefined
                      ? undefined
                      : String(menuItem.label),
            enabled: menuItem.enabled === undefined ? true : !!menuItem.enabled,
            separator: !!menuItem.separator,
        };
    });
};

export const HelpMenu: React.FunctionComponent = () => {
    const helpText = useL10n("?", "HelpMenu.Help Menu");

    const uiLanguage = useApiString("currentUiLanguage", "en");

    const showIconOnly =
        helpText === "?" || ["en", "fr", "de", "es"].includes(uiLanguage);

    const [anchorEl, setAnchorEl] = React.useState<HTMLElement | null>(null);
    const [parentContainer, setParentContainer] =
        React.useState<HTMLElement | null>(null);
    const [parentAnchorEl, setParentAnchorEl] =
        React.useState<HTMLElement | null>(null);
    const [parentEmotionCache, setParentEmotionCache] =
        React.useState<EmotionCache | null>(null);
    const [menuItems, setMenuItems] = React.useState<IMenuItem[]>([]);

    const loadMenuItems = React.useCallback(
        (onLoaded?: (itemCount: number) => void) => {
            get("workspace/topRight/helpMenu", (result) => {
                const items = normalizeMenuItems(result.data?.items);
                setMenuItems(items);
                onLoaded?.(items.length);
            });
        },
        [],
    );

    const onClose = React.useCallback(() => {
        setAnchorEl(null);
        if (parentAnchorEl) {
            parentAnchorEl.remove();
            setParentAnchorEl(null);
        }
        setParentContainer(null);
        setParentEmotionCache(null);
    }, [parentAnchorEl]);

    const onOpen = React.useCallback(() => {
        const buttonElement = document.getElementById("helpMenuButton");
        if (!buttonElement) {
            return;
        }

        const parentWindow = window.parent;
        const parentDocument = parentWindow?.document;
        if (!parentDocument || parentDocument === document) {
            setAnchorEl(buttonElement);
            loadMenuItems();
            return;
        }

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
            key: "help-menu-parent",
            container: parentDocument.head,
            prepend: true,
        });

        setParentContainer(parentDocument.body);
        setParentAnchorEl(parentAnchor);
        setParentEmotionCache(cache);

        if (menuItems.length > 0) {
            setAnchorEl(parentAnchor);
            loadMenuItems();
            return;
        }

        loadMenuItems((itemCount) => {
            if (itemCount > 0) {
                setAnchorEl(parentAnchor);
            } else {
                parentAnchor.remove();
                setParentAnchorEl(null);
                setParentContainer(null);
                setParentEmotionCache(null);
            }
        });
    }, [loadMenuItems, menuItems.length]);

    const handleMenuItemClick = React.useCallback(
        (item: IMenuItem) => {
            if (!item.id || item.enabled === false) {
                return;
            }
            postJson("workspace/topRight/helpMenuAction", { id: item.id });
            onClose();
        },
        [onClose],
    );

    const menu = (
        <Menu
            open={Boolean(anchorEl)}
            anchorEl={anchorEl}
            onClose={onClose}
            disablePortal={false}
            keepMounted={false}
            anchorOrigin={{ vertical: "bottom", horizontal: "left" }}
            transformOrigin={{ vertical: "top", horizontal: "left" }}
            container={parentContainer ?? undefined}
            slotProps={{
                paper: {
                    sx: {
                        minWidth: 220,
                        maxWidth: 440,
                    },
                },
            }}
        >
            {menuItems.map((item, index) => {
                if (item.separator) {
                    return <Divider key={`separator-${index}`} />;
                }

                return (
                    <MenuItem
                        key={`${item.id ?? item.label ?? index}`}
                        onClick={() => handleMenuItemClick(item)}
                        disabled={item.enabled === false}
                        dense
                    >
                        {item.label}
                    </MenuItem>
                );
            })}
        </Menu>
    );

    const button = (
        <TopRightMenuButton
            buttonId="helpMenuButton"
            text={showIconOnly ? "" : helpText}
            onClick={onOpen}
            startIcon={showIconOnly ? <HelpOutline /> : undefined}
            endIcon={<ArrowDropDown css={topRightMenuArrowCss} />}
            hasText={!showIconOnly}
        />
    );

    if (!showIconOnly) {
        return (
            <>
                {button}
                {parentContainer && parentEmotionCache
                    ? createPortal(
                          <CacheProvider value={parentEmotionCache}>
                              {menu}
                          </CacheProvider>,
                          parentContainer,
                      )
                    : menu}
            </>
        );
    }

    return (
        <>
            <BloomTooltip tip={{ l10nKey: "HelpMenu.Help Menu" }}>
                {button}
            </BloomTooltip>
            {parentContainer && parentEmotionCache
                ? createPortal(
                      <CacheProvider value={parentEmotionCache}>
                          {menu}
                      </CacheProvider>,
                      parentContainer,
                  )
                : menu}
        </>
    );
};
