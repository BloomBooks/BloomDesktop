import { callWhenFocusLost } from "../../../bookEdit/toolbox/toolbox";

export function openMenuAndCloseOnFocusLost(
    anchorElement: HTMLElement,
    setAnchorEl: (element: HTMLElement) => void,
    closeMenu: () => void,
): void {
    setAnchorEl(anchorElement);
    callWhenFocusLost(closeMenu);
}
