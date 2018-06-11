// This class manages a websocket, currently at the window level, currently with
// a fixed name. (Possible enhancement: support top window level).
// You can add listeners (for "message") with addListener(),
// and close the socket with closeSocket().
export default class WebSocketManager {

    private static listeners: ((event: MessageEvent) => void)[] = new Array();

    /**
      *  In an attempt to make it easier to come to grips with some lifetime issues, we
      * are naming the websocket by a "socketName" and making this private, so
      * that clients don't have direct access to the client.
      * Instead the client should call "addListener(socketName)" and then when cleaning
      * up, call "closeSocket(socketName)".
      */
    private static getWebSocket(socketName: string): WebSocket {
        if (!window[socketName]) {
            //currently we use a different port for this websocket, and it's the main port + 1
            let websocketPort = parseInt(window.location.port, 10) + 1;
            //here we're passing "socketName" in the "subprotocol" parameter, just for ease of identifying
            //sockets on the server side when debugging.
            window[socketName] = new WebSocket("ws://127.0.0.1:" + websocketPort.toString(), socketName);

            // the following is a refactored holdover from a situation where we were having trouble
            // getting the web ui to properly close its own listeners and socket, so we had to
            // revert to have c# send a message that would close this down. It may or may not be
            // used, but it's here if we need it. The perfered method is for the client UI to call closeSocket().
            let closeListener = (event: MessageEvent) => {
                var e = JSON.parse(event.data);
                if (e.id === "websocketControl/close/" + socketName) {
                    this.closeSocket(socketName);
                }
            };
            this.addListener(socketName, closeListener);
        }
        return window[socketName];
    }

    /**
     * Disconnect all listeners and close the websocket.
     * @param {string} socketName - should use the same name through the lifetime of the window
     */
    public static closeSocket(socketName: string): void {
        let webSocket: WebSocket = window[socketName];
        if (webSocket) {
            while (this.listeners.length) {
                webSocket.removeEventListener("message", this.listeners.pop());
            }
            webSocket.close();
            window[socketName] = null;
        }
    }

    /**
     * Find or create a websocket and add a listener to it.
     * @param {string} socketName - should use the same name through the lifetime of the window
     */
    public static addListener(socketName: string, listener: (ev: MessageEvent) => void): void {
        this.getWebSocket(socketName).addEventListener("message", listener);
        this.listeners.push(listener);
    }

    /**
     * Find or create a websocket (often used after addEventListener, in which case it is sure to
     * be found), and request a one-time notification when the socket is open, that is, able to
     * receive (and send) messages. If the socket is already open, onReady() will be called at
     * once, before the call to this method returns.
     */
    public static notifyReady(socketName: string, onReady: () => void) {
        var socket = this.getWebSocket(socketName);
        if (socket.readyState === 0) { // CONNECTING
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
