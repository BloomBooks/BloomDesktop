// Small JSON-response helpers shared by every edge function. Kept deliberately
// un-clever: the contract error shapes in CONTRACTS.md are just
// `{ error: "<Code>", ...extra }`, so we build exactly that.

export const CORS_HEADERS: Record<string, string> = {
    // Bloom Desktop calls these from the C# process (HttpClient), not a browser page,
    // so CORS is not actually load-bearing — but it's harmless and cheap to allow, in
    // case any tooling (smoke tests, future browser-based admin UI) calls in directly.
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Headers":
        "authorization, x-client-info, apikey, content-type",
    "Access-Control-Allow-Methods": "POST, OPTIONS",
};

export const jsonResponse = (status: number, body: unknown): Response =>
    new Response(JSON.stringify(body), {
        status,
        headers: { "Content-Type": "application/json", ...CORS_HEADERS },
    });

/** Standard error envelope: `{ error: "<Code>", ...extra }`. */
export const errorResponse = (
    status: number,
    error: string,
    extra?: Record<string, unknown>,
): Response => jsonResponse(status, { error, ...extra });

export class HttpError extends Error {
    readonly status: number;
    readonly body: Record<string, unknown>;
    constructor(status: number, body: Record<string, unknown>) {
        super(typeof body.error === "string" ? body.error : `HTTP ${status}`);
        this.status = status;
        this.body = body;
    }
    toResponse(): Response {
        return jsonResponse(this.status, this.body);
    }
}
