import { jsx, css } from "@emotion/react";
import { Menu } from "@mui/material";
import * as React from "react";

//TODO finish and use in bookbutton and publish tab

export const ContextMenu: React.FunctionComponent<{
    items: JSX.Element[];
    children: React.ReactNode;
    onContextClick: (event: React.MouseEvent<HTMLElement>) => void;
}> = props => {
    const [contextMousePoint, setContextMousePoint] = React.useState<
        | {
              mouseX: number;
              mouseY: number;
          }
        | undefined
    >();

    const handleClose = () => {
        setContextMousePoint(undefined);
    };

    // todo why?
    // Given the actual point the user clicked, set our state variable to a slightly adjusted point
    // where we want the popup menu to appear.
    const setAdjustedContextMenuPoint = (x: number, y: number) => {
        setContextMousePoint({
            mouseX: x - 2,
            mouseY: y - 4
        });
    };

    const handleContextClick = (event: React.MouseEvent<HTMLElement>) => {
        setAdjustedContextMenuPoint(event.clientX, event.clientY);
        props.onContextClick(event);
    };

    return (
        <div onContextMenu={e => handleContextClick(e)}>
            {/* // google how to add a custom property on multiple elemnts */}
            {props.children}
            {contextMousePoint && props.items.length > 0 && (
                <Menu
                    keepMounted={true}
                    open={!!contextMousePoint}
                    onClose={handleClose}
                    anchorReference="anchorPosition"
                    anchorPosition={{
                        top: contextMousePoint!.mouseY,
                        left: contextMousePoint!.mouseX
                    }}
                >
                    {props.items}
                </Menu>
            )}
        </div>
    );
};

// TODO for PublishTab.tsx:

// const menuItems = () => {
// 	return (
//         <LocalizableMenuItem
// 				key={"EditTab.BookContextMenu.openHtmlInBrowser"}
// 				english={
// 					"Open the HTML used to make this PDF, in the default system browser"
// 				}
// 				l10nId={"EditTab.BookContextMenu.openHtmlInBrowser"}
// 				onClick={
// 					() => {
// 						post("publish/openInBrowser");
// 					} // todo should these even be a post?
// 				}
// 				addEllipsis={false}
// 				requiresAnyEnterprise={false}
// 				disabled={!canDownloadPDF}
// 			></LocalizableMenuItem>
// 			<LocalizableMenuItem
// 				key={"PublishTab.OpenThePDFInTheSystemPDFViewer"}
// 				english={"Open the PDF in the default system PDF viewer"}
// 				l10nId={"PublishTab.OpenThePDFInTheSystemPDFViewer"}
// 				onClick={() => {
// 					post("publish/openPdf");
// 				}}
// 				addEllipsis={false}
// 				requiresAnyEnterprise={false}
// 				disabled={!canDownloadPDF} // see publishview UpdateDisplay
// 			></LocalizableMenuItem>
// 			<LocalizableMenuItem
// 				key={"exportAudioFiles1PerPageToolStripMenuItem"}
// 				english={"Export audio files, 1 per page"}
// 				l10nId={"exportAudioFiles1PerPageToolStripMenuItem"} // TODO mark not localizable
// 				onClick={() => {
// 					post("publish/exportAudioFiles1PerPageToolStrip");
// 				}}
// 				addEllipsis={false}
// 				requiresAnyEnterprise={false}
// 				disabled={false}
// 			></LocalizableMenuItem>
// 	);
// };
