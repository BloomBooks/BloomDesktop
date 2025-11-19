import { useEffect } from "react";

export interface IBloomWebSocketEvent {
    clientContext: string;
    id: string;
    message?: string;
}
export interface IBloomWebSocketProgressEvent extends IBloomWebSocketEvent {
    progressKind?:
        | "Error"
        | "Warning"
        | "Progress"
        | "Note"
        | "Instruction"
        | "Heading"
        | "Fatal";
    percent?: number;
}

export function useWebSocketListener(
    clientContext: string,
    listener: (messageEvent: IBloomWebSocketEvent) => void,
) {
    useEffect(() => {
        WebSocketManager.addListener(clientContext, listener);
    }, []);
}

// avoid making this public, so that we can have more freedom to make changes to the signature
function useWebSocketListenerInnerWithMessage<T>(
    clientContext: string,
    eventId: string,
    listener: (T) => void,
    processEvent: (e: IBloomWebSocketEvent) => T,
    filter?: (e: IBloomWebSocketEvent) => boolean,
) {
    useEffect(() => {
        const l = (e: IBloomWebSocketEvent) => {
            if (e.id === eventId && (!filter || filter(e))) {
                listener(processEvent(e));
            }
        };
        WebSocketManager.addListener(clientContext, l);
        // Clean up when we are unmounted or this useEffect runs again (i.e. if the props.webSocketContext were to change)
        return () => WebSocketManager.removeListener(clientContext, l);
    }, []);
}

export function useSubscribeToWebSocketForEvent(
    clientContext: string,
    eventId: string,
    listener: (e: IBloomWebSocketEvent) => void,
    onlyCallListenerIfMessageIsTruthy?: boolean,
) {
    useWebSocketListenerInnerWithMessage<IBloomWebSocketEvent>(
        clientContext,
        eventId,
        listener,
        (e) => e,
        onlyCallListenerIfMessageIsTruthy ? (e) => !!e.message : (e) => true,
    );
}
export function useSubscribeToWebSocketForStringMessage(
    clientContext: string,
    eventId: string,
    listener: (message: string) => void,
) {
    useWebSocketListenerInnerWithMessage<string>(
        clientContext,
        eventId,
        listener,
        (e) => e.message!,
        (e) => !!e.message, // ignore if no message
    );
}

// Subscribe to an event and listen for the whole object bundle that the server sends. For an example of the c# server side of this, see HandleChooseFolder().
export function useSubscribeToWebSocketForObject<T>(
    clientContext: string,
    eventId: string,
    listener: (message: T) => void,
) {
    useEffect(() => {
        const websocketListener = (e) => {
            if (e.id === eventId) {
                listener(e as unknown as T);
            }
        };
        WebSocketManager.addListener(clientContext, websocketListener);
        return () => {
            WebSocketManager.removeListener(clientContext, websocketListener);
        };
    }, [clientContext]);
}

// Subscribe to an event where the "message" string is holding a JSON object
// which this will parse.
// Deprecated for general use: this makes sense for progress messages, but
// in general APIs should just use objects and not a "message" string.
export function useSubscribeToWebSocketForObjectInMessageParam<T>(
    clientContext: string,
    eventId: string,
    listener: (message: T) => void,
) {
    useWebSocketListenerInnerWithMessage<T>(
        clientContext,
        eventId,
        listener,
        (e) => JSON.parse(e.message!) as T,
        (e) => !!e.message, // ignore if no message
    );
}

// This class manages a websocket, currently at the WebSocketManager.socketMap level, currently with
// a fixed name. (Possible enhancement: support top WebSocketManager.socketMap level).
// You can add listeners (for "message") with addListener(),
// and close the socket with closeSocket().
export default class WebSocketManager {
    // map from clientContext to the real listener function created in getWebSocket and
    // attached to the socket.
    private static clientContextToDispatcherFunction: {
        [clientContext: string]: (event: MessageEvent) => void;
    } = {};

    // the dispatcher above will then cycle through these:
    // map from clientContext to functions to call when we get a message for that clientContext
    private static clientContextCallbacks: {
        [clientContext: string]: Array<(messageEvent: object) => void>;
    } = {};

    private static socketMap: {
        [clientContext: string]: WebSocket;
    } = {};

    // Currently calling this every two seconds. From my research, it appears that there
    // could be multiple reasons for a websocket to get disconnected, and it is good
    // practice to check regularly whether it needs to be reconnected if we are still
    // using it...which presumably we are, since we have a mechanism for closing them.
    // In particular, putting the computer to sleep seems to disconnect all web sockets
    // in WebView2 (BL-12329).
    // (If this throws some exception, it will be reported as usual for Javascript errors:
    // that is, in Alpha builds there is a toast, otherwise it just gets logged. Then we will continue
    // to attempt reconnection of any sockets that need it at regular intervals. If the same
    // exception is thrown repeatedly as a result, no harm is done, since we already have
    // code to suppress sequences of toasts with the same message. All but the first are ignored,
    // unless some other toast intervenes. If a different exception is thrown each time, or
    // in some alternation, an alpha build could get a lot of toasts; in that case, hopefully
    // the user reports them and we can fix whatever is causing them, and in the meantime the
    // alpha user will have to ignore them or go back to beta.)
    private static ReconnectClosedSockets() {
        const keys = Object.getOwnPropertyNames(this.socketMap);
        keys.forEach((clientContext) => {
            const socket: WebSocket = WebSocketManager.socketMap[clientContext];
            if (socket.readyState === WebSocket.CLOSED) {
                delete WebSocketManager.socketMap[clientContext]; // force re-creating
                // But note, we did not clear WebSocketManager.clientContextCallbacks[clientContext],
                // so we will keep the same callbacks.
                WebSocketManager.getOrCreateWebSocket(clientContext);
                console.log("re-created socket for " + clientContext);
            }
        });
    }

    private static startedCheckingForClosedSockets = false;

    private static continuouslyCheckForClosedSockets() {
        if (this.startedCheckingForClosedSockets) {
            return;
        }
        this.startedCheckingForClosedSockets = true;
        const looper = () => {
            // If ReconnectClosedSockets() throws an exception, it will be reported in the usual way
            // (toast with option to report, only in Alpha/Debug builds?). But hopefully it's
            // a one-time thing. By setting up the timeout for the next iteration before we do it,
            // we ensure that the next iteration will happen even if the current one throws.
            // Repeated occurrences of the same exception are ignored (by our Toast code).
            // Of course, if it throws something different every time, it will get annoying. But if there's some
            // situation where that happens, we want to know. And if we don't keep trying, we'll
            // be right back in the situation where, long after the exception has been forgotten,
            // the computer sleeps and then Bloom stops responding.
            setTimeout(looper, 2000);
            this.ReconnectClosedSockets();
        };
        // Could just call looper() here. But I'd rather wait for the first call to ReconnectClosedSockets()
        // until we've finished the current getOrCreateWebSocket(). In fact, I'm going to make a longer
        // timeout here...don't need the Reconnect code taking up time while Bloom is starting up, and if there
        // is some async stuff going on that might somehow let Reconnect interfere with the startup process,
        // this should help defend against it.
        setTimeout(looper, 10000);
    }
    /**
     *  In an attempt to make it easier to come to grips with some lifetime issues, we
     * are naming the websocket by a "clientContext" and making this private, so
     * that clients don't have direct access to the client.
     * Instead the client should call "addListener(clientContext)" and then when cleaning
     * up, call "closeSocket(clientContext)".
     */
    public static getOrCreateWebSocket(clientContext: string): WebSocket {
        this.continuouslyCheckForClosedSockets();
        if (!WebSocketManager.socketMap[clientContext]) {
            //currently we use a different port for this websocket, and it's the main port + 1
            const websocketPort = parseInt(window.location.port, 10) + 1;
            //here we're passing "socketName" in the "subprotocol" parameter, just for ease of identifying
            //sockets on the server side when debugging.

            // I tried a lot of error handling here: catching any exception in the constructor, attaching
            // onerror and onclose handlers to the new socket...and we still get notifications sent to the unhandled
            // error hook, when a connection is refused, as it is if the page we're trying to set up
            // a socket for has already been navigated away from. Apparently there's a browser spec that
            // says clients should purposely make it difficult for Javascript to explore what's going
            // wrong when a socket can't be opened. So instead those errors are suppressed
            // in Browser.ReportJavascriptError.
            const address = "ws://127.0.0.1:" + websocketPort.toString();
            // Note, the trailing slash is needed to not match on Chrome's "like Gecko)". I kid you not.
            const isGeckoFxOrFirefox =
                navigator.userAgent.toLowerCase().indexOf("gecko/") > -1;
            // Chrome doesn't seem to handle our websocket (provided by the Fleck project) if you specify a
            // subprotocol AND you don't tell Fleck about the protocol. Which is reasonable, but for some
            // reason not a problem with GeckoFx 60. Since we only use the subprotocol for debugging, we
            // just drop it if we're in not in geckofx or Firefox.

            const ws = new WebSocket(
                address,
                isGeckoFxOrFirefox ? clientContext : undefined,
            );

            WebSocketManager.socketMap[clientContext] = ws;

            if (!WebSocketManager.clientContextCallbacks[clientContext]) {
                WebSocketManager.clientContextCallbacks[clientContext] = [];
            }
            // the following is a refactored holdover from a situation where we were having trouble
            // getting the web ui to properly close its own listeners and socket, so we had to
            // revert to have c# send a message that would close this down. It may or may not be
            // used, but it's here if we need it. The preferred method is for the client UI to call closeSocket().
            const listener = (event: MessageEvent) => {
                const e: IBloomWebSocketEvent = JSON.parse(event.data);
                if (e.id === "websocketControl/close/" + clientContext) {
                    WebSocketManager.closeSocket(clientContext);
                } else if (e.clientContext === clientContext) {
                    WebSocketManager.clientContextCallbacks[
                        clientContext
                    ].forEach((callback) => callback(e));
                }
            };
            WebSocketManager.clientContextToDispatcherFunction[clientContext] =
                listener;
            WebSocketManager.socketMap[clientContext].addEventListener(
                "message",
                listener,
            );
        }
        return WebSocketManager.socketMap[clientContext];
    }

    /**
     * Disconnect all listeners and close the websocket.
     * @param {string} clientContext - should use the same name through the lifetime of the WebSocketManager.socketMap
     */
    public static closeSocket(clientContext: string): void {
        delete WebSocketManager.clientContextCallbacks[clientContext];
        const webSocket: WebSocket = WebSocketManager.socketMap[clientContext];
        if (webSocket) {
            webSocket.removeEventListener(
                "message",
                WebSocketManager.clientContextToDispatcherFunction[
                    clientContext
                ],
            );
            webSocket.close();
            delete WebSocketManager.socketMap[clientContext];
        }
    }

    /**
     * Clear out all sockets that start with the given prefix
     * @param {string} clientContext - should use the same name through the lifetime of the WebSocketManager.socketMap
     */
    public static closeSocketsWithPrefix(prefix: string) {
        const keys = Object.getOwnPropertyNames(this.socketMap);
        keys.forEach((clientContext) => {
            if (clientContext.startsWith(prefix)) {
                WebSocketManager.closeSocket(clientContext);
            }
        });
    }

    /**
     * Find or create a websocket and add a listener to it.
     * When a message is received on the socket whose data parses into an object whose clientContext property
     * is equal to the given clientContext, the parsed data object will be passed to the listener function.
     * @param {string} clientContext - should use the same name through the lifetime of the WebSocketManager.socketMap
     */
    public static addListener<T extends IBloomWebSocketEvent>(
        clientContext: string,
        listener: (messageEvent: T) => void,
        tagForDebugging?: string,
    ): void {
        // Skip WebSocket creation in test/mock environments
        if (
            clientContext.indexOf("mock_") > -1 ||
            (window as any)._SKIP_WEBSOCKET_CREATION_
        ) {
            // this is used in storybook stories and playwright tests when not in "with-bloom" mode.
            if (!WebSocketManager.clientContextCallbacks[clientContext])
                WebSocketManager.clientContextCallbacks[clientContext] = [];
        } else {
            WebSocketManager.getOrCreateWebSocket(clientContext); // side effect makes sure there's an array in listenerMap to push onto.
        }
        WebSocketManager.clientContextCallbacks[clientContext].push(listener);
        // console.log(
        //     `${tagForDebugging ?? ""} addListener(${clientContext})  now has ${
        //         WebSocketManager.clientContextCallbacks[clientContext]?.length
        //     } listeners.`
        // );
        const count =
            WebSocketManager.clientContextCallbacks[clientContext].length;

        // if clientContext is not one of the known ones that have lots of listeners, report it.
        const frequentlyListenedContexts = [
            // Each BookButton, for the moment at least, subscribes to these.
            "collections",
            "bookImage",
            "book", // via useWatchString
            "bookTeamCollectionStatus", // via useTColBookStatus
            // each book collection subscribes to this
            "editableCollectionList",
        ];

        // in the case of the progressDialog, we expect to have 2: one for the dialog, which is listening, and one for the ProgressBox.
        if (count > 2 && !frequentlyListenedContexts.includes(clientContext)) {
            console.error(
                `addListener sees that we have ${count} listeners on "${clientContext}". Normally if everything is disconnecting appropriately, these should not add up.`,
            );
        }
    }
    public static once<T extends IBloomWebSocketEvent>(
        clientContext: string,
        listener: (messageEvent: T) => void,
    ): void {
        const onceListener = (messageEvent: T) => {
            listener(messageEvent);
            WebSocketManager.removeListener(clientContext, onceListener);
        };
        WebSocketManager.addListener(clientContext, onceListener);
    }

    public static removeListener(
        clientContext: string,
        listener: (messageEvent: IBloomWebSocketEvent) => void,
    ): void {
        WebSocketManager.clientContextCallbacks[clientContext] =
            WebSocketManager.clientContextCallbacks[clientContext].filter(
                (l) => l !== listener,
            );
    }

    // useful for storybook stories to send messages
    public static mockSend<T extends IBloomWebSocketEvent>(
        clientContext: string,
        event: T,
    ) {
        WebSocketManager.clientContextCallbacks[clientContext].forEach(
            (listener) => listener(event),
        );
    }

    /**
     * Find or create a websocket (often used after addEventListener, in which case it is sure to
     * be found), and request a one-time notification when the socket is open, that is, able to
     * receive (and send) messages. If the socket is already open, onReady() will be called at
     * once, before the call to this method returns.
     */
    public static notifyReady(clientContext: string, onReady: () => void) {
        const socket = WebSocketManager.getOrCreateWebSocket(clientContext);
        console.log(
            "WebSocketManager:notifyReady. readyState = " +
                socket.readyState.toString(),
        );
        if (socket.readyState === 0) {
            // CONNECTING
            const openFunc = () => {
                socket.removeEventListener("open", openFunc);
                onReady();
            };
            //alert("waiting for open");
            socket.addEventListener("open", openFunc);
        } else {
            // presume state OPEN; out of luck if already closed or closing
            // Since it's already open we can call the function now.
            onReady();
        }
    }
}
