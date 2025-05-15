import axios, { AxiosResponse, AxiosRequestConfig, AxiosError } from "axios";
import * as StackTrace from "stacktrace-js";
import { reportError, reportPreliminaryError } from "../lib/errorHandler";
import * as React from "react";
import { useEffect, useState } from "react";
import { useSubscribeToWebSocketForEvent } from "./WebSocketManager";

// You can modify mockReplies in order to work on UI components without the Bloom backend...
// namely, storybook.
// It's surely too fragile for use in unit tests.
// Mocks things that go through get(). That includes getString(), getBoolean(), useApiBoolean(), etc.
// Example:
// mockReplies["book/metadata"] = {
//     data: {
//         metadata: {
//             author: { ...

function isRunningStorybook(): boolean {
    try {
        if (window.location.href.startsWith("http://localhost:58886"))
            // storybook port is specified in package.json
            return true;
    } catch (e) {
        //ignore
    }
    return false;
}

export function getBloomApiPrefix(includeApi: boolean = true): string {
    const apiPart = includeApi ? "api/" : "";
    if (isRunningStorybook()) return `http://localhost:8089/bloom/${apiPart}`;

    return `/bloom/${apiPart}`;
}

export const mockReplies = {};

let pageIsClosing: boolean = false;

// This function is designed to be used like this:
// wrapAxios(axios.{get, post, etc}().then(...));
// That is, the argument should be an AxiosPromise;
// typically the promise returned by a .then() clause.
// Wrapping it in this function takes the place of writing
// your own .catch clause, and provides better error reporting
// if the server unexpectedly returns a failure result
// (or an exception is thrown in the .then code)
// You can also pass a get, post, etc. call without a .then().
//
// Returns a modified promise (the same promise you passed in, but with an error reporting catch included)
export async function wrapAxios<T>(
    call: Promise<void | AxiosResponse<T>>,
    report: boolean = true,
    url: string = "unknown"
): Promise<void | AxiosResponse<T>> {
    // Save the place where the original axios call was made.
    // The stack in the error passed to catch is usually not very
    // useful, containing just a few levels from the axios code
    // that handle the rejection of a promise, though it might contain
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
        if (pageIsClosing) {
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

        let msg: string = "Unexpected promise failure";
        if (url !== "unknown") {
            msg += " in " + url;
        }
        if (error && error.message && error.stack) {
            msg += ": " + error.message;
            if (error.response && error.response.statusText) {
                msg += ": (response: " + error.response.statusText + ")";
            }
        } else {
            msg += ": " + error;
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
export function NotifyPageClosing() {
    pageIsClosing = true;
}

export function getString(
    urlSuffix: string,
    successCallback: (value: string) => void
) {
    get(urlSuffix, result => {
        successCallback(result.data);
    });
}

export function getWithPromise(
    urlSuffix: string
): Promise<void | AxiosResponse<any>> {
    return wrapAxios(axios.get(getBloomApiPrefix() + urlSuffix));
}

// This method is used to get a result from Bloom.
export function get(
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
    wrapAxios(
        axios
            .get(getBloomApiPrefix() + urlSuffix)
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

// This method is used to get a result from Bloom.
export async function getAsync(urlSuffix: string) {
    return await axios.get(getBloomApiPrefix() + urlSuffix);
}

// A react hook for controlling an API-backed boolean from a React pure functional component
// Returns a tuple of [theCurrentValue, aFunctionForChangingTheValue(newValue)]
// When you call the returned function, two things happen: 1) we POST the value to the Bloom API
// and 2) we tell react that the value changed. It will then re-render the component;
// the component will call this again, but this time the tuple will contain the new value.
export function useApiBoolean(
    urlSuffix: string,
    defaultValue: boolean
): [boolean, (value: boolean) => void] {
    const [value, setValue] = useState(defaultValue);
    useEffect(() => {
        getBoolean(urlSuffix, c => {
            setValue(c);
        });
    }, []);

    const fn = (value: boolean) => {
        postBoolean(urlSuffix, value);
        setValue(value);
    };
    return [value, fn];
}

export function useCanModifyCurrentBook(): boolean {
    const [canModifyCurrentBook] = useApiBoolean(
        "common/canModifyCurrentBook",
        false
    );
    return canModifyCurrentBook;
}

export function useApiObject<T>(
    urlSuffix: string,
    defaultValue: T,
    // Set to true to skip making the API query and just return defaultValue.
    // This is useful when certain conditions are met where we don't need the data.
    skipQuery: boolean = false
): T {
    const [value, setValue] = useState<T>(defaultValue);
    useEffect(() => {
        if (skipQuery) {
            setValue(defaultValue);
        } else {
            get(urlSuffix, c => {
                if (typeof c.data === "string") {
                    setValue(JSON.parse(c.data as string));
                } else setValue(c.data);
            });
        }
    }, [urlSuffix, skipQuery, defaultValue]);
    return value;
}

export function useApiData<T>(urlSuffix: string, defaultValue: T): T {
    return useApiDataInternal(urlSuffix, defaultValue);
}

// Shared code for useApiData and useWatchApiData. If you are tempted to use it directly,
// you should probably be using useWatchApiData.
// The generation argument is a value that can be incremented to force redoing the main
// query, typically because an event we were watching for was sent from the server.
function useApiDataInternal<T>(
    urlSuffix: string,
    defaultValue: T,
    generation?: number
): T {
    const [value, setValue] = useState<T>(defaultValue);
    useEffect(() => {
        get(urlSuffix, c => {
            setValue(c.data);
        });
    }, [generation, urlSuffix]);
    return value;
}

// Conceptually returns the result of 'get(urlSuffix)'.
// When initially called, returns defaultValue, but a new render will be forced
// when the query returns the data.
// Also monitors our web socket for the specified event occurring in the specified
// context, and forces another render from a fresh call to 'get' when the
// event occurs.
export function useWatchApiData<T>(
    urlSuffix: string,
    defaultValue: T,
    clientContext: string,
    eventId: string
): T {
    const [generation, setGeneration] = useState(0);
    // Force a reload when the specified event happens.
    useSubscribeToWebSocketForEvent(clientContext, eventId, () => {
        setGeneration(old => old + 1);
    });
    return useApiDataInternal(urlSuffix, defaultValue, generation);
}

export function useWatchBooleanEvent(
    defaultValue: boolean,
    clientContext: string,
    eventId: string
) {
    const [val, setVal] = useState(defaultValue);
    useSubscribeToWebSocketForEvent(clientContext, eventId, data => {
        setVal(data.message === "true");
    });
    return val;
}

// Manages a state that is initially defaultValue, but subscribes to the specified web
// socket, and when it arrives, set the state to its message.
export function useWatchString(
    defaultValue: string,
    clientContext: string,
    eventId: string
): string {
    const [val, setVal] = useState(defaultValue);
    useSubscribeToWebSocketForEvent(clientContext, eventId, data => {
        setVal(data.message!);
    });
    // If we get re-rendered with a different defaultValue, update our result to that.
    useEffect(() => {
        if (val != defaultValue) {
            setVal(defaultValue);
        }
    }, [defaultValue]);
    return val;
}

export function useApiString(urlSuffix: string, defaultValue: string): string {
    return useApiData<string>(urlSuffix, defaultValue);
}

// Like UseApiData, except you also get a function for changing the state on the server.
export function useApiState<T>(
    urlSuffix: string,
    defaultValue: T
): [T, (value: T) => void] {
    const [value, setValue] = useState<T>(defaultValue);
    useEffect(() => {
        get(urlSuffix, c => {
            setValue(c.data);
        });
    }, []);

    const setFunction = (value: T) => {
        postData(urlSuffix, value);
        setValue(value);
    };
    return [value, setFunction];
}

// A vairiant of useApiState which returns a third value indicating whether we actually got the data yet.
// Possibly this could just replace useApiState, and clients that don't want the third argument could
// ignore it. But I'm not sure that there might not be an extra render resulting from setting the
// extra state variable, so I think it's worth having a separate function.
export function useApiStateWithStatus<T>(
    urlSuffix: string,
    defaultValue: T
): [T, (value: T) => void, boolean] {
    const [value, setValue] = useState<T>(defaultValue);
    const [gotit, setGotIt] = useState(false);
    useEffect(() => {
        get(urlSuffix, c => {
            setValue(c.data);
            setGotIt(true);
        });
    }, []);

    const setFunction = (value: T) => {
        postData(urlSuffix, value);
        setValue(value);
    };
    return [value, setFunction, gotit];
}

// Like useApiState, except it doesn't send the state back to the server when something changes.
// This is useful when you're going to wait for the user to click "OK" before sending all the
// state back.
export function useApiOneWayState<T>(
    urlSuffix: string,
    defaultValue: T
): [T, (value: T) => void] {
    const [value, setValue] = useState<T>(defaultValue);
    useEffect(() => {
        get(urlSuffix, c => {
            setValue(c.data);
        });
    }, []);
    return [value, setValue];
}

// A react hook for controlling an API-backed string from a React pure functional component
// Returns a tuple of [theCurrentValue, aFunctionForChangingTheValue(newValue)]
// When you call the returned function, two things happen: 1) we POST the value to the Bloom API
// and 2) we tell react that the value changed. It will then re-render the component;
// the component will call this again, but this time the tuple will contain the new value.
//
// The conditional parameter is optional.
// If defined, the string will be retrieved only if calling conditional() returns true.
export function useApiStringState(
    urlSuffix: string,
    defaultValue: string,
    conditional?: () => boolean
) {
    const generateSetStateWrapper = (
        setState: React.Dispatch<React.SetStateAction<string>>
    ) => {
        const setStateWrapper = (value: string) => {
            postString(urlSuffix, value);
            setState(value);
        };

        return setStateWrapper;
    };

    return useApiStringStateInternal(
        urlSuffix,
        defaultValue,
        conditional,
        generateSetStateWrapper
    );
}

/**
 * A react hook very much like useApiStringState, but with two timing-related differences:
 * 1) waits for the POST request to succeed before setting the value.
 * 2) The setter function returns a Promise so that the caller can wait until setting is fully complete.
 */
export function useApiStringStatePromise(
    urlSuffix: string,
    defaultValue: string,
    conditional?: () => boolean
) {
    const generateSetStateWrapper = (
        setState: React.Dispatch<React.SetStateAction<string>>
    ) => {
        // Note that this returns Promise<void> instead of void
        const setStateWrapper = async (value: string) => {
            await postString(urlSuffix, value);
            setState(value);
        };

        return setStateWrapper;
    };

    return useApiStringStateInternal(
        urlSuffix,
        defaultValue,
        conditional,
        generateSetStateWrapper
    );
}

/**
 * Internal helper method to handle shared code of useApiStringState and useApiStringStatePromise
 *
 * In addition to the shared parameters, this Internal function adds a generateSetStateWrapper parameter.
 * This param is a function which generates the "setStateWrapper" which wraps up the standard setState function from React.useHook with some other functionality.
 * The sole parameter to generateSetStateWrapperFunction is the underlying setState from React's useState hook.
 * The return value should be a function that can be used in place of the underlying setState from React's useState hook.
 */
function useApiStringStateInternal<T>(
    urlSuffix: string,
    defaultValue: string,
    conditional: (() => boolean) | undefined,
    generateSetStateWrapper: (
        setValue: React.Dispatch<React.SetStateAction<string>>
    ) => T
): [string, T] {
    const [value, setValue] = useState(defaultValue);
    useEffect(() => {
        if (!conditional || conditional()) {
            getString(urlSuffix, c => {
                setValue(c);
            });
        }
    }, []);

    const setterWrapperFunction = generateSetStateWrapper(setValue);
    return [value, setterWrapperFunction];
}

export function getBoolean(
    urlSuffix: string,
    successCallback: (value: boolean) => void
) {
    return get(urlSuffix, result => {
        successCallback(result.data);
    });
}

export function useApiJson(urlSuffix: string): [any | undefined] {
    const [value, setValue] = useState<any | undefined>();
    useEffect(() => {
        get(urlSuffix, c => {
            setValue(c.data);
        });
    }, []);
    return value;
}

// This method is used to get a result from Bloom, passing parameters to the nested axios call.
export function getWithConfig(
    urlSuffix: string,
    config: AxiosRequestConfig,
    successCallback: (r: AxiosResponse) => void
) {
    wrapAxios(
        axios.get(getBloomApiPrefix() + urlSuffix, config).then(successCallback)
    );
}

/**
 * Same as getWithConfig, but using Promise-based syntax instead of callbacks
 * The generic type <T> should be the type of the "data" field in a successful AxiosResponse.
 * Note: This function assumes the caller accurately provides <T>. No runtime validation is done to validate that data actually conforms to <T>
 */
export async function getWithConfigAsync<T>(
    urlSuffix: string,
    config: AxiosRequestConfig
) {
    return wrapAxios(axios.get<T>(getBloomApiPrefix() + urlSuffix, config));
}

export function postString(urlSuffix: string, value: string) {
    return wrapAxios(
        axios.post(getBloomApiPrefix() + urlSuffix, value, {
            headers: {
                "Content-Type": "text/plain"
            }
        })
    );
}

export function postBoolean(urlSuffix: string, value: boolean) {
    const data = adjustFalsyData(value);
    const url = getBloomApiPrefix() + urlSuffix;
    wrapAxios(
        axios.post(url, data, {
            headers: {
                "Content-Type": "application/json"
            }
        }),
        true,
        url
    );
}

// This method is used to post something to Bloom server.
export function post(
    urlSuffix: string,
    successCallback?: (r: AxiosResponse) => void,
    failureCallback?: (r: AxiosResponse) => void
) {
    if ((window as any).__karma__) {
        console.log(`skipping post to ${urlSuffix} because in unit tests`);
        if (successCallback) {
            successCallback({} as AxiosResponse); // A dummy AxiosResponse for compiling purposes. Unit tests should avoid using it.
        }
        return;
    }

    wrapAxios(
        axios
            .post(getBloomApiPrefix() + urlSuffix)
            .then(successCallback) // (no-ops if successCallback is undefined)
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
export function postThatMightNavigate(urlSuffix: string) {
    // The internal catch should suppress any errors. In case that fails (which it has), passing
    // false to wrapAxios further suppresses any error reporting.
    return wrapAxios(
        axios.post(getBloomApiPrefix() + urlSuffix).catch(),
        false
    );
}

// Post something to Bloom server with data.
export function postData(
    urlSuffix: string,
    data: any,
    successCallback?: (r: AxiosResponse) => void,
    errorCallback?: (r: AxiosResponse) => void
): Promise<void | AxiosResponse<any>> {
    data = adjustFalsyData(data);

    return wrapAxios(
        axios
            .post(getBloomApiPrefix() + urlSuffix, data)
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

// This method is used to post something from Bloom with data and params.
// You can do follow-up work either using the older callback pattern (successCallback/errorCallback)
//    or you can use the returned Promise to use the newer promise-chain or async/await patterns.
// [JT: I don't think this is entirely true, at least if you pass callbacks. You can await the result, but the calling code doesn't
// actually wait for the successCallback to complete. Use postDataWithConfigAsync if you need to await the
// result.]
export function postDataWithConfig(
    urlSuffix: string,
    data: any,
    config: AxiosRequestConfig,
    successCallback?: (r: AxiosResponse) => void,
    errorCallback?: (r: AxiosResponse) => void
): Promise<void | AxiosResponse<any>> {
    data = adjustFalsyData(data);
    return wrapAxios(
        axios
            .post(getBloomApiPrefix() + urlSuffix, data, config)
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

// This method is used to post something from Bloom with data and params.
// Follow-up work must use the async/await pattern.
export async function postDataWithConfigAsync(
    urlSuffix: string,
    data: any,
    config: AxiosRequestConfig
): Promise<void | AxiosResponse<any>> {
    data = adjustFalsyData(data);
    // Review: do we need something like wrapAxios here? I _think_ the browser is pretty
    // good about showing complete call stacks in async code, so I hope not.
    return await axios.post(getBloomApiPrefix() + urlSuffix, data, config);
}

// You can do follow-up work either using the older callback pattern (successCallback/errorCallback)
//    or you can use the returned Promise to use the newer promise-chain or async/await patterns.
// [JT: I don't think this is entirely true, at least if you pass callbacks. You can await the result, but the calling code doesn't
// actually wait for the successCallback to complete. Use postJsonAsync if you need to wait for the
// result.]
export function postJson(
    urlSuffix: string,
    data: any,
    successCallback?: (r: AxiosResponse) => void,
    errorCallback?: (r: AxiosResponse) => void
): Promise<void | AxiosResponse<any>> {
    data = adjustFalsyData(data);
    return postDataWithConfig(
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

export async function postJsonAsync(
    urlSuffix: string,
    data: any
): Promise<void | AxiosResponse<any>> {
    data = adjustFalsyData(data);
    return await postDataWithConfigAsync(urlSuffix, data, {
        headers: {
            "Content-Type": "application/json; charset=utf-8" // JSON normally uses UTF-8. Need to explicitly set it because UTF-8 is not the default.
        }
    });
}

let debugMessageCount = 0; // used to serialize debug messages
// This is useful for debugging TypeScript code, especially on Linux.  I wouldn't necessarily expect
// to see it used anywhere in code that gets submitted and merged.
export function postDebugMessage(message: string): void {
    ++debugMessageCount;
    postDataWithConfig(
        "common/debugMessage",
        debugMessageCount.toString() + "/ " + message,
        {
            headers: { "Content-Type": "text/plain" }
        }
    );
}

function adjustFalsyData(data: any): any {
    // Need to stringify FALSE value starting in axios 0.20. (See https://github.com/axios/axios/issues/3549)
    // Starting in axios 0.20.0, it was observed that 0 and false would cause an empty body to be sent, whereas previously they sent the value itself.
    // (This is because 0 and false are falsy, and axios now checks for whether data is falsy or not, whereas previously it checked whether data is undefined or not).
    // The GitHub issue says they plan to fix it (since the JSON spec says that it is OK to pass in false or a number (e.g. 0)),
    // so in the future this function may become unnecessary if it's fixed in a later version.
    return data === 0 || data === false ? JSON.stringify(data) : data;
}

window.addEventListener("beforeunload", () => NotifyPageClosing());
