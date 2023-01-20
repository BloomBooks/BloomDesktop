// React hooks cannot be used inside of a "class" component like BloomApi, so we've moved them out here.
import React = require("react");
import { useState } from "react";
import { useSubscribeToWebSocketForEvent } from "./WebSocketManager";
import { BloomApi } from "./bloomApi";

// List of exported React hooks:
// useApiData<T>(urlSuffix: string, defaultValue: T)
// useApiBoolean(urlSuffix: string, defaultValue: boolean)
// useCanModifyCurrentBook()
// useWatchApiData<T>(urlSuffix: string, defaultValue: T, clientContext: string, eventId: string)
// useWatchBooleanEvent(defaultValue: boolean, clientContext: string, eventId: string)
// useWatchString(defaultValue: string, clientContext: string, eventId: string)
// useApiString(urlSuffix: string, defaultValue: string)
// useApiState<T>(urlSuffix: string, defaultValue: T)
// useApiOneWayState<T>(urlSuffix: string, defaultValue: T)
// useApiStringState(urlSuffix: string, defaultValue: string, conditional?: () => boolean)
// useApiStringStatePromise(urlSuffix: string, defaultValue: string, conditional?: () => boolean)
// useApiJson(urlSuffix: string)

// Shared code for useApiData and useWatchApiData. If you are tempted to use it directly,
// you should probably be using useWatchApiData. (This one's not exported for a reason.)
// The generation argument is a value that can be incremented to force redoing the main
// query, typically because an event we were watching for was sent from the server.
function useApiDataInternal<T>(
    urlSuffix: string,
    defaultValue: T,
    generation?: number
): T {
    const [value, setValue] = React.useState<T>(defaultValue);
    React.useEffect(() => {
        BloomApi.get(urlSuffix, c => {
            setValue(c.data);
        });
    }, [generation, urlSuffix]);
    return value;
}

export function useApiData<T>(urlSuffix: string, defaultValue: T): T {
    return useApiDataInternal(urlSuffix, defaultValue);
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
    const [value, setValue] = React.useState(defaultValue);
    React.useEffect(() => {
        BloomApi.getBoolean(urlSuffix, c => {
            setValue(c);
        });
    }, [urlSuffix]);

    const fn = (value: boolean) => {
        BloomApi.postBoolean(urlSuffix, value);
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

// Conceptually returns the result of BloomApi.get(urlSuffix).
// When initially called, returns defaultValue, but a new render will be forced
// when the query returns the data.
// Also monitors our web socket for the specified event occurring in the specified
// context, and forces another render from a fresh call to BloomApi.get when the
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
        if (data.message) {
            setVal(data.message!);
        }
    });
    // If we get re-rendered with a different defaultValue, update our result to that.
    React.useEffect(() => {
        if (val != defaultValue) {
            setVal(defaultValue);
        }
    }, [defaultValue, val]);
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
    const [value, setValue] = React.useState<T>(defaultValue);
    React.useEffect(() => {
        BloomApi.get(urlSuffix, c => {
            setValue(c.data);
        });
    }, [urlSuffix]);

    const setFunction = (value: T) => {
        BloomApi.postData(urlSuffix, value);
        setValue(value);
    };
    return [value, setFunction];
}

// Like useApiState, except it doesn't send the state back to the server when something changes.
// This is useful when you're going to wait for the user to click "OK" before sending all the
// state back.
export function useApiOneWayState<T>(
    urlSuffix: string,
    defaultValue: T
): [T, (value: T) => void] {
    const [value, setValue] = React.useState<T>(defaultValue);
    React.useEffect(() => {
        BloomApi.get(urlSuffix, c => {
            setValue(c.data);
        });
    }, [urlSuffix]);
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
            BloomApi.postString(urlSuffix, value);
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
            await BloomApi.postString(urlSuffix, value);
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
    const [value, setValue] = React.useState(defaultValue);
    React.useEffect(() => {
        if (!conditional || conditional()) {
            BloomApi.getString(urlSuffix, c => {
                setValue(c);
            });
        }
    }, [conditional, urlSuffix]);

    const setterWrapperFunction = generateSetStateWrapper(setValue);
    return [value, setterWrapperFunction];
}

export function useApiJson(urlSuffix: string): [any | undefined] {
    const [value, setValue] = React.useState<any | undefined>();
    React.useEffect(() => {
        BloomApi.get(urlSuffix, c => {
            setValue(c.data);
        });
    }, [urlSuffix]);
    return value;
}
