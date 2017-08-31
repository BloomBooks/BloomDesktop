const kSocketName = "webSocket";

// This class manages a websocket, currently at the window level, currently with
// a fixed name. (Possible enhancement: support top window level).
// You can add listeners (for "message") with addListener(),
// and close the socket with closeSocket().
// Alternatively, using setCloseId, you can make the manager listen for a message
// with that ID. When received, it will remove all listeners and close the socket.
// Only one client per page needs to do this.
// (Without this, our web socket server does eventually receive a close notification
// when this window goes away, but various inscrutable JavaScript exceptions are raised.)
export default class WebSocketManager {

    private static listeners: ((event: MessageEvent) => void)[] = new Array();

    public static getWebSocket(): WebSocket {
        if (!window[kSocketName]) {
            //currently we use a different port for this websocket, and it's the main port + 1
            let websocketPort = parseInt(window.location.port, 10) + 1;
            window[kSocketName] = new WebSocket("ws://127.0.0.1:" + websocketPort.toString());
        }
        return window[kSocketName];
    }

    public static closeSocket(): void {
        let webSocket: WebSocket = window[kSocketName];
        if (webSocket) {
            while (this.listeners.length) {
                webSocket.removeEventListener("message", this.listeners.pop());
            }
            webSocket.close();
            window[kSocketName] = null;
        }
    }

    public static addListener(listener: (ev: MessageEvent) => void): void {
        this.getWebSocket().addEventListener("message", listener);
        this.listeners.push(listener);
    }

    public static setCloseId(id: string): void {
        let closeListener = (event: MessageEvent) => {
            var e = JSON.parse(event.data);
            if (e.id === id) {
                this.closeSocket();
            }
        }
        this.addListener(closeListener);
    }
}