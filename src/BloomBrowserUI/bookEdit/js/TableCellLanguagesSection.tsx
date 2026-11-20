import { css } from "@emotion/react";
import * as React from "react";
import { useLayoutEffect, useRef, useState } from "react";
import Checkbox from "@mui/material/Checkbox";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import { useL10n } from "../../react_components/l10nHooks";
import {
    ICellLanguage,
    menuLanguages,
    RoleState,
    roleState,
    someGroupHasOneLanguage,
    toggleRole,
} from "./tableCellLanguages";

// The table menus' Languages item: a menu item that opens a submenu with one check box per
// language of the collection, for the text cells the menu acts on. It is drawn into the
// bloom-table library's own menus through setHostMenuSectionProvider, so the item copies the look
// of the library's menu items: 13px text, 6px 14px padding, a 22px icon gutter, and the same hover
// colour.
//
// The submenu is position: fixed but stays inside the library's popup in the DOM. Fixed lets it
// stand outside the popup, which clips what overflows it; staying inside means a press on a check
// box is not a press outside the menu, which would close it.

const kMenuGutterPx = 22;
const kHoverBackground = "#eef6f8";

const LanguageRow: React.FunctionComponent<{
    language: ICellLanguage;
    state: RoleState;
    // Unticking would leave some cell with no language.
    locked: boolean;
    onToggle: () => void;
}> = (props) => {
    const tooltip = useL10n(
        props.language.tooltipEnglish,
        props.language.tooltipL10nId,
    );
    const label = `${props.language.shortName} - ${props.language.name}`;
    return (
        <label
            title={tooltip}
            data-cell-language={props.language.role}
            data-cell-language-state={props.state}
            // Keep the focus in the cell the user was typing in; the menu's own controls do
            // the same.
            onMouseDown={(event) => event.preventDefault()}
            css={css`
                display: flex;
                align-items: center;
                padding: 0 14px 0 6px;
                white-space: nowrap;
                font-size: 13px;
                color: #222;
                cursor: ${props.locked ? "default" : "pointer"};
            `}
        >
            <Checkbox
                size="small"
                checked={props.state === "all"}
                indeterminate={props.state === "some"}
                disabled={props.locked}
                inputProps={{ "aria-label": label }}
                onChange={props.onToggle}
                css={css`
                    padding: 3px;
                    &.Mui-checked,
                    &.MuiCheckbox-indeterminate {
                        color: ${kBloomBlue};
                    }
                `}
            />
            {label}
        </label>
    );
};

export const TableCellLanguagesSection: React.FunctionComponent<{
    groups: HTMLElement[];
    languages: ICellLanguage[];
}> = (props) => {
    const label = useL10n("Languages", "Common.Languages");
    // The check boxes are read from the cells themselves, so after a change all that is needed
    // is to draw again.
    const [, noteChange] = useState(0);
    const [open, setOpen] = useState(false);
    const [submenuPlace, setSubmenuPlace] = useState<{
        left: number;
        top: number;
    }>();
    const item = useRef<HTMLDivElement>(null);
    const submenu = useRef<HTMLDivElement>(null);

    // A layout effect because the submenu's place depends on its own measured width (it opens to
    // the left when there is no room on the right), and it must be in place before it is painted.
    // It also closes the submenu if the library's menu scrolls: the menu scrolls when it is taller
    // than the window, which would carry the Languages item away from the fixed submenu, and a
    // person scrolling the menu is looking for another item anyway. (The page scrolling moves
    // neither, since the library's menu is fixed too.)
    useLayoutEffect(() => {
        if (!open || !item.current || !submenu.current) return;
        const itemRect = item.current.getBoundingClientRect();
        const width = submenu.current.offsetWidth;
        const view = item.current.ownerDocument.defaultView!;
        const roomOnRight = itemRect.right + width <= view.innerWidth;
        setSubmenuPlace({
            left: roomOnRight ? itemRect.right - 2 : itemRect.left - width + 2,
            top: itemRect.top - 4,
        });
        const menu = item.current.closest<HTMLElement>("[data-btable-menu]")!;
        const close = () => setOpen(false);
        menu.addEventListener("scroll", close);
        return () => menu.removeEventListener("scroll", close);
    }, [open]);

    const someCellHasOneLanguage = someGroupHasOneLanguage(
        props.groups,
        props.languages,
    );

    return (
        <div
            data-table-cell-languages=""
            onMouseEnter={() => setOpen(true)}
            onMouseLeave={() => setOpen(false)}
        >
            <div
                ref={item}
                role="menuitem"
                aria-haspopup="menu"
                aria-expanded={open}
                aria-label={label}
                onMouseDown={(event) => event.preventDefault()}
                onClick={() => setOpen(true)}
                css={css`
                    display: flex;
                    align-items: center;
                    padding: 6px 14px;
                    font-size: 13px;
                    color: #222;
                    cursor: pointer;
                    background: ${open ? kHoverBackground : "transparent"};
                `}
            >
                <span
                    css={css`
                        flex: 0 0 ${kMenuGutterPx}px;
                    `}
                />
                <span
                    css={css`
                        flex: 1 1 auto;
                    `}
                >
                    {label}
                </span>
                <span
                    aria-hidden="true"
                    css={css`
                        margin-left: 16px;
                        color: #666;
                    `}
                >
                    ▸
                </span>
            </div>
            {open && (
                <div
                    ref={submenu}
                    role="menu"
                    data-table-cell-languages-submenu=""
                    css={css`
                        position: fixed;
                        left: ${submenuPlace?.left ?? 0}px;
                        top: ${submenuPlace?.top ?? 0}px;
                        z-index: 1;
                        background: #fff;
                        border: 1px solid rgba(0, 0, 0, 0.15);
                        border-radius: 8px;
                        box-shadow: 0 4px 16px rgba(0, 0, 0, 0.25);
                        padding: 4px 0;
                    `}
                >
                    {menuLanguages(props.languages).map((language) => {
                        const state = roleState(
                            props.groups,
                            language.role,
                            props.languages,
                        );
                        return (
                            <LanguageRow
                                key={language.role}
                                language={language}
                                state={state}
                                locked={
                                    state === "all" && someCellHasOneLanguage
                                }
                                onToggle={() => {
                                    toggleRole(
                                        props.groups,
                                        language.role,
                                        props.languages,
                                    );
                                    noteChange((count) => count + 1);
                                }}
                            />
                        );
                    })}
                </div>
            )}
        </div>
    );
};
