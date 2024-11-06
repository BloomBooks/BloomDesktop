import { postString } from "./bloomApi";

const msghandler = (e: MessageEvent) => {
    const data = JSON.parse(e.data);
    if (!data || !data.messageType || !data.href) {
        return;
    }
    if (data.messageType === "bloomnav") {
        e.preventDefault();
        e.stopPropagation();
        postString("bloomnav", data.href);
    }
};

// Set up a window-level handler which will intercept any message with
// messageType "bloomnav" and send it to the bloomApi.
export function hookupBloomNavHandler() {
    // Removing it first ensures that we only have one, even if we call this more than once.
    window.removeEventListener("message", msghandler, { capture: true });
    window.addEventListener("message", msghandler, { capture: true });
}
