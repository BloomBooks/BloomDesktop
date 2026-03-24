import { Divider } from "@mui/material";
import * as React from "react";
import { postString } from "../utils/bloomApi";
import {
    LocalizableCheckboxMenuItem,
    LocalizableMenuItem,
    LocalizableNestedMenuItem,
} from "../react_components/localizableMenuItem";

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
    requiresDeletePermission?: boolean;
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
    isBookDeletable: boolean,
    close: () => void,
    bookId: string,
    collectionId: string,
    tooltipIfCannotSaveBook?: string,
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
                    isBookDeletable,
                    close,
                    bookId,
                    collectionId,
                    tooltipIfCannotSaveBook,
                );
                return submenuItems.length ? (
                    <LocalizableNestedMenuItem
                        english={spec.label}
                        l10nId={spec.l10nId!}
                        l10nParam0={spec.l10nParam0}
                    >
                        {submenuItems}
                    </LocalizableNestedMenuItem>
                ) : undefined;
            }

            if (spec.hide && spec.hide()) {
                return undefined;
            }
            // If we have determined that a command should be shown, this logic determines whether it should be
            // disabled or not. Note that this only applies to the editable collection; commands that
            // don't apply to other collections are hidden. For example, the Delete command in the
            // downloads collection is always enabled, even though our code will currently not report
            // books in it as either savable or deletable.
            let disabled = false;
            if (isEditableCollection) {
                if (spec.requiresDeletePermission) {
                    disabled = !isBookDeletable;
                } else if (spec.requiresSavePermission) {
                    disabled = !isBookSavable;
                }
            }
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
                        collectionId,
                    )}`,
                    bookId,
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
        .filter((x) => x); // that is, remove ones where the map function returned undefined

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
                !isDivider(menuItemsT[index + 1]!)),
    );
};
