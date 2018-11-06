import axios, { AxiosResponse, AxiosRequestConfig, AxiosPromise } from "axios";
import * as StackTrace from "stacktrace-js";
import { reportError, reportPreliminaryError } from "../lib/errorHandler";

export class BloomApi {
    private static kBloomApiPrefix = "/bloom/api/";
    private static pageIsClosing: boolean = false;
    // This function is designed to be used lilke this:
    // BloomApi.wrapAxios(axios.{get, post, etc}().then(...));
    // That is, the argument should be an AxiosPromise;
    // typically the promise returned by a .then() clause.
    // Wrapping it in this function takes the place of writing
    // your own .catch clause, and provides better error reporting
    // if the server unexpectedly returns a failure result
    // (or an exception is thrown in the .then code)
    // You can also pass a get, post, etc. call without a .then().
    public static wrapAxios(
        call: AxiosPromise | Promise<void>,
        report: boolean = true
    ) {
        // Save the place where the original axios call was made.
        // The stack in the error passed to catch is usually not very
        // useful, containing just a few levels from the axios code
        // that handle the rejection of a promise, thoug it might contain
        // useful information about a problem within the '.then' block.
        // The stack that we would get by throwing in the catch function
        // itself is even less useful, typically containing nothing but
        // this method. But the one we can make where this function is called
        // really tells us where the problem is. Unfortunately, to be able
        // to report it, we have to save it even if we don't need it.
        // But fortunately, axios calls tend not to be used for very
        // performance-critical calls.
        var axiosCallState = new Error("dummy");
        call.catch(error => {
            if (!report) {
                return;
            }
            if (BloomApi.pageIsClosing) {
                return;
            }
            // We want to report a two-part stack: the one from axiosCallState
            // showing where the request came from, and the one from our
            // error argument, which may sometimes show where the code in a
            // 'then' clause failed, though more often it is just internal
            // axios code. To get a combined stack and report it the way we
            // want we need to map the stacks to the original source code
            // separately.
            // (throwing an exception here bypasses our window.onerror function
            // altogether, for some unknown reason; calling window.onerror with a composed stack
            // defeats its stack mapping.)

            var msg: string;
            if (error && error.message && error.stack) {
                msg = "Unexpected promise failure: " + error.message;
                if (error.response && error.response.statusText) {
                    msg += " (response: " + error.response.statusText + ")";
                }
            } else {
                msg = "Unexpected promise failure: " + error;
            }

            // First we make a preliminary report that doesn't involve going to the server for source maps.
            // See the full report code below for comments. This should be the same report as generated
            // by the main report code below, except for not converting the stacks.
            if (error && error.message && error.stack) {
                reportPreliminaryError(
                    msg,
                    axiosCallState.stack + "\ninner exception\n" + error.stack
                );
            } else {
                reportPreliminaryError(msg, axiosCallState.stack);
            }

            // If all goes well this better report will replace it:
            StackTrace.fromError(axiosCallState).then(stackframes => {
                var stringifiedStackAxiosCall = stackframes
                    .map(sf => {
                        return sf.toString();
                    })
                    .join("\n");
                // The error object we get from Axios is typically a Javascript
                // Error (with message and stack), and often also has a response
                // property, though we code defensively against the possibility
                // that it does not.
                if (error && error.message && error.stack) {
                    StackTrace.fromError(error).then(stackframes => {
                        var stringifiedStackError = stackframes
                            .map(sf => {
                                return sf.toString();
                            })
                            .join("\n");
                        reportError(
                            msg,
                            stringifiedStackAxiosCall +
                                "\ninner exception\n" +
                                stringifiedStackError
                        );
                    });
                } else {
                    // don't know what error is, can't get a stack from it, just include
                    // whatever JavaScript can make of it in the report, along with the
                    // main stack.
                    reportError(msg, stringifiedStackAxiosCall);
                }
            });
        });
    }

    // This is called when a page starts to shut down. Attempts at sending to
    // the server after this tend to fail unpredictably. Give up on reporting errors.
    public static NotifyPageClosing() {
        BloomApi.pageIsClosing = true;
    }

    // This method is used to get a result from Bloom.
    public static get(
        urlSuffix: string,
        successCallback: (r: AxiosResponse) => void
    ) {
        BloomApi.wrapAxios(
            axios.get(this.kBloomApiPrefix + urlSuffix).then(successCallback)
        );
    }

    // This method is used to get a result from Bloom, passing paramaters to the nested axios call.
    public static getWithConfig(
        urlSuffix: string,
        config: AxiosRequestConfig,
        successCallback: (r: AxiosResponse) => void
    ) {
        BloomApi.wrapAxios(
            axios
                .get(this.kBloomApiPrefix + urlSuffix, config)
                .then(successCallback)
        );
    }

    // This method is used to post something from Bloom.
    public static post(
        urlSuffix: string,
        successCallback?: (r: AxiosResponse) => void
    ) {
        if ((window as any).__karma__) {
            console.log(`skipping post to ${urlSuffix} because in unit tests`);
            return;
        }

        if (successCallback) {
            BloomApi.wrapAxios(
                axios
                    .post(this.kBloomApiPrefix + urlSuffix)
                    .then(successCallback)
            );
        } else {
            BloomApi.wrapAxios(axios.post(this.kBloomApiPrefix + urlSuffix));
        }
    }

    // Do a post (like duplicate page) that might result in navigating the browser
    // containing the code that calls post. This messes up the network connection
    // and results in spurious network errors being reported.
    // Note that this method deliberately doesn't have a successCallback.
    // We are suppressing ALL exceptions from the post.
    // If we one day need to do this with a callback, we will need to think very
    // hard about possible exceptions during the callback (and the possibility
    // that the callback is somehow messed up by the page reloading).
    public static postThatMightNavigate(urlSuffix: string) {
        // The internal catch should suppress any errors. In case that fails (which it has), passing
        // false to wrapAxios further suppresses any error reporting.
        BloomApi.wrapAxios(
            axios.post(this.kBloomApiPrefix + urlSuffix).catch(),
            false
        );
    }

    // This method is used to post something from Bloom with data.
    public static postData(
        urlSuffix: string,
        data: any,
        successCallback?: (r: AxiosResponse) => void
    ) {
        if (successCallback) {
            BloomApi.wrapAxios(
                axios
                    .post(this.kBloomApiPrefix + urlSuffix, data)
                    .then(successCallback)
            );
        } else {
            BloomApi.wrapAxios(
                axios.post(this.kBloomApiPrefix + urlSuffix, data)
            );
        }
    }

    // This method is used to post something from Bloom with data and params.
    public static postDataWithConfig(
        urlSuffix: string,
        data: any,
        config: AxiosRequestConfig,
        successCallback?: (r: AxiosResponse) => void
    ) {
        if (successCallback) {
            BloomApi.wrapAxios(
                axios
                    .post(this.kBloomApiPrefix + urlSuffix, data, config)
                    .then(successCallback)
            );
        } else {
            BloomApi.wrapAxios(
                axios.post(this.kBloomApiPrefix + urlSuffix, data, config)
            );
        }
    }

    public static postJson(
        urlSuffix: string,
        data: any,
        successCallback?: (r: AxiosResponse) => void
    ) {
        BloomApi.postDataWithConfig(
            urlSuffix,
            data,
            {
                headers: { "Content-Type": "application/json" }
            },
            successCallback
        );
    }
}

window.addEventListener("beforeunload", () => BloomApi.NotifyPageClosing());
