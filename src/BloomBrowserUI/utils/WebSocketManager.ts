interface IBloomWebSocketEvent {
    clientContext: string;
    id: string;
    message?: string;
    cssStyleRule?: string;
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

    private static socketMap: { [clientContext: string]: WebSocket } = {};

    /**
     *  In an attempt to make it easier to come to grips with some lifetime issues, we
     * are naming the websocket by a "clientContext" and making this private, so
     * that clients don't have direct access to the client.
     * Instead the client should call "addListener(clientContext)" and then when cleaning
     * up, call "closeSocket(clientContext)".
     */
    private static getOrCreateWebSocket(clientContext: string): WebSocket {
        if (!WebSocketManager.socketMap[clientContext]) {
            //currently we use a different port for this websocket, and it's the main port + 1
            let websocketPort = parseInt(window.location.port, 10) + 1;
            //here we're passing "socketName" in the "subprotocol" parameter, just for ease of identifying
            //sockets on the server side when debugging.
            WebSocketManager.socketMap[clientContext] = new WebSocket(
                "ws://127.0.0.1:" + websocketPort.toString(),
                clientContext
            );
            if (!WebSocketManager.clientContextCallbacks[clientContext]) {
                WebSocketManager.clientContextCallbacks[clientContext] = [];
            }
            // the following is a refactored holdover from a situation where we were having trouble
            // getting the web ui to properly close its own listeners and socket, so we had to
            // revert to have c# send a message that would close this down. It may or may not be
            // used, but it's here if we need it. The perfered method is for the client UI to call closeSocket().
            let listener = (event: MessageEvent) => {
                var e: IBloomWebSocketEvent = JSON.parse(event.data);
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
        let webSocket: WebSocket = WebSocketManager.socketMap[clientContext];
        if (webSocket) {
            webSocket.removeEventListener(
                "message",
                WebSocketManager.clientContextToDispatcherFunction[
                    clientContext
                ]
            );
            webSocket.close();
            WebSocketManager.socketMap[clientContext] = null;
        }
    }
    /**
     * Find or create a websocket and add a listener to it.
     * When a message is received on the socket whose data parses into an object whose clientContext property
     * is equal to the given clientContext, the parsed data object will be passed to the listener function.
     * @param {string} clientContext - should use the same name through the lifetime of the WebSocketManager.socketMap
     */
    public static addListener(
        clientContext: string,
        listener: (messageEvent: IBloomWebSocketEvent) => void
    ): void {
        WebSocketManager.getOrCreateWebSocket(clientContext); // side effect makes sure there's an array in listenerMap to push onto.
        WebSocketManager.clientContextCallbacks[clientContext].push(listener);
    }

    /**
     * Find or create a websocket (often used after addEventListener, in which case it is sure to
     * be found), and request a one-time notification when the socket is open, that is, able to
     * receive (and send) messages. If the socket is already open, onReady() will be called at
     * once, before the call to this method returns.
     */
    public static notifyReady(clientContext: string, onReady: () => void) {
        var socket = WebSocketManager.getOrCreateWebSocket(clientContext);
        if (socket.readyState === 0) {
            // CONNECTING
            var openFunc = () => {
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
