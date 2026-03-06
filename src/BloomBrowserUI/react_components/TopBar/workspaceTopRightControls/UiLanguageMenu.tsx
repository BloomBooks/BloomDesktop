import * as React from "react";
import { ArrowDropDown } from "@mui/icons-material";
import { get, postJson, useApiString } from "../../../utils/bloomApi";
import { TopRightMenuButton, topRightMenuArrowCss } from "./TopRightMenuButton";
import { useL10n } from "../../l10nHooks";
import Menu from "@mui/material/Menu";
import MenuItem from "@mui/material/MenuItem";
import Checkbox from "@mui/material/Checkbox";
import Divider from "@mui/material/Divider";
import { useParentFrameMenuPortal } from "../useParentFrameMenuPortal";

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
    const {
        anchorEl,
        closeMenu,
        setMenuAnchor,
        prepareAnchorAtButton,
        menuContainer,
        renderMenuInParentFrame,
    } = useParentFrameMenuPortal();
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
        closeMenu();
    }, [closeMenu]);

    const onOpen = React.useCallback(() => {
        const preparedAnchor = prepareAnchorAtButton(
            "uiLanguageMenuButton",
            "ui-language-menu-parent",
        );
        if (!preparedAnchor) {
            return;
        }

        if (menuItems.length > 0) {
            setMenuAnchor(preparedAnchor);
            loadMenuItems();
            return;
        }

        loadMenuItems((itemCount) => {
            if (itemCount > 0) {
                setMenuAnchor(preparedAnchor);
            } else {
                closeMenu();
            }
        });
    }, [
        closeMenu,
        loadMenuItems,
        menuItems.length,
        prepareAnchorAtButton,
        setMenuAnchor,
    ]);

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
            container={menuContainer}
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
            {renderMenuInParentFrame(menu)}
        </>
    );
};
