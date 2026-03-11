import * as React from "react";
import { css } from "@emotion/react";
import { ArrowDropDown, Check } from "@mui/icons-material";
import {
    get,
    postJson,
    useApiBoolean,
    useApiString,
} from "../../../utils/bloomApi";
import { TopRightMenuButton, topRightMenuArrowCss } from "./TopRightMenuButton";
import { useL10n } from "../../l10nHooks";
import Menu from "@mui/material/Menu";
import Checkbox from "@mui/material/Checkbox";
import Divider from "@mui/material/Divider";
import { useParentFrameMenuPortal } from "../useParentFrameMenuPortal";
import { LocalizableMenuItem } from "../../localizableMenuItem";

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

const compactMenuItemCss = css`
    padding-top: 3px !important;
    padding-bottom: 3px !important;
    min-height: 32px;
`;

const compactLabelCss = css`
    font-size: 0.9rem;
    line-height: 1.3;
`;

const compactDividerCss = css`
    margin: 2px 0;
`;

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
        closeMenu,
        openMenuAtButtonWithItemsLoader,
        getRootMenuProps,
        renderMenuInParentFrame,
    } = useParentFrameMenuPortal();
    const [showUnapprovedTranslations, setShowUnapprovedTranslations] =
        useApiBoolean("workspace/showUnapprovedTranslations", false);
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
                    action: "helpTranslate",
                    label: helpTranslateText,
                    enabled: true,
                    checked: false,
                });
                items.push({ separator: true });
                items.push({
                    action: "toggleShowUnapprovedTranslations",
                    label: showUnapprovedTranslationsText,
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
        openMenuAtButtonWithItemsLoader(
            "uiLanguageMenuButton",
            "ui-language-menu-parent",
            menuItems.length,
            loadMenuItems,
        );
    }, [loadMenuItems, menuItems.length, openMenuAtButtonWithItemsLoader]);

    const handleMenuItemClick = React.useCallback(
        (item: IMenuItem) => {
            if (!item.action || item.enabled === false) {
                return;
            }

            if (item.action === "toggleShowUnapprovedTranslations") {
                setShowUnapprovedTranslations(!showUnapprovedTranslations);
                onClose();
                return;
            }

            postJson("workspace/uiLanguageAction", {
                action: item.action,
                languageName: item.languageName ?? null,
            });
            onClose();
        },
        [onClose, setShowUnapprovedTranslations, showUnapprovedTranslations],
    );

    const menu = (
        <Menu {...getRootMenuProps(onClose)}>
            {menuItems.map((item, index) => {
                if (item.separator) {
                    return (
                        <Divider
                            key={`separator-${index}`}
                            css={compactDividerCss}
                        />
                    );
                }

                return (
                    <LocalizableMenuItem
                        key={`${item.action ?? ""}:${item.languageName ?? ""}:${item.label ?? index}`}
                        english={item.label ?? ""}
                        l10nId={null}
                        onClick={() => handleMenuItemClick(item)}
                        disabled={item.enabled === false}
                        css={compactMenuItemCss}
                        labelCss={compactLabelCss}
                        variant="body2"
                        icon={
                            item.action === "setLanguage" ? (
                                item.checked ? (
                                    <Check
                                        css={css`
                                            padding: 0 6px 0 0;
                                        `}
                                    />
                                ) : undefined
                            ) : item.action ===
                              "toggleShowUnapprovedTranslations" ? (
                                <Checkbox
                                    checked={showUnapprovedTranslations}
                                    size="small"
                                    disableRipple
                                    css={css`
                                        padding: 0 6px 0 0;
                                    `}
                                />
                            ) : undefined
                        }
                    />
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
