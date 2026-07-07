// Thin PostgREST client for the `tc` schema. Edge functions are thin orchestrators:
// the heavy per-request DB logic (locking, manifest diffing, atomic multi-table
// writes) lives in SECURITY DEFINER Postgres functions
// (supabase/migrations/20260706000004_tc_checkin_txn_functions.sql) that we call
// here via RPC, ALWAYS forwarding the caller's own Authorization header rather than
// a service-role key — see that migration's header comment for why this is correct
// (PostgREST resolves auth.jwt() from the Authorization bearer token regardless of
// which apikey is presented, and every function re-validates internally).
import { HttpError } from "./errors.ts";
import { supabaseAnonKey, supabaseUrl } from "./env.ts";

/** Extracts the incoming request's bearer token unmodified. Edge functions run with
 * verify_jwt = true by default (config.toml), so by the time our code runs the
 * platform has already rejected missing/invalid tokens with 401 — this is just for
 * forwarding, not for verification. */
export const authHeader = (req: Request): string => {
    const value = req.headers.get("Authorization");
    if (!value) {
        // Should not happen given verify_jwt = true, but fail loudly rather than
        // silently calling PostgREST unauthenticated if it ever does.
        throw new HttpError(401, { error: "unauthenticated" });
    }
    return value;
};

/** Calls a `tc` schema RPC (POST /rest/v1/rpc/<name>) with the caller's own JWT.
 * On a PostgREST error response, unwraps the PT### HTTP-status convention (see the
 * migration header comment) and the JSON-encoded `message` field into a proper
 * HttpError with the structured contract error body. */
export const callTcRpc = async <T = unknown>(
    req: Request,
    fnName: string,
    args: Record<string, unknown>,
): Promise<T> => {
    const res = await fetch(`${supabaseUrl()}/rest/v1/rpc/${fnName}`, {
        method: "POST",
        headers: {
            "Content-Type": "application/json",
            apikey: supabaseAnonKey(),
            Authorization: authHeader(req),
            "Content-Profile": "tc",
            "Accept-Profile": "tc",
        },
        body: JSON.stringify(args),
    });

    const text = await res.text();
    const parsed = text ? JSON.parse(text) : null;

    if (!res.ok) {
        throw new HttpError(res.status, parsePostgrestErrorBody(parsed));
    }

    return parsed as T;
};

/** Plain PostgREST read (GET /rest/v1/<table>?...) under RLS with the caller's own
 * JWT — used where a full RPC round-trip isn't needed (e.g. checkin-finish reading
 * back its own open transaction row to learn which paths to verify against S3). */
export const selectTcRow = async <T = Record<string, unknown>>(
    req: Request,
    table: string,
    query: string,
): Promise<T | null> => {
    const res = await fetch(`${supabaseUrl()}/rest/v1/${table}?${query}`, {
        method: "GET",
        headers: {
            apikey: supabaseAnonKey(),
            Authorization: authHeader(req),
            "Accept-Profile": "tc",
        },
    });
    const text = await res.text();
    const parsed = text ? JSON.parse(text) : [];
    if (!res.ok) {
        throw new HttpError(res.status, parsePostgrestErrorBody(parsed));
    }
    const rows = parsed as T[];
    return rows[0] ?? null;
};

/** PostgREST wraps our `RAISE EXCEPTION '%', <json>` message in
 * `{ message, code, details, hint }`. Our SQL always raises a JSON-object message
 * (e.g. `{"error":"LockHeldByOther","holder":{...}}`), so unwrap it back into a
 * flat contract-shaped body. Falls back gracefully for anything unexpected
 * (a Postgres-native error, a constraint violation, etc.) rather than throwing. */
const parsePostgrestErrorBody = (body: unknown): Record<string, unknown> => {
    const message = (body as { message?: unknown } | null)?.message;
    if (typeof message === "string") {
        try {
            const inner = JSON.parse(message);
            if (inner && typeof inner === "object") {
                return inner as Record<string, unknown>;
            }
        } catch {
            // Not JSON — fall through to the generic shape below.
        }
        return { error: message };
    }
    return { error: "internal_error", details: body };
};
