// Shared helpers for the Deno unit tests under supabase/functions/**. Every edge
// function handler talks to two external systems: PostgREST (via plain `fetch` in
// rpc.ts) and S3/STS (via the AWS SDK clients in s3.ts). This module gives tests a
// cheap way to fake both without a live stack:
//   - `withMockFetch` temporarily replaces `globalThis.fetch` for the duration of one
//     test, then restores the original — used to fake PostgREST RPC/table responses.
//   - `setTestEnv` populates the env vars `_shared/env.ts` requires, so importing a
//     handler module never throws "missing required environment variable" at test time.
//   - `mockRequest` builds a same-shaped `Request` the handler expects (JSON body +
//     bearer token), matching what `serveJsonPost` hands the handler after parsing.
// S3/STS mocking uses `aws-sdk-client-mock` directly in each test file (it patches the
// SDK client prototypes, so no indirection is needed here).

/** Sets every env var `_shared/env.ts` reads, with dev-mode-friendly defaults. Call
 * this at the top of every test file (module scope) — handlers call `s3Env()` /
 * `supabaseUrl()` etc. eagerly inside the request path, not at import time, but it's
 * simplest to just always have them present. */
export const setTestEnv = (): void => {
    Deno.env.set("SUPABASE_URL", "http://127.0.0.1:54321");
    Deno.env.set("SUPABASE_ANON_KEY", "test-anon-key");
    Deno.env.set("BLOOM_DEV_MODE", "true");
    Deno.env.set("BLOOM_S3_ENDPOINT", "http://minio.invalid:9000");
    Deno.env.set("BLOOM_S3_BUCKET", "bloom-teams-test");
    Deno.env.set("BLOOM_S3_REGION", "us-east-1");
    Deno.env.set("BLOOM_S3_ROOT_ACCESS_KEY", "test-root-key");
    Deno.env.set("BLOOM_S3_ROOT_SECRET_KEY", "test-root-secret");
};

/** Builds a `Request` shaped like what `serveJsonPost` passes to a handler: JSON body,
 * bearer auth header. Handlers only read `req.headers`, never re-read the body (that
 * was already consumed by `serveJsonPost`), so this is safe to pass directly. */
export const mockRequest = (body: unknown, token = "test-jwt"): Request =>
    new Request("http://localhost/test", {
        method: "POST",
        headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });

/** Calls an exported handler the same way `serveJsonPost` (handler.ts) would: a thrown
 * `HttpError` becomes its JSON error `Response` instead of an unhandled rejection.
 * Handlers are written to throw (see AGENTS.md's fail-fast testing philosophy) and rely
 * on `serveJsonPost`'s try/catch to translate that — tests calling the handler directly
 * (bypassing serveJsonPost) need the same translation, or every error-path test would
 * have to catch HttpError itself. */
export const callHandler = async (
    handler: (req: Request, body: Record<string, unknown>) => Promise<Response>,
    req: Request,
    body: Record<string, unknown>,
): Promise<Response> => {
    // Imported lazily to avoid every test file needing its own import of HttpError.
    const { HttpError } = await import("./errors.ts");
    try {
        return await handler(req, body);
    } catch (err) {
        if (err instanceof HttpError) {
            return err.toResponse();
        }
        throw err;
    }
};

export type FetchStub = (input: string | URL | Request, init?: RequestInit) => Promise<Response>;

/** Replaces `globalThis.fetch` with `stub` for the duration of `fn`, always restoring
 * the original afterward (even if `fn` throws) — used to fake PostgREST responses from
 * `_shared/rpc.ts`'s `callTcRpc`/`selectTcRow`, which call the real `fetch`. */
export const withMockFetch = async <T>(stub: FetchStub, fn: () => Promise<T>): Promise<T> => {
    const original = globalThis.fetch;
    // deno-lint-ignore no-explicit-any
    globalThis.fetch = stub as any;
    try {
        return await fn();
    } finally {
        globalThis.fetch = original;
    }
};

/** A `fetch` stub that dispatches by matching a substring against the request URL, in
 * order — the first match wins. Each route returns `{ status, body }`; `body` is
 * JSON-stringified (or `""` for `null`, matching how PostgREST responds to e.g. a
 * successful RPC with no return value). */
export const routedFetchStub = (
    routes: { when: string; status: number; body: unknown }[],
): FetchStub => {
    return (input) => {
        const url = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
        const route = routes.find((r) => url.includes(r.when));
        if (!route) {
            throw new Error(`routedFetchStub: no route matched for ${url}`);
        }
        const text = route.body === null ? "" : JSON.stringify(route.body);
        return Promise.resolve(
            new Response(text, { status: route.status, headers: { "Content-Type": "application/json" } }),
        );
    };
};
