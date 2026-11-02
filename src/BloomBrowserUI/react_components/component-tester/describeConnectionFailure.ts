/**
 * The nightly has twice failed one component test on its first navigation with
 * `net::ERR_CONNECTION_FAILED`, while every other test around it passed and the dev server logged
 * nothing. Chromium reports that error for a socket error it has no specific name for, so the
 * message alone cannot say whether the dev server, Windows, or Chromium was at fault. This
 * records, at the moment of failure, the facts that tell those apart.
 */

import { execFile } from "node:child_process";
import * as net from "node:net";

/**
 * Probes the dev server with a raw TCP connect and an HTTP GET from Node (outside Chromium), and
 * counts the machine's TCP connections by state, which shows whether it was short of local ports.
 * Returns a report to append to the navigation error. Never throws: a probe that fails is itself
 * part of the report.
 */
export async function describeConnectionFailure(
    baseUrl: string,
): Promise<string> {
    const url = new URL(baseUrl);
    const lines = [
        `Connection diagnostics at ${new Date().toISOString()}:`,
        `  raw TCP connect to ${url.hostname}:${url.port}: ${await probeTcpConnect(url.hostname, Number(url.port))}`,
        `  HTTP GET ${url.href} from Node: ${await probeHttpGet(url.href)}`,
        `  TCP connections by state (netstat): ${await countTcpStates()}`,
    ];
    return lines.join("\n");
}

/**
 * Opens and closes one TCP connection, and reports how long it took or the OS error code.
 */
function probeTcpConnect(host: string, port: number): Promise<string> {
    const start = Date.now();
    return new Promise((resolve) => {
        const socket = net.connect({ host, port });
        socket.setTimeout(5000);
        socket.on("connect", () => {
            socket.destroy();
            resolve(`connected in ${Date.now() - start} ms`);
        });
        socket.on("timeout", () => {
            socket.destroy();
            resolve(`no answer after ${Date.now() - start} ms`);
        });
        socket.on("error", (error: NodeJS.ErrnoException) => {
            resolve(
                `failed after ${Date.now() - start} ms: ${error.code} (errno ${error.errno}) ${error.message}`,
            );
        });
    });
}

/**
 * Fetches the page the test failed to load, and reports the status and time or the error.
 */
async function probeHttpGet(href: string): Promise<string> {
    const start = Date.now();
    try {
        const response = await fetch(href, {
            signal: AbortSignal.timeout(5000),
        });
        await response.arrayBuffer();
        return `HTTP ${response.status} in ${Date.now() - start} ms`;
    } catch (error) {
        const cause = (error as { cause?: { code?: string } }).cause;
        return `failed after ${Date.now() - start} ms: ${String(error)}${cause?.code ? ` (${cause.code})` : ""}`;
    }
}

/**
 * Runs netstat and counts the TCP connections in each state, e.g. "ESTABLISHED 120, TIME_WAIT 9000".
 */
function countTcpStates(): Promise<string> {
    return new Promise((resolve) => {
        execFile(
            "netstat",
            ["-an", "-p", "tcp"],
            { timeout: 15000, maxBuffer: 64 * 1024 * 1024 },
            (error, stdout) => {
                if (error) {
                    resolve(`netstat failed: ${error.message}`);
                    return;
                }
                const counts = new Map<string, number>();
                for (const match of stdout.matchAll(
                    /\b(ESTABLISHED|LISTEN(?:ING)?|TIME_WAIT|CLOSE_WAIT|FIN_WAIT_1|FIN_WAIT_2|SYN_SENT|SYN_RECEIVED|LAST_ACK|CLOSING)\b/g,
                )) {
                    counts.set(match[1], (counts.get(match[1]) ?? 0) + 1);
                }
                resolve(
                    [...counts]
                        .map(([state, count]) => `${state} ${count}`)
                        .join(", ") || "no connections parsed",
                );
            },
        );
    });
}
