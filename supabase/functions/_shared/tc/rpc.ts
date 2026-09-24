// Thin PostgREST client for the `tc` schema. Edge functions are thin orchestrators:
// the heavy per-request DB logic (locking, manifest diffing, atomic multi-table
// writes) lives in SECURITY DEFINER Postgres functions
// (supabase/schemas/tc/02_functions.sql) that we call here via RPC.
//
// Two ways of calling, and the rule for choosing:
//   - callTcRpc forwards the caller's OWN Authorization header. PostgREST validates the
//     JWT (a Firebase ID token under third-party auth, a GoTrue token locally) and the
//     function reads the caller from auth.jwt(). Used for everything a member could
//     safely do by calling the RPC directly (start, abort, membership checks).
//   - callTcServiceRpc uses the service-role key. Used ONLY for the finish RPCs
//     (checkin_finish_tx, collection_files_finish_tx), which record S3 version-ids the
//     edge function has just verified; they are EXECUTE-able by service_role alone, so a
//     member cannot call them directly with unverified version-ids. Because auth.jwt() is
//     then the service role, the caller's id is passed explicitly, after first being
//     established from their own JWT by callerIdentity() (never from the request body).
import { HttpError } from "./errors.ts";
import { supabaseAnonKey, supabaseServiceRoleKey, supabaseUrl } from "./env.ts";

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
export const callTcRpc = <T = unknown>(
    req: Request,
    fnName: string,
    args: Record<string, unknown>,
): Promise<T> => postTcRpc<T>(fnName, args, supabaseAnonKey(), authHeader(req));

/** Calls a service-role-only `tc` RPC with the service-role key (see the header
 * comment for when that is allowed). The request's own token is never used here. */
export const callTcServiceRpc = <T = unknown>(
    fnName: string,
    args: Record<string, unknown>,
): Promise<T> => {
    const key = supabaseServiceRoleKey();
    return postTcRpc<T>(fnName, args, key, `Bearer ${key}`);
};

/** The caller's identity as the database sees it (tc.current_caller). */
export interface CallerIdentity {
    userId: string;
    email: string | null;
    name: string | null;
}

/** Establishes who the caller is by asking PostgREST, with the caller's own JWT, to run
 * tc.current_caller(). PostgREST is the component that validates that JWT (it is set up
 * for Supabase third-party Firebase auth, so a Firebase ID token works here, whereas
 * GoTrue's /auth/v1/user would not know a Firebase user). An invalid or expired token is
 * rejected there with 401 and never reaches the service-role call that follows. */
export const callerIdentity = (req: Request): Promise<CallerIdentity> =>
    callTcRpc<CallerIdentity>(req, "current_caller", {});

const postTcRpc = async <T>(
    fnName: string,
    args: Record<string, unknown>,
    apikey: string,
    authorization: string,
): Promise<T> => {
    const res = await fetch(`${supabaseUrl()}/rest/v1/rpc/${fnName}`, {
        method: "POST",
        headers: {
            "Content-Type": "application/json",
            apikey,
            Authorization: authorization,
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
