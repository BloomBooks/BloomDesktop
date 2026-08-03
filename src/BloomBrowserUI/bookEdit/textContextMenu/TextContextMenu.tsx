import { css } from "@emotion/react";

import * as React from "react";
import * as ReactDOM from "react-dom";
import Menu from "@mui/material/Menu";
import { ThemeProvider } from "@mui/material/styles";
import { default as NoIndentIcon } from "@mui/icons-material/FormatIndentDecreaseSharp";
import { lightTheme } from "../../bloomMaterialUITheme";
import { LocalizableMenuItem } from "../../react_components/localizableMenuItem";
import {
    canToggleNoIndent,
    findParagraphForTextContextMenu,
    isNoIndentOn,
    toggleNoIndent,
} from "./noIndent";

// The context menu for a right-click on the text of an ordinary text box in the edit view
// (BL-16649). Text inside a canvas element gets CanvasElementContextControls' menu instead;
// see findParagraphForTextContextMenu.
//
// The menu is a controlled MUI Menu anchored at the mouse position, in the style of
// CanvasElementContextControls. Open state lives in renderTextContextMenu (below) rather
// than in the component, so that the component can be re-rendered for a new paragraph
// without carrying over stale state.
const TextContextMenu: React.FunctionComponent<{
    paragraph: HTMLElement;
    open: boolean;
    setOpen: (open: boolean) => void;
    anchorPosition: { left: number; top: number };
}> = (props) => {
    const noIndentIsOn = isNoIndentOn(props.paragraph);

    const handleNoIndentClick = () => {
        toggleNoIndent(props.paragraph);
        props.setOpen(false);
    };

    return (
        <ThemeProvider theme={lightTheme}>
            <Menu
                open={props.open}
                anchorReference="anchorPosition"
                anchorPosition={props.anchorPosition}
                onClose={() => props.setOpen(false)}
                // The page has its own focus concerns (the caret in the text we right-clicked on),
                // and we don't want opening the menu to disturb them.
                disableAutoFocus={true}
                disableEnforceFocus={true}
                css={css`
                    ul li {
                        color: #4d4d4d;
                        svg {
                            color: inherit !important;
                        }
                    }
                `}
            >
                <LocalizableMenuItem
                    english="No Indent"
                    l10nId="EditTab.TextContextMenu.NoIndent"
                    icon={<NoIndentIcon />}
                    checked={noIndentIsOn}
                    disabled={!canToggleNoIndent(props.paragraph)}
                    onClick={handleNoIndentClick}
                />
            </Menu>
        </ThemeProvider>
    );
};

const kTextContextMenuRootId = "text-context-menu";

// Renders (or re-renders) the text context menu into a root div of the page document.
function renderTextContextMenu(
    paragraph: HTMLElement,
    open: boolean,
    anchorPosition: { left: number; top: number },
) {
    const pageDocument = paragraph.ownerDocument;
    let root = pageDocument.getElementById(kTextContextMenuRootId);
    if (!root) {
        root = pageDocument.createElement("div");
        root.setAttribute("id", kTextContextMenuRootId);
        // We don't have to worry about removing this before saving because it is above the
        // level of the bloom-page, the only part of the body that gets saved.
        pageDocument.body.appendChild(root);
    }
    ReactDOM.render(
        <TextContextMenu
            paragraph={paragraph}
            open={open}
            anchorPosition={anchorPosition}
            setOpen={(newOpen: boolean) =>
                renderTextContextMenu(paragraph, newOpen, anchorPosition)
            }
        />,
        root,
    );
}

/**
 * Makes a right-click on the text of an ordinary text box put up our own context menu.
 * Call once per page document, from the edit-mode page setup.
 */
export function setupTextContextMenu(): void {
    document.addEventListener("contextmenu", (event: MouseEvent) => {
        // Ctrl+right-click is reserved for the WebView2 developer menu; see
        // WebView2Browser.ContextMenuRequested.
        if (event.ctrlKey) return;
        const paragraph = findParagraphForTextContextMenu(event.target);
        if (!paragraph) return;
        event.preventDefault();
        event.stopPropagation();
        renderTextContextMenu(paragraph, true, {
            left: event.clientX,
            top: event.clientY,
        });
    });
}
