// A web proxy that refuses everything, to take a launched Bloom offline without taking the machine
// offline. Restart Bloom with IRelaunchChanges.proxy set to its url, and every request Bloom's own
// code makes to the internet fails, as it does with no connection, while Bloom's server and its
// browser, which talk over localhost, keep working. The proxy also records which hosts Bloom tried
// to reach, so a test can show that Bloom really did try, and failed, rather than never trying.

import * as http from "node:http";
import type { AddressInfo, Socket } from "node:net";

/** What startRefusingProxy() returns. */
export interface IRefusingProxy {
    /** The proxy's address, for IRelaunchChanges.proxy, e.g. "http://127.0.0.1:5123". */
    url: string;
    /**
     * Each host (with its port) that something asked this proxy to reach, in the order asked,
     * since it started.
     */
    hostsAsked: () => string[];
    /** Stop listening and drop every connection. */
    close: () => Promise<void>;
}

/**
 * Start a proxy on a free port of 127.0.0.1 that answers every request, an HTTPS tunnel (CONNECT)
 * or a plain HTTP one, with 502 Bad Gateway, as a proxy does when it cannot reach the server.
 */
export async function startRefusingProxy(): Promise<IRefusingProxy> {
    const hosts: string[] = [];
    const sockets = new Set<Socket>();
    const server = http.createServer((request, response) => {
        // A plain HTTP request through a proxy names its whole URL.
        let host = request.headers.host ?? "";
        try {
            host = new URL(request.url ?? "").host || host;
        } catch {
            // Not an absolute URL: keep the Host header.
        }
        hosts.push(host);
        response.writeHead(502, { Connection: "close" });
        response.end();
    });
    server.on("connect", (request, socket: Socket) => {
        hosts.push(request.url ?? "");
        socket.end("HTTP/1.1 502 Bad Gateway\r\nConnection: close\r\n\r\n");
    });
    server.on("connection", (socket: Socket) => {
        sockets.add(socket);
        socket.on("close", () => sockets.delete(socket));
    });
    await new Promise<void>((resolve, reject) => {
        server.once("error", reject);
        server.listen(0, "127.0.0.1", () => resolve());
    });
    const { port } = server.address() as AddressInfo;
    return {
        url: `http://127.0.0.1:${port}`,
        hostsAsked: () => [...hosts],
        close: () =>
            new Promise<void>((resolve) => {
                for (const socket of sockets) socket.destroy();
                server.close(() => resolve());
            }),
    };
}
