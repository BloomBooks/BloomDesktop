/// <reference path="../typings/jquery/jquery.d.ts" />
import * as $ from "jquery";
import * as StackTrace from "stacktrace-js";
import { BloomApi } from "../utils/bloomApi";

// This file implements custom Bloom global error handling.
// It should be imported by the root module in each bundle.

// This function is shared by code that wants to report errors but for some
// reason shouldn't do so by throwing.
export function reportError(message: string, stack: string) {
    BloomApi.postData("common/error", { message: message, stack: stack });
}

// This collects javascript exceptions not handled in a try...catch block and forwards them to the server.
// Catching them like this allows us to apply stacktrace-js to the stack, converting it
// from locations in our packed bundles to locations in the original source files.
// Using our own api to report the errors also makes us independent of GeckoFx's
// way of dealing with unhandled exceptions, and helps us distinguish thrown
// from unhandled ones, which some Gecko45 reporting doesn't.
window.onerror = function(msg, url, line, col, error) {
    StackTrace.fromError(error).then(stackframes => {
        var stringifiedStack = stackframes
            .map(function(sf) {
                return sf.toString();
            })
            .join("\n");
        reportError(msg.toString(), stringifiedStack);
    });
    return true; // suppress normal handling.
};

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
