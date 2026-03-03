import * as React from "react";
import { ArrowDropDown } from "@mui/icons-material";
import { get, postJson, useApiString } from "../../../utils/bloomApi";
import { TopRightMenuButton, topRightMenuArrowCss } from "./TopRightMenuButton";
import { useL10n } from "../../l10nHooks";
import Menu from "@mui/material/Menu";
import MenuItem from "@mui/material/MenuItem";
import Checkbox from "@mui/material/Checkbox";
import Divider from "@mui/material/Divider";
import createCache, { EmotionCache } from "@emotion/cache";
import { CacheProvider } from "@emotion/react";
import { createPortal } from "react-dom";

interface IMenuItem {
    action?:
        | "setLanguage"
        | "toggleShowUnapprovedTranslations"
        | "helpTranslate";
    languageName?: string;
    label?: string;
    enabled?: boolean;
    checked?: boolean;
    separator?: boolean;
}

const normalizeLanguageNames = (languageNames: unknown): string[] => {
    if (!Array.isArray(languageNames)) {
        return [];
    }

    return languageNames.map((languageName) => String(languageName));
};

export const UiLanguageMenu: React.FunctionComponent = () => {
    const label = useApiString("workspace/uiLanguageLabel", "");
    const showUnapprovedTranslationsText = useL10n(
        "Show translations which have not been approved yet",
        "CollectionTab.LanguageMenu.ShowUnapprovedTranslations",
    );
    const helpTranslateText = useL10n(
        "Help us translate Bloom (web)",
        "CollectionTab.UILanguageMenu.HelpTranslate",
    );
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
            get("workspace/uiLanguages", (result) => {
                const languageNames = normalizeLanguageNames(result.data);
                const items: IMenuItem[] = languageNames.map(
                    (languageName) => ({
                        action: "setLanguage",
                        languageName,
                        label: languageName,
                        enabled: true,
                        checked: languageName === label,
                    }),
                );
                items.push({ separator: true });
                items.push({
                    action: "toggleShowUnapprovedTranslations",
                    label: showUnapprovedTranslationsText,
                    enabled: true,
                    checked: false,
                });
                items.push({ separator: true });
                items.push({
                    action: "helpTranslate",
                    label: helpTranslateText,
                    enabled: true,
                    checked: false,
                });
                setMenuItems(items);
                onLoaded?.(items.length);
            });
        },
        [helpTranslateText, label, showUnapprovedTranslationsText],
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
        const buttonElement = document.getElementById("uiLanguageMenuButton");
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
            key: "ui-language-menu-parent",
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
            if (!item.action || item.enabled === false) {
                return;
            }
            postJson("workspace/uiLanguageAction", {
                action: item.action,
                languageName: item.languageName ?? null,
            });
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
                        key={`${item.action ?? ""}:${item.languageName ?? ""}:${item.label ?? index}`}
                        onClick={() => handleMenuItemClick(item)}
                        disabled={item.enabled === false}
                        dense
                    >
                        <Checkbox
                            checked={Boolean(item.checked)}
                            size="small"
                            disableRipple
                            sx={{ padding: "0 6px 0 0" }}
                        />
                        {item.label}
                    </MenuItem>
                );
            })}
        </Menu>
    );

    return (
        <>
            <TopRightMenuButton
                buttonId="uiLanguageMenuButton"
                text={label}
                onClick={onOpen}
                endIcon={<ArrowDropDown css={topRightMenuArrowCss} />}
            />
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
