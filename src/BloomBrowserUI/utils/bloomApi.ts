import axios, {
    AxiosResponse,
    AxiosRequestConfig,
    AxiosPromise,
    AxiosError
} from "axios";
import * as StackTrace from "stacktrace-js";
import { reportError, reportPreliminaryError } from "../lib/errorHandler";
import React = require("react");

// You can modify mockReplies in order to work on UI components without the Bloom backed... namely, storybook.
// It's surely too fragile for use in unit tests.
// Mocks things that go through get(). That includes getString(), getBoolean(), useApiBoolean(), etc.
// Example:
// mockReplies["book/metadata"] = {
//     data: {
//         metadata: {
//             author: { ...

export let mockReplies = {};

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
    //
    // Returns a modified promise (the same promise you passed in, but with an error reporting catch included)
    public static async wrapAxios(
        call: Promise<void | AxiosResponse>,
        report: boolean = true
    ): Promise<void | AxiosResponse<any>> {
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
        const axiosCallState = new Error("dummy");
        return call.catch(error => {
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

            let msg: string;
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
                const stringifiedStackAxiosCall = stackframes
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
                        const stringifiedStackError = stackframes
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

    public static getString(
        urlSuffix: string,
        successCallback: (value: string) => void
    ) {
        BloomApi.get(urlSuffix, result => {
            successCallback(result.data);
        });
    }

    public static getWithPromise(
        urlSuffix: string
    ): Promise<void | AxiosResponse<any>> {
        return BloomApi.wrapAxios(axios.get(this.kBloomApiPrefix + urlSuffix));
    }

    // This method is used to get a result from Bloom.
    public static get(
        urlSuffix: string,
        successCallback: (r: AxiosResponse) => void,
        errorCallback?: (r: AxiosError) => void
    ) {
        if (mockReplies[urlSuffix]) {
            // like the "real thing", this is going to return and
            // then some time in the future will call the callback
            // (here, we're just saying do it asap)
            window.setTimeout(() => successCallback(mockReplies[urlSuffix]), 0);
        }
        BloomApi.wrapAxios(
            axios
                .get(this.kBloomApiPrefix + urlSuffix)
                .then(successCallback)
                .catch(r => {
                    if (errorCallback) {
                        errorCallback(r);
                    } else {
                        throw r;
                    }
                })
        );
    }

    // A react hook for controlling an API-backed boolean from a React pure functional component
    // Returns a tuple of [theCurrentValue, aFunctionForChangingTheValue(newValue)]
    // When you call the returned function, two things happen: 1) we POST the value to the Bloom API
    // and 2) we tell react that the value changed. It will then re-render the component;
    // the component will call this again, but this time the tuple will contain the new value.
    public static useApiBoolean(
        urlSuffix: string,
        defaultValue: boolean
    ): [boolean, (value: boolean) => void] {
        const [value, setValue] = React.useState(defaultValue);
        React.useEffect(() => {
            BloomApi.getBoolean(urlSuffix, c => {
                setValue(c);
            });
        }, []);

        const fn = (value: boolean) => {
            BloomApi.postBoolean(urlSuffix, value);
            setValue(value);
        };
        return [value, fn];
    }

    // A react hook for controlling an API-backed string from a React pure functional component
    // Returns a tuple of [theCurrentValue, aFunctionForChangingTheValue(newValue)]
    // When you call the returned function, two things happen: 1) we POST the value to the Bloom API
    // and 2) we tell react that the value changed. It will then re-render the component;
    // the component will call this again, but this time the tuple will contain the new value.
    //
    // The conditional parameter is optional.
    // If defined, the string will be retrieved only if calling conditional() returns true.
    public static useApiString(
        urlSuffix: string,
        defaultValue: string,
        conditional?: () => boolean
    ): [string, (value: string) => void] {
        const [value, setValue] = React.useState(defaultValue);
        React.useEffect(() => {
            if (!conditional || conditional()) {
                BloomApi.getString(urlSuffix, c => {
                    setValue(c);
                });
            }
        }, []);

        const fn = (value: string) => {
            BloomApi.postString(urlSuffix, value);
            setValue(value);
        };
        return [value, fn];
    }

    public static getBoolean(
        urlSuffix: string,
        successCallback: (value: boolean) => void
    ) {
        return BloomApi.get(urlSuffix, result => {
            successCallback(result.data);
        });
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

    public static postString(
        urlSuffix: string,
        value: string,
        successCallback?: (r: AxiosResponse) => void
    ) {
        BloomApi.wrapAxios(
            axios
                .post(this.kBloomApiPrefix + urlSuffix, value, {
                    headers: {
                        "Content-Type": "text/plain"
                    }
                })
                .then(successCallback ? successCallback : () => {})
        );
    }

    public static postBoolean(urlSuffix: string, value: boolean) {
        const data = this.adjustFalsyData(value);
        BloomApi.wrapAxios(
            axios.post(this.kBloomApiPrefix + urlSuffix, data, {
                headers: {
                    "Content-Type": "application/json"
                }
            })
        );
    }

    // This method is used to post something from Bloom.
    public static post(
        urlSuffix: string,
        successCallback?: (r: AxiosResponse) => void,
        failureCallback?: (r: AxiosResponse) => void
    ) {
        if ((window as any).__karma__) {
            console.log(`skipping post to ${urlSuffix} because in unit tests`);
            return;
        }

        BloomApi.wrapAxios(
            axios
                .post(this.kBloomApiPrefix + urlSuffix)
                .then(successCallback ? successCallback : () => {}) // do nothing on success if no callback
                .catch(
                    failureCallback
                        ? failureCallback
                        : // leave failure unhandled if no callback
                          r => {
                              throw r;
                          }
                )
        );
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
        successCallback?: (r: AxiosResponse) => void,
        errorCallback?: (r: AxiosResponse) => void
    ): Promise<void | AxiosResponse<any>> {
        data = this.adjustFalsyData(data);

        return BloomApi.wrapAxios(
            axios
                .post(this.kBloomApiPrefix + urlSuffix, data)
                .then(successCallback ? successCallback : () => {})
                .catch(r => {
                    if (errorCallback) {
                        errorCallback(r);
                    } else {
                        throw r;
                    }
                })
        );
    }

    // This method is used to post something from Bloom with data and params.
    // You can do follow-up work either using the older callback pattern (successCallback/errorCallback)
    //    or you can use the returned Promise to use the newer promise-chain or async/await patterns.
    public static postDataWithConfig(
        urlSuffix: string,
        data: any,
        config: AxiosRequestConfig,
        successCallback?: (r: AxiosResponse) => void,
        errorCallback?: (r: AxiosResponse) => void
    ): Promise<void | AxiosResponse<any>> {
        data = this.adjustFalsyData(data);
        return BloomApi.wrapAxios(
            axios
                .post(this.kBloomApiPrefix + urlSuffix, data, config)
                .then(
                    successCallback
                        ? successCallback
                        : r => {
                              return r; // Need to return the response (instead of having an empty body) so that the returned promise contains the AxiosResponse
                          }
                )
                .catch(r => {
                    if (errorCallback) {
                        errorCallback(r);
                    } else {
                        throw r;
                    }
                })
        );
    }

    // You can do follow-up work either using the older callback pattern (successCallback/errorCallback)
    //    or you can use the returned Promise to use the newer promise-chain or async/await patterns.
    public static postJson(
        urlSuffix: string,
        data: any,
        successCallback?: (r: AxiosResponse) => void,
        errorCallback?: (r: AxiosResponse) => void
    ): Promise<void | AxiosResponse<any>> {
        data = this.adjustFalsyData(data);
        return BloomApi.postDataWithConfig(
            urlSuffix,
            data,
            {
                headers: {
                    "Content-Type": "application/json; charset=utf-8" // JSON normally uses UTF-8. Need to explicitly set it because UTF-8 is not the default.
                }
            },
            successCallback,
            errorCallback
        );
    }

    private static debugMessageCount = 0; // used to serialize debug messages
    // This is useful for debugging TypeScript code, especially on Linux.  I wouldn't necessarily expect
    // to see it used anywhere in code that gets submitted and merged.
    public static postDebugMessage(message: string): void {
        ++this.debugMessageCount;
        BloomApi.postDataWithConfig(
            "common/debugMessage",
            this.debugMessageCount.toString() + "/ " + message,
            {
                headers: { "Content-Type": "text/plain" }
            }
        );
    }

    private static adjustFalsyData(data: any): any {
        // Need to stringify FALSE value starting in axios 0.20. (See https://github.com/axios/axios/issues/3549)
        // Starting in axios 0.20.0, it was observed that 0 and false would cause an empty body to be sent, whereas previously they sent the value itself.
        // (This is because 0 and false are falsy, and axios now checks for whether data is falsy or not, whereas previously it checked whether data is undefined or not).
        // The GitHub issue says they plan to fix it (since the JSON spec says that it is OK to pass in false or a number (e.g. 0)),
        // so in the future this function may become unnecessary if it's fixed in a later version.
        return data === 0 || data === false ? JSON.stringify(data) : data;
    }
}

window.addEventListener("beforeunload", () => BloomApi.NotifyPageClosing());
