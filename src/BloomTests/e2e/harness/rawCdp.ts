// Direct (non-Playwright) CDP client for WebView2 targets that Playwright's connectOverCDP
// cannot see.
//
// DISCOVERED LIMITATION (see progress log / README): Bloom's "Create Team Collection" and
// other ReactDialog-hosted WebView2 controls (opened via `ReactDialog.ShowOnIdle`, a brand-new
// native Form + WebView2 control distinct from the main Collection/Edit/Publish window) show up
// in the raw CDP `/json/list` endpoint but NEVER appear in Playwright's `browser.contexts()` /
// `.pages()` after `chromium.connectOverCDP()` — confirmed empirically by polling for 5+
// seconds after connecting while the dialog target was independently confirmed still present
// via `/json/list`. This looks like a Playwright/WebView2 auto-attach gap (each WinForms-hosted
// WebView2 control may register as a separate target group that Playwright's default browser
// session doesn't subscribe to), not a timing race. Rather than depending on a fix upstream,
// this harness talks CDP directly over the target's own `webSocketDebuggerUrl` for any page
// that Playwright can't see, using `Runtime.evaluate` to read/manipulate the DOM. This is still
// exercising the real, running WebView2 control — just via a lower-level protocol client.
import { randomUUID } from "node:crypto";

export interface CdpTargetInfo {
    id: string;
    title: string;
    url: string;
    type: string;
    webSocketDebuggerUrl: string;
}

/** Lists every CDP target Bloom's remote-debugging endpoint currently knows about (includes
 * targets Playwright's connectOverCDP fails to auto-attach — see module doc above). */
export const listCdpTargets = async (
    cdpPort: number,
): Promise<CdpTargetInfo[]> => {
    const response = await fetch(`http://localhost:${cdpPort}/json/list`);
    if (!response.ok) {
        throw new Error(
            `CDP /json/list failed: ${response.status} ${response.statusText}`,
        );
    }
    return response.json();
};

/** Polls `/json/list` until a target's title or url contains `substring`, then returns it. */
export const waitForCdpTarget = async (
    cdpPort: number,
    substring: string,
    timeoutMs = 15_000,
): Promise<CdpTargetInfo> => {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        // Swallow transient connection failures: opening a brand-new WinForms+WebView2 dialog
        // can make the shared CDP endpoint briefly refuse connections (observed empirically),
        // which is not the same as "the target doesn't exist yet" and shouldn't abort the poll.
        const targets = await listCdpTargets(cdpPort).catch(
            () => [] as CdpTargetInfo[],
        );
        const match = targets.find(
            (target) =>
                target.title.includes(substring) ||
                target.url.includes(substring),
        );
        if (match) return match;
        await new Promise((resolve) => setTimeout(resolve, 300));
    }
    const seen = (await listCdpTargets(cdpPort)).map(
        (target) => `${target.title} ${target.url}`,
    );
    throw new Error(
        `Timed out waiting for a CDP target containing '${substring}'. Seen: ${JSON.stringify(seen)}`,
    );
};

/** A thin client for a single CDP target's WebSocket endpoint. Only implements what this
 * harness needs: Runtime.evaluate (with awaitPromise so async expressions work) and disconnect. */
export class RawCdpPage {
    private ws: WebSocket;
    private nextId = 1;
    private pending = new Map<
        number,
        { resolve: (value: any) => void; reject: (error: any) => void }
    >();
    private ready: Promise<void>;

    constructor(webSocketDebuggerUrl: string) {
        this.ws = new WebSocket(webSocketDebuggerUrl);
        this.ready = new Promise((resolve, reject) => {
            this.ws.addEventListener("open", () => resolve());
            this.ws.addEventListener("error", (event) => reject(event));
        });
        this.ws.addEventListener("message", (event) => {
            const message = JSON.parse(event.data.toString());
            if (message.id && this.pending.has(message.id)) {
                const { resolve, reject } = this.pending.get(message.id)!;
                this.pending.delete(message.id);
                if (message.error)
                    reject(new Error(JSON.stringify(message.error)));
                else resolve(message.result);
            }
        });
    }

    private send(
        method: string,
        params: Record<string, unknown> = {},
    ): Promise<any> {
        const id = this.nextId++;
        return new Promise((resolve, reject) => {
            this.pending.set(id, { resolve, reject });
            this.ws.send(JSON.stringify({ id, method, params }));
        });
    }

    /** Evaluates `expression` in the page's main world. If it returns a Promise, awaits it. */
    async evaluate<T = unknown>(expression: string): Promise<T> {
        await this.ready;
        const result = await this.send("Runtime.evaluate", {
            expression,
            returnByValue: true,
            awaitPromise: true,
        });
        if (result.exceptionDetails) {
            throw new Error(
                `Runtime.evaluate threw: ${JSON.stringify(result.exceptionDetails)}\nExpression: ${expression}`,
            );
        }
        return result.result?.value as T;
    }

    /** Sets a React-controlled `<input>`'s value via the native value setter (bypassing React's
     * value-setter override) and dispatches an `input` event so React's onChange fires — the
     * standard trick for scripting controlled inputs without a full synthetic-event stack. */
    async fillInput(selector: string, value: string): Promise<void> {
        const escapedSelector = JSON.stringify(selector);
        const escapedValue = JSON.stringify(value);
        await this.evaluate(`
            (() => {
                const el = document.querySelector(${escapedSelector});
                if (!el) throw new Error('fillInput: no element matching ${escapedSelector}');
                const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
                setter.call(el, ${escapedValue});
                el.dispatchEvent(new Event('input', { bubbles: true }));
                el.dispatchEvent(new Event('change', { bubbles: true }));
            })()
        `);
    }

    /** Clicks the element matching `selector` (throws if not found). */
    async click(selector: string): Promise<void> {
        const escapedSelector = JSON.stringify(selector);
        await this.evaluate(`
            (() => {
                const el = document.querySelector(${escapedSelector});
                if (!el) throw new Error('click: no element matching ${escapedSelector}');
                el.click();
            })()
        `);
    }

    /** Returns the (trimmed) textContent of the first element matching `selector`, or null. */
    async textContent(selector: string): Promise<string | null> {
        const escapedSelector = JSON.stringify(selector);
        return this.evaluate<string | null>(`
            (() => {
                const el = document.querySelector(${escapedSelector});
                return el ? el.textContent.trim() : null;
            })()
        `);
    }

    /** Polls until `selector` exists in the DOM (throws on timeout). */
    async waitForSelector(selector: string, timeoutMs = 15_000): Promise<void> {
        const deadline = Date.now() + timeoutMs;
        while (Date.now() < deadline) {
            const exists = await this.evaluate<boolean>(
                `!!document.querySelector(${JSON.stringify(selector)})`,
            );
            if (exists) return;
            await new Promise((resolve) => setTimeout(resolve, 300));
        }
        throw new Error(
            `waitForSelector: '${selector}' did not appear within ${timeoutMs}ms.`,
        );
    }

    close(): void {
        this.ws.close();
    }
}

/** Waits for a CDP target matching `titleOrUrlSubstring`, connects to it directly, and returns
 * a ready-to-use RawCdpPage. */
export const attachToCdpTarget = async (
    cdpPort: number,
    titleOrUrlSubstring: string,
    timeoutMs = 15_000,
): Promise<RawCdpPage> => {
    const target = await waitForCdpTarget(
        cdpPort,
        titleOrUrlSubstring,
        timeoutMs,
    );
    const cdpPage = new RawCdpPage(target.webSocketDebuggerUrl);
    // Force a microtask tick so the constructor's `ready` promise has a listener attached
    // before callers start issuing commands (evaluate() already awaits `ready` regardless).
    void randomUUID();
    return cdpPage;
};
