import * as StackTrace from "stacktrace-js";
import Axios from "axios";

// This file implements custom Bloom global error handling.
// It should be imported by the root module in each bundle.

// This function is shared by code that wants to report errors but for some
// reason shouldn't do so by throwing.
export function reportError(message: string, stack: string | undefined) {
    const stackStr = stack || "";
    if (typeof window === "undefined") {
        // Running in Node.js test environment, just log to console
        console.error("Error: " + message);
        if (stackStr) console.error(stackStr);
        return;
    }
    const isTest =
        typeof process !== "undefined" && process.env.NODE_ENV === "test"
    if (isTest) {
        console.log(
            "skipping post to common/error because in unit tests: \r\n" +
                message +
                "\r\n" +
                stackStr,
        );
        return;
    }
    console.log("Posting to common/error " + message + " " + stackStr);
    // we don't want to use the error handling bloomapi wrapper here...
    // else we will recursively report errors about attempts to report errors
    Axios.post(
        "/bloom/api/common/error",
        // I think we should just be able to pass the object here, and it would automatically
        // be stringified and marked as JSON. But the server doesn't get the data.
        JSON.stringify({
            message: message,
            stack: stackStr,
        }),
        {
            headers: {
                "Content-Type": "application/json; charset=utf-8", // JSON normally uses UTF-8. Need to explicitly set it because UTF-8 is not the default.
            },
        },
    ).catch(() => {
        console.log("*****Got error trying report error");
    });
}

export function reportPreliminaryError(
    message: string,
    stack: string | null | undefined,
) {
    if (typeof window === "undefined") {
        // Running in Node.js test environment, just log to console
        console.error("Error: " + message);
        if (stack) console.error(stack);
        return;
    }
        const isTest =
        typeof process !== "undefined" && process.env.NODE_ENV === "test"
    if (isTest) {
        console.log(
            "skipping post to common/error because in unit tests: \r\n" +
                message +
                "\r\n" +
                stack,
        );
        return;
    }
    // we don't want to use the error handling bloomapi wrapper here...
    // else we will recursively report errors about attempts to report errors
    //    postData("common/preliminaryError", {
    Axios.post(
        "/bloom/api/common/preliminaryError",
        // I think we should just be able to pass the object here, and it would automatically
        // be stringified and marked as JSON. But the server doesn't get the data.
        JSON.stringify({
            message: message,
            stack: stack || "",
        }),
        {
            headers: {
                "Content-Type": "application/json; charset=utf-8", // JSON normally uses UTF-8. Need to explicitly set it because UTF-8 is not the default.
            },
        },
    ).catch(() => {
        console.log("*****Got error trying report preliminaryError");
    });
}

// Decides whether an otherwise-unhandled error message is a known, benign nuisance that
// should be logged and swallowed rather than reported to the user. Kept as a pure,
// exported function so it can be unit tested.
export function shouldIgnoreUnhandledError(message: string): boolean {
    if (
        message.includes(
            "ResizeObserver loop completed with undelivered notifications",
        )
    ) {
        // We've done some investigation of this error. It doesn't seem to come from our code,
        // but from something deep in React. It signifies (roughly) that changes made by one resize observer
        // changed the size of something causing another resize observer to fire at a higher
        // level in the document, and that handler is going to be postponed to the next animation frame.
        // It doesn't seem to be causing any harm, and ongoing reports of it are a nuisance.
        return true;
    }
    if (message.includes("[CKEDITOR.resourceManager.load] Resource name")) {
        // BL-16587: CKEditor loads its plugins (floatpanel, colorbutton, etc.) lazily by
        // injecting a <script> for each plugin's plugin.js. When the user switches tabs or
        // pages quickly, the edit page (and its editor instance) is torn down while such a
        // request is still in flight; the request gets cancelled, the script loader reports a
        // failure, and CKEditor's resourceManager throws (as a bare string)
        // '[CKEDITOR.resourceManager.load] Resource name "floatpanel" was not found at "...".'.
        // The editor that triggered the load is being discarded anyway, so this is harmless,
        // but the uncaught throw surfaces as an alarming error report. Swallow it. (The
        // accompanying "network name is no longer available" server log line is the same
        // teardown race seen from the C# side.)
        // We deliberately match any plugin name, not just floatpanel, since the teardown race
        // can cancel the load of any lazily-loaded plugin. This does mean a genuine plugin
        // failure (e.g. a plugin.js missing from a build) is also swallowed here; that is an
        // acceptable trade-off because such a failure is not a one-off teardown artifact but a
        // consistent, reproducible breakage that shows up as a visibly non-working toolbar
        // during normal editing, so it does not depend on this error report to be noticed.
        return true;
    }
    return false;
}

// This collects javascript exceptions not handled in a try...catch block and forwards them to the server.
// Catching them like this allows us to apply stacktrace-js to the stack, converting it
// from locations in our packed bundles to locations in the original source files.
// Using our own api to report the errors also makes us independent of GeckoFx's
// way of dealing with unhandled exceptions, and helps us distinguish thrown
// from unhandled ones, which some Gecko45 reporting doesn't.
if (typeof window !== "undefined") {
    window.onerror = (msg, url, line, col, error) => {
        const message = msg.toString();
        if (shouldIgnoreUnhandledError(message)) {
            console.error("Ignoring: " + message);
            return true; // suppress normal handling.
        }
        if (!error) {
            reportError(message, "(stack not available)");
            return true;
        }
        // Make a preliminary report, which will be discarded if the stack conversion succeeds.
        reportPreliminaryError(message, error.stack);
        // Try to make the report using source stack.
        StackTrace.fromError(error).then((stackframes) => {
            const stringifiedStack = stackframes
                .map((sf) => {
                    return sf.toString();
                })
                .join("\n");
            reportError(message, stringifiedStack);
        });
        return true; // suppress normal handling.
    };
}

// Saving this as it MIGHT be useful if we decide to have another go at catching
// unhandled promise rejections.
// With the current version of browser-unhandled-rejection, it reports axios calls
// where the server reports failure, even though they appear to have proper catch calls.

// Applies polyfill if necessary to window.Promise, so that we get notified of
// 'unhandled rejections', that is, exceptions and other problems that occur in promises
// without catch clauses. This allows the unhandledrejection event to be raised.
// Problem: triggers even for promises that DO have immediate catch clauses. Don't know why.
// Decided not to bother for now.
// import { auto as applyBrowserUnhandledRejectionPolyfill } from "browser-unhandled-rejection";
// applyBrowserUnhandledRejectionPolyfill();

// window.addEventListener("unhandledrejection", event => {
//     StackTrace.fromError((event as any).reason).then(stackframes => {
//         var stringifiedStack = stackframes
//             .map(function(sf) {
//                 return sf.toString();
//             })
//             .join("\n");
//         var msg = "An unhandled promise rejection occurred";
//         if (event && (event as any).reason && (event as any).reason.message) {
//             msg += ": " + (event as any).reason.message;
//         }
//         reportError(msg, stringifiedStack);
//     });
// });
