// Ad-hoc SQL verification against the local Supabase Postgres instance, for assertions like
// "a tc.collections row now exists" that the UI alone can't easily confirm. Connects directly
// via `pg` (not the `supabase db query` CLI) because shelling out to `supabase` — a Volta .cmd
// shim on Windows — needs `shell: true`, and Node's shell-arg concatenation (not proper
// escaping — see the child_process shell-option deprecation warning) mangled quoted SQL
// containing spaces in practice. A direct TCP connection sidesteps all of that.
import { Client } from "pg";

// Local Supabase's fixed dev Postgres connection (see `supabase status` / server/dev/README.md).
// Stable across `supabase db reset` — reset replays migrations, it doesn't change credentials.
const CONNECTION_STRING =
    "postgresql://postgres:postgres@localhost:54322/postgres";

/** Runs `sql` against the local stack and returns the result rows. Opens and closes a fresh
 * connection per call — verification queries are infrequent enough that pooling isn't worth
 * the complexity of also closing a shared pool at the end of a test run. */
export const queryDb = async <
    T extends Record<string, unknown> = Record<string, unknown>,
>(
    sql: string,
    params: unknown[] = [],
): Promise<T[]> => {
    const client = new Client({ connectionString: CONNECTION_STRING });
    await client.connect();
    try {
        const result = await client.query(sql, params);
        return result.rows as T[];
    } finally {
        await client.end();
    }
};
