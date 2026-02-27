import WebSocketManager, {
    IBloomWebSocketEvent,
} from "../../utils/WebSocketManager";
import { postJson } from "../../utils/bloomApi";
import { showBookGridSetupDialog } from "../../react_components/BookGridSetup/BookGridSetupDialog";
import { Link } from "../../react_components/BookGridSetup/BookLinkTypes";

export function setupBookLinkGrids(container: HTMLElement) {
    // Add skeleton to empty grids on initial setup
    const linkGrids = Array.from(
        container.getElementsByClassName("bloom-link-grid"),
    ) as HTMLElement[];
    for (const linkGrid of linkGrids) {
        addSkeletonIfEmpty(linkGrid as HTMLElement);
        // In case anyone wonders why this works here but will not on most canvas elements
        // and their children...we put a rule in basepage.less that puts bloom-link-grid
        // just barely above the comical.js canvas, so it can receive mouse events.
        // In case you are tempted to do the same for other canvas elements, bear in mind
        // that the price paid is not being able to put things like comic tail handles over
        // such elements.
        linkGrid.ondblclick = function () {
            editLinkGrid(linkGrid);
        };
    }
}

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
                    thumbnail: `${window.location.origin}/bloom/api/collections/book/coverImage?book-id=${id}`,
                },
            };
        });

    showBookGridSetupDialog(
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
                    const setImgSrc = (evt: IBloomWebSocketEvent) => {
                        img.setAttribute("src", evt.message as string); // note, img.src = "foo" does something different!
                    };
                    WebSocketManager.notifyReady(messageContext, () => {
                        postJson("editView/addImageFromUrl", {
                            desiredFileNameWithoutExtension:
                                desiredFileNameWithoutExtension,
                            url: link.book.thumbnail,
                        });
                    });

                    WebSocketManager.once(messageContext, setImgSrc);

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
    // Check if there are any actual book buttons (not skeleton ones)
    const hasRealButtons =
        linkGrid.getElementsByClassName("bloom-bookButton").length > 0;
    const hasSkeletons =
        linkGrid.getElementsByClassName("bloom-bookButton-skeleton").length > 0;

    if (!hasRealButtons && !hasSkeletons) {
        // Create skeleton with 6 placeholder book buttons
        // Note: NOT using bloom-ui class because Cleanup() removes those
        for (let i = 0; i < 6; i++) {
            const skeletonDiv = document.createElement("div");
            skeletonDiv.className = "bloom-bookButton-skeleton";
            const thumbnailDiv = document.createElement("div");
            thumbnailDiv.className = "skeleton-thumbnail";
            const labelDiv = document.createElement("div");
            labelDiv.className = "skeleton-label";
            skeletonDiv.appendChild(thumbnailDiv);
            skeletonDiv.appendChild(labelDiv);
            linkGrid.appendChild(skeletonDiv);
        }
    } else if (hasRealButtons && hasSkeletons) {
        Array.from(
            linkGrid.getElementsByClassName("bloom-bookButton-skeleton"),
        ).forEach((x) => x.remove());
    }
}
