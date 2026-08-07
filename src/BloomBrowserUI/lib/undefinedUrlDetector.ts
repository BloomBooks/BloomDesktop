// Catches the moment a piece of our front-end code puts a JavaScript value it doesn't actually
// have into a url.
//
// `element.src = someUndefinedVariable` doesn't fail: the DOM turns the value into the *text*
// "undefined" and asks the server for a file by that name. The server can only report that some
// file called "undefined" is missing, which tells us nothing about who asked for it - so these
// have sat in Sentry for years without ever being traceable to a line of code (BL-16666, split from BL-16577). We
// already fixed one instance the hard way (BL-16447, where the Adjust Timings dialog rendered
// before its audio url was ready); this exists so the next one names itself.
//
// The interception below is deliberately passive: it reports and then lets the assignment
// proceed exactly as before. What we need from it is the stack, and we get that either way, so
// there is no reason to change what the app does.
//
// Coverage, so nobody reads a silence here as proof of innocence: this patches the prototypes of
// *the window it is installed in*. Bloom's edit screen puts the book page and the toolbox in
// iframes, and elements created inside a frame are instances of that frame's own constructors. We
// are installed in every frame that runs one of our bundles, since each bundle's root module
// imports errorHandler - but parent-frame code that reaches into a child frame's document
// (`pageIframe.contentDocument.createElement("img").src = x`, a pattern bookEdit does use) is
// creating elements from the child's constructors while running in the parent, and is not seen.

/// The exact strings JavaScript produces when these values are turned into text. We match them
/// case-sensitively and only as a whole url or a whole path segment, so a real file that happens
/// to be called "Undefined.png", or a folder called "undefinedThings", is left alone.
const javascriptValueStrings = ["undefined", "null", "NaN"];

/**
 * True if this will become a url that is, or contains a path segment that is, the text form of a
 * JavaScript value. Nothing legitimately loads such a url, so a true here is always one of our bugs.
 *
 * Takes the value rather than a string on purpose. At an assignment like `img.src = x` the value
 * arrives here as the real `undefined`; it is the DOM that turns it into the text "undefined" a
 * moment later. By the time anyone could see a string it is already too late to know who did it.
 */
export function isJavascriptValueUrl(url: unknown): boolean {
    // These are exactly the values that stringify to the text we are looking for.
    if (url === undefined || url === null) return true;
    if (typeof url === "number") return Number.isNaN(url);
    // Anything else that isn't a string (a URL object, say) stringifies to something real.
    if (typeof url !== "string" || url.length === 0) return false;

    // Ignore any query string or fragment; only the path can name a file.
    const path = url.split(/[?#]/)[0];
    return path
        .split(/[/\\]/)
        .some((segment) => javascriptValueStrings.includes(segment));
}

/**
 * What we send to the server. Kept separate from the interception so it can be tested, and so the
 * message reads the same wherever it was caught.
 */
export function describeBadUrl(url: unknown, whereItWasSet: string): string {
    return (
        `A url was built from a JavaScript value that wasn't ready: ${whereItWasSet} was set to "${String(url)}". ` +
        `Bloom will now ask the server for a file by that name and fail. See BL-16666.`
    );
}

/**
 * Takes the Error rather than its stack text, so the caller can source-map it. A raw stack points
 * into a bundle, which is close to useless for a feature whose whole job is to name a line of our
 * source. errorHandler does that mapping, since it already owns it for window.onerror.
 */
type Reporter = (message: string, error: Error) => void;

let installed = false;

// Each report is an http post to the server, and the bug that triggers it is typically a render
// that repeats - the Adjust Timings dialog in BL-16447 would have fired on every open. We only
// need to learn about each site once, so report a given message once and stop altogether after a
// handful. Losing the repeat count costs us nothing: the point is to identify the code, and the
// server-side count in Sentry already tells us how often it happens.
const maxReports = 10;
const alreadyReported = new Set<string>();

/** Exported only so tests can start from a clean slate. */
export function resetReportingForTests(): void {
    alreadyReported.clear();
}

/**
 * Installs the interception. Safe to call more than once; only the first call does anything, so a
 * bundle that pulls in the bootstrap twice doesn't end up double-reporting.
 *
 * @param report how to get the problem back to us - errorHandler's reportError in real use.
 */
export function installUndefinedUrlDetector(report: Reporter): void {
    if (installed || typeof window === "undefined") return;
    installed = true;

    const check = (url: unknown, whereItWasSet: string) => {
        if (!isJavascriptValueUrl(url)) return;
        const message = describeBadUrl(url, whereItWasSet);
        if (alreadyReported.has(message) || alreadyReported.size >= maxReports)
            return;
        alreadyReported.add(message);
        // The stack of *this* call is the whole point: it names the line that built the url.
        report(message, new Error(message));
    };

    guardSrcProperty(window.HTMLImageElement, "an image's src", check);
    guardSrcProperty(window.HTMLMediaElement, "an audio/video src", check);
    guardSrcProperty(window.HTMLIFrameElement, "an iframe's src", check);
    guardSrcProperty(window.HTMLScriptElement, "a script's src", check);
    guardSetAttribute(check);
    guardFetch(check);
    guardXmlHttpRequest(check);
}

/**
 * Wraps the `src` property of one element type. We call the original setter afterwards, so the
 * element behaves exactly as it did before; we are only listening.
 */
function guardSrcProperty(
    elementType: { prototype: object } | undefined,
    description: string,
    check: (url: unknown, whereItWasSet: string) => void,
): void {
    // Not every environment has every element type (jsdom, for one), and a browser could in
    // principle define src without a setter. Nothing to wrap in that case.
    if (!elementType) return;
    const original = Object.getOwnPropertyDescriptor(
        elementType.prototype,
        "src",
    );
    if (!original || !original.set) return;

    Object.defineProperty(elementType.prototype, "src", {
        ...original,
        set(value: unknown) {
            check(value, description);
            original.set!.call(this, value);
        },
    });
}

/**
 * The other way an element's src gets set. Wrapping `setAttribute` on `Element` covers every
 * element type at once, including any we didn't wrap a `src` property for.
 */
function guardSetAttribute(
    check: (url: unknown, whereItWasSet: string) => void,
): void {
    const originalSetAttribute = Element.prototype.setAttribute;
    Element.prototype.setAttribute = function (
        name: string,
        value: string,
    ): void {
        // Only src: an <a href> of "undefined" is a dead link rather than a failed request, and
        // is not what we are hunting.
        if (name === "src") check(value, `setAttribute("src")`);
        originalSetAttribute.call(this, name, value);
    };
}

/**
 * The BL-16447 case came in through a library (WaveSurfer) fetching the url rather than putting it
 * on an element, so element interception alone would have missed it.
 */
function guardFetch(
    check: (url: unknown, whereItWasSet: string) => void,
): void {
    if (typeof window.fetch !== "function") return;
    const originalFetch = window.fetch;
    window.fetch = function (input: RequestInfo | URL, init?: RequestInit) {
        check(urlOfFetchInput(input), "a fetch()");
        // Call it on `window`, not on `this`. A bare `fetch(url)` inside a module gives us
        // `this === undefined`, and the browser rejects fetch invoked on anything that isn't the
        // window ("Illegal invocation") - which would break every fetch in Bloom.
        return originalFetch.call(window, input, init);
    };
}

/**
 * fetch() accepts a string, a URL, or a Request; pull the url out of whichever we were given so
 * the check sees the same thing the network will.
 */
function urlOfFetchInput(input: RequestInfo | URL): string | undefined {
    if (typeof input === "string") return input;
    if (input instanceof URL) return input.href;
    // A Request object
    if (input && typeof (input as Request).url === "string")
        return (input as Request).url;
    return undefined;
}

/**
 * Older code (and some libraries) still request through XMLHttpRequest rather than fetch, and
 * `open` is where the url is named, so that is what we wrap.
 */
function guardXmlHttpRequest(
    check: (url: unknown, whereItWasSet: string) => void,
): void {
    if (typeof XMLHttpRequest !== "function") return;
    const originalOpen = XMLHttpRequest.prototype.open;
    // Deliberately untyped rest args: open() has two overloads and we only care about the url.
    XMLHttpRequest.prototype.open = function (
        method: string,
        url: string | URL,
        ...rest: unknown[]
    ) {
        check(typeof url === "string" ? url : url?.href, "an XMLHttpRequest");
        return (originalOpen as (...args: unknown[]) => void).call(
            this,
            method,
            url,
            ...rest,
        );
    };
}
