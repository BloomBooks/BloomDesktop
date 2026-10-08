import * as React from "react";
import Menu from "@mui/material/Menu";
import { Divider } from "@mui/material";
import { ThemeProvider } from "@mui/material/styles";
import { lightTheme } from "../../bloomMaterialUITheme";
import { LocalizableSelectableMenuItem } from "../../react_components/localizableMenuItem";
import {
    contextMenuCss,
    renderContextMenuItems,
} from "../js/canvasElementManager/canvasControlMenuRendering";
import { renderRoot } from "../../utils/reactRender";
import { canToggleNoIndent, isNoIndentOn, toggleNoIndent } from "./noIndent";
import {
    getTextContextMenuContent,
    ITextContextMenuContent,
} from "./textContextMenuContent";

// The context menu for a right-click on the text of an ordinary text box in the edit view
// (BL-16649). Text inside a canvas element gets CanvasElementContextControls' menu instead;
// see findParagraphForTextContextMenu.
//
// The menu is a controlled MUI Menu anchored at the mouse position, in the style of
// CanvasElementContextControls. Open state lives in renderTextContextMenu (below) rather
// than in the component, so that the component can be re-rendered for a new paragraph
// without carrying over stale state.
//
// The menu can hold two kinds of command. One acts on the paragraph that was clicked ("No
// Indent"). The other acts on inline (Word-style) images in the text box: Insert Image, or,
// when the click was on an image, the standard image menu with the same commands an image in a
// canvas element offers. getTextContextMenuContent decides which of these a given click gets.

// "No Indent" acts on one paragraph, so it is offered only when the right-click was inside a
// paragraph. A click on an inline image is not. isNoIndentOn and canToggleNoIndent both need a
// paragraph to answer, so without one the item is left out of the menu instead of being shown
// disabled.
const NoIndentMenuItem: React.FunctionComponent<{
    paragraph: HTMLElement;
    onDone: () => void;
}> = (props) => (
    <LocalizableSelectableMenuItem
        english="Do Not Indent This Paragraph"
        l10nId="EditTab.TextContextMenu.NoIndent"
        selected={isNoIndentOn(props.paragraph)}
        disabled={!canToggleNoIndent(props.paragraph)}
        onClick={() => {
            toggleNoIndent(props.paragraph);
            props.onDone();
        }}
    />
);

const TextContextMenu: React.FunctionComponent<{
    content: ITextContextMenuContent;
    open: boolean;
    setOpen: (open: boolean) => void;
    anchorPosition: { left: number; top: number };
}> = (props) => {
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
                css={contextMenuCss}
            >
                {props.content.paragraph && (
                    <NoIndentMenuItem
                        paragraph={props.content.paragraph}
                        onDone={() => props.setOpen(false)}
                    />
                )}
                {props.content.paragraph &&
                    props.content.inlineImageItems.length > 0 && (
                        <Divider variant="middle" component="li" />
                    )}
                {/* Each of these items closes the menu itself, by calling the closeMenu that
                    the content was built with (see setupTextContextMenu). The standard image
                    commands decide when to close it, because a command that opens a dialog has
                    to close the menu with the focus handling that dialogs need before its
                    dialog appears. */}
                {renderContextMenuItems(
                    props.content.inlineImageItems,
                    props.open,
                )}
            </Menu>
        </ThemeProvider>
    );
};

const kTextContextMenuRootId = "text-context-menu";

// Renders (or re-renders) the text context menu into a root div of the page document.
// renderRoot rather than the React 17 ReactDOM.render this was first written against: under
// React 18 a container may be given a Root only once, and this function deliberately
// re-renders the same container to drive the menu's open state. renderRoot caches the Root
// per container for exactly that. Its mount is asynchronous, which is fine here -- nothing
// reads the menu's DOM after the call.
function renderTextContextMenu(
    pageDocument: Document,
    content: ITextContextMenuContent,
    open: boolean,
    anchorPosition: { left: number; top: number },
) {
    let root = pageDocument.getElementById(kTextContextMenuRootId);
    if (!root) {
        root = pageDocument.createElement("div");
        root.setAttribute("id", kTextContextMenuRootId);
        // We don't have to worry about removing this before saving. Saving posts the whole
        // body (getBodyContentForSavePage), but the C# side keeps only the div.bloom-page out
        // of it, and this sits outside that -- as does the menu markup itself, which MUI
        // portals into the body while it is open.
        pageDocument.body.appendChild(root);
    }
    renderRoot(
        <TextContextMenu
            content={content}
            open={open}
            anchorPosition={anchorPosition}
            setOpen={(newOpen: boolean) =>
                renderTextContextMenu(
                    pageDocument,
                    content,
                    newOpen,
                    anchorPosition,
                )
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
        const anchorPosition = { left: event.clientX, top: event.clientY };
        // The menu items close the menu by calling closeMenu. It has to exist before the
        // content is built, because the content holds on to it, and it re-renders that same
        // content. So content is declared first and assigned after closeMenu is defined.
        let content: ITextContextMenuContent | undefined;
        const closeMenu = () => {
            if (content)
                renderTextContextMenu(document, content, false, anchorPosition);
        };
        content = getTextContextMenuContent(event.target, closeMenu);
        // When there is nothing to offer, leave the event alone so WebView2's own menu appears.
        if (!content) return;
        event.preventDefault();
        event.stopPropagation();
        renderTextContextMenu(document, content, true, anchorPosition);
    });
}
