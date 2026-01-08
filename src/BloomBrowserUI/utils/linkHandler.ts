import { postString } from "./bloomApi";

const handler = (e: Event) => {
    if (!(e.target instanceof Element)) {
        return;
    }
    const elt = e.target as Element;
    const anchor = elt.closest("a");
    if (!anchor) {
        return;
    }
    const href = anchor.getAttribute("href");
    if (!href) {
        return;
    }
    if (
        href.startsWith("http") ||
        href.startsWith("mailto") ||
        href.startsWith("file")
    ) {
        console.log("handling link ", href);
        e.preventDefault();
        e.stopPropagation();
        postString("link", href);
    }
};

// Set up a document-level handler which will intercept any click on an anchor
// with href http:, https:, or mailto:, and delegate it to a bloomApi (which in
// turn delegates it to the default system browser).
export function hookupLinkHandler() {
    // Removing it first ensures that we only have one, even if we call this more than once.
    document.removeEventListener("click", handler, { capture: true });
    document.addEventListener("click", handler, { capture: true });
}
