import * as $ from "jquery";
import WebSocketManager from "../../utils/WebSocketManager";
import { postJson } from "../../utils/bloomApi";
import { showLinkGridSetupDialog } from "../../react_components/bookLinkSetup/BookGridSetupDialog";
import { Link } from "../../react_components/bookLinkSetup/BookLinkTypes";

export function editLinkGrid(linkGrid: HTMLElement) {
    console.log("editLinkGrid called with:", linkGrid);
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

    showLinkGridSetupDialog(
        currentLinks,
        // callback if they press OK
        (links: Link[]) => {
            linkGrid.innerHTML = "";

            if (links.length === 0) {
                // No books selected, show skeleton
                addSkeletonIfEmpty(linkGrid);
            } else {
                // Add real book buttons
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
                            "sending post for " +
                                desiredFileNameWithoutExtension,
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
                    p.textContent =
                        link.book.title || link.book.folderName || "";
                    button.appendChild(p);

                    linkGrid.appendChild(button);
                });
            }
        },
    );
}

export function addSkeletonIfEmpty(linkGrid: HTMLElement) {
    console.log("addSkeletonIfEmpty called", linkGrid);
    console.log("innerHTML before:", linkGrid.innerHTML);

    // Check if there are any actual book buttons (not skeleton ones)
    const hasRealButtons = $(linkGrid).find(".bloom-bookButton").length > 0;
    const hasSkeletons =
        $(linkGrid).find(".bloom-bookButton-skeleton").length > 0;

    console.log(
        "hasRealButtons:",
        hasRealButtons,
        "hasSkeletons:",
        hasSkeletons,
    );

    if (!hasRealButtons && !hasSkeletons) {
        console.log("Adding skeleton");
        // Create skeleton with 6 placeholder book buttons
        // Note: NOT using bloom-ui class because Cleanup() removes those
        const skeletonHtml = Array(6)
            .fill(null)
            .map(
                () =>
                    '<div class="bloom-bookButton-skeleton"><div class="skeleton-thumbnail"></div><div class="skeleton-label"></div></div>',
            )
            .join("");

        $(linkGrid).append(skeletonHtml);
        console.log("innerHTML after:", linkGrid.innerHTML);
    } else if (hasRealButtons && hasSkeletons) {
        console.log("Removing skeleton because real buttons exist");
        $(linkGrid).find(".bloom-bookButton-skeleton").remove();
    }
}
