import { BloomApi } from "./bloomApi";
import { useEffect } from "react";

export interface IBloomWebSocketEvent {
    clientContext: string;
    id: string;
    message?: string;
}
export interface IBloomWebSocketProgressEvent extends IBloomWebSocketEvent {
    progressKind?: "Error" | "Warning" | "Progress" | "Note" | "Instruction";
}

export function useWebSocketListener(
    clientContext: string,
    listener: (messageEvent: IBloomWebSocketEvent) => void
) {
    useEffect(() => {
        WebSocketManager.addListener(clientContext, listener);
    }, []);
}

// avoid making this public, so that we can have more freedom to make changes to the signature
function useWebSocketListenerInner<T>(
    clientContext: string,
    eventId: string,
    listener: (T) => void,
    processEvent: (e: IBloomWebSocketEvent) => T,
    filter?: (e: IBloomWebSocketEvent) => boolean
) {
    useEffect(() => {
        const l = (e: IBloomWebSocketEvent) => {
            if (e.id === eventId && (!filter || filter(e))) {
                listener(processEvent(e));
            }
        };
        WebSocketManager.addListener(clientContext, l);
        // Clean up when we are unmounted or this useEffect runs again (i.e. if the props.webSocketContext were to change)
        // TODO: we want this, but it breaks ReaderPublishScreen, maybe
        // because the progress dialog closes and something disconnects and then
        // the preview screen doesn't get the message that we're done.
        //return WebSocketManager.removeListener(clientContext, l);
    }, []);
}

export function useSubscribeToWebSocketForEvent(
    clientContext: string,
    eventId: string,
    listener: (e: IBloomWebSocketEvent) => void,
    onlyCallListenerIfMessageIsTruthy?: boolean
) {
    useWebSocketListenerInner<IBloomWebSocketEvent>(
        clientContext,
        eventId,
        listener,
        e => e,
        onlyCallListenerIfMessageIsTruthy ? e => !!e.message : e => true
    );
}
export function useSubscribeToWebSocketForStringMessage(
    clientContext: string,
    eventId: string,
    listener: (message: string) => void
) {
    useWebSocketListenerInner<string>(
        clientContext,
        eventId,
        listener,
        e => e.message!,
        e => !!e.message // ignore if no message
    );
}

// Subscribe to an event where the message string is holding a JSON object
// which this will parse.
export function useSubscribeToWebSocketForObject<T>(
    clientContext: string,
    eventId: string,
    listener: (message: T) => void
) {
    useWebSocketListenerInner<T>(
        clientContext,
        eventId,
        listener,
        e => JSON.parse(e.message!) as T,
        e => !!e.message // ignore if no message
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

    /**
     *  In an attempt to make it easier to come to grips with some lifetime issues, we
     * are naming the websocket by a "clientContext" and making this private, so
     * that clients don't have direct access to the client.
     * Instead the client should call "addListener(clientContext)" and then when cleaning
     * up, call "closeSocket(clientContext)".
     */
    public static getOrCreateWebSocket(clientContext: string): WebSocket {
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
                isGeckoFxOrFirefox ? clientContext : undefined
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
                    ].forEach(callback => callback(e));
                }
            };
            WebSocketManager.clientContextToDispatcherFunction[
                clientContext
            ] = listener;
            WebSocketManager.socketMap[clientContext].addEventListener(
                "message",
                listener
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
                ]
            );
            webSocket.close();
            delete WebSocketManager.socketMap[clientContext];
        }
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
        tagForDebugging?: string
    ): void {
        if (clientContext.indexOf("mock_") > -1) {
            // this is used in storybook stories. Don't try finding a server because there isn't one.
            // Events will come in via mockSend().
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

        // in the case of the progressDialog, we expect to have 2: one for the dialog, which is listening, and one for the ProgressBox.
        if (count > 2) {
            console.error(
                `addListener sees that we have ${count} listeners on "${clientContext}". Normally if everything is disconnecting appropriately, these should not add up.`
            );
        }
    }

    public static removeListener(
        clientContext: string,
        listener: (messageEvent: IBloomWebSocketEvent) => void
    ): void {
        WebSocketManager.clientContextCallbacks[
            clientContext
        ] = WebSocketManager.clientContextCallbacks[clientContext].filter(
            l => l === listener
        );
    }

    // useful for storybook stories to send messages
    public static mockSend<T extends IBloomWebSocketEvent>(
        clientContext: string,
        event: T
    ) {
        WebSocketManager.clientContextCallbacks[
            clientContext
        ].forEach(listener => listener(event));
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
                socket.readyState.toString()
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
