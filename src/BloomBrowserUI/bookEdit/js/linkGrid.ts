import * as $ from "jquery";
import WebSocketManager from "../../utils/WebSocketManager";
import { postJson } from "../../utils/bloomApi";
import { showLinkGridSetupsDialog } from "../bookLinkSetup/LinkGridSetupDialog";
import { Link } from "../bookLinkSetup/BookLinkTypes";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";

export function editLinkGrid(linkGrid: HTMLElement) {
    //If our listeners hear back, they should close, but if something goes wrong, we don't want them to pile up.
    WebSocketManager.closeSocketsWithPrefix("makeThumbnailFile-");

    const currentLinks: Link[] = Array.from(linkGrid.children)
        .filter(
            (child: Element) =>
                child.classList.contains("bloom-bookButton") &&
                child.hasAttribute("data-bloom-book-id"), // drop buttons that might have been edited by hand and don't have this
        )
        .map((button: Element) => {
            const id = button.getAttribute("data-bloom-book-id")!; // we already filtered out any that don't have this
            const titleElement = button.getElementsByTagName("p")[0]; // TODO should this be something computed from the Book by asking the server?
            return {
                book: {
                    id,
                    title: titleElement?.textContent || "",
                    // we don't know what the server path to this book would be at the moment
                    thumbnail: `${window.location.origin}/bloom/api/collections/book/thumbnail?book-id=${id}`,
                },
            };
        });

    showLinkGridSetupsDialog(
        currentLinks,
        // callback if they press OK
        (links: Link[]) => {
            linkGrid.innerHTML = "";
            addGridPromptIfEmpty(linkGrid);

            links.forEach((link) => {
                const button = document.createElement("div");
                button.className = "bloom-bookButton";
                button.setAttribute("data-href", `/book/${link.book.id}`);
                button.setAttribute("data-bloom-book-id", link.book.id);
                if (link.page) {
                    button.setAttribute(
                        "data-bloom-page-id",
                        link.page.pageId.toString(),
                    );
                }
                // create img for the thumbnail
                const img = document.createElement("img");
                button.appendChild(img);

                const desiredFileNameWithoutExtension = `bookButton-${link.book.id}`;

                const messageContext =
                    "makeThumbnailFile-" + desiredFileNameWithoutExtension;

                // listen for a websocket message that the image has been saved
                // and then update the src attribute
                const setImgSrc = () => {
                    console.log("got message " + messageContext);
                    img.setAttribute(
                        "src",
                        desiredFileNameWithoutExtension + ".png",
                    ); // note, img.src = "foo" does something different!
                };
                WebSocketManager.notifyReady(messageContext, () => {
                    console.log(
                        "sending post for " + desiredFileNameWithoutExtension,
                    );
                    postJson("editView/addImageFromUrl", {
                        desiredFileNameWithoutExtension:
                            desiredFileNameWithoutExtension,
                        url: link.book.thumbnail,
                    });
                });

                WebSocketManager.once(messageContext, setImgSrc);
                console.log("listening for ", messageContext);

                const p = document.createElement("p");
                p.textContent = link.book.title || link.book.folderName || "";
                button.appendChild(p);

                linkGrid.appendChild(button);
            });
        },
    );
}

function addGridPromptIfEmpty(linkGrid: HTMLElement) {
    theOneLocalizationManager
        .asyncGetText(
            "EditTab.ClickToEditBookGrid",
            "Click to edit book grid",
            "",
        )
        .done((clickBookGridPrompt: string) => {
            if (!$(linkGrid).find(".bloom-bookButton").length) {
                $(linkGrid).append(
                    `<p class='bloom-ui' id='edit-book-grid-prompt'>${clickBookGridPrompt}</p>`,
                );
            } else {
                $(linkGrid).find("#edit-book-grid-prompt").remove();
            }
        });
}
