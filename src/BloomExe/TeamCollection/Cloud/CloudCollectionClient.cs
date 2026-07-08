using System;
using System.Net;
using Newtonsoft.Json.Linq;
using RestSharp;
using SIL.Reporting;

namespace Bloom.TeamCollection.Cloud
{
    /// <summary>
    /// Server-reported error codes that Cloud Team Collection callers need to branch on, per
    /// CONTRACTS.md. `Unknown` covers any error we could not classify (still surfaced with the
    /// server's own message). `NotSignedIn` is synthesized locally (not a server code) when a
    /// 401 survives a refresh attempt.
    /// </summary>
    public enum CloudErrorCode
    {
        Unknown,
        NotSignedIn,
        LockHeldByOther,
        BaseVersionSuperseded,
        NameConflict,
        ClientOutOfDate,
        MissingOrBadUploads,
        VersionConflict,
        TransactionExpired,
    }

    /// <summary>
    /// A typed error from a Cloud Team Collection RPC or edge function call. <see cref="Code"/>
    /// lets callers branch (e.g. show "X is editing this book" for LockHeldByOther) without
    /// string-matching; <see cref="Details"/> is the raw parsed JSON error body (e.g. the lock
    /// holder's identity, or the list of bad paths for MissingOrBadUploads) for callers that need
    /// more than the code.
    /// </summary>
    public class CloudCollectionClientException : ApplicationException
    {
        public CloudErrorCode Code { get; }
        public JToken Details { get; }

        public CloudCollectionClientException(
            CloudErrorCode code,
            string message,
            JToken details = null
        )
            : base(message)
        {
            Code = code;
            Details = details;
        }
    }

    /// <summary>
    /// The one thing <see cref="CloudCollectionClient"/> needs from a transport: execute a
    /// request, get a response. Deliberately much smaller than RestSharp's own IRestClient
    /// (which carries dozens of unrelated configuration members) so tests can substitute a fake
    /// executor with a single method instead of stubbing an entire third-party interface.
    /// </summary>
    internal interface IRestExecutor
    {
        IRestResponse Execute(IRestRequest request);
    }

    /// <summary>Production <see cref="IRestExecutor"/>: a thin wrapper over a real RestSharp RestClient.</summary>
    internal class RestSharpExecutor : IRestExecutor
    {
        private readonly RestClient _client;

        public RestSharpExecutor(string baseUrl)
        {
            _client = new RestClient(baseUrl);
        }

        public IRestResponse Execute(IRestRequest request) => _client.Execute(request);
    }

    /// <summary>
    /// RestSharp client for Cloud Team Collection Postgres RPCs (PostgREST) and edge functions,
    /// per CONTRACTS.md. Modeled on <see cref="Bloom.WebLibraryIntegration.BloomLibraryBookApiClient"/>.
    /// Injects the bearer token from <see cref="CloudAuth"/> on every call, retries once via
    /// <see cref="CloudAuth.TryRefreshOn401"/> on a 401, and maps error responses to a
    /// <see cref="CloudCollectionClientException"/> with a typed <see cref="CloudErrorCode"/>.
    /// This class only owns the transport; the RPC/edge-function-specific methods (get_collection_state,
    /// checkout_book, checkin-start, etc.) are built on top of it by later tasks.
    /// </summary>
    public class CloudCollectionClient
    {
        private readonly CloudEnvironment _environment;
        private readonly CloudAuth _auth;
        private IRestExecutor _restClient;

        public CloudCollectionClient(CloudEnvironment environment, CloudAuth auth)
        {
            _environment = environment;
            _auth = auth;
        }

        /// <summary>
        /// Test-only seam: lets unit tests substitute a fake <see cref="IRestExecutor"/> so error
        /// mapping and header injection can be verified without a live server. Production code
        /// never needs to call this; <see cref="RestClient"/> lazily creates a real one.
        /// </summary>
        internal void SetRestClientForTests(IRestExecutor restClient) => _restClient = restClient;

        private IRestExecutor RestClient =>
            _restClient ?? (_restClient = new RestSharpExecutor(_environment.SupabaseUrl));

        /// <summary>
        /// Calls a `tc`-schema Postgres RPC (PostgREST `/rest/v1/rpc/&lt;name&gt;`). Per
        /// CONTRACTS.md v1.1, <paramref name="parametersWithPPrefixedKeys"/> must already use the
        /// `p_`-prefixed argument names the SQL functions declare (PostgREST matches JSON keys to
        /// parameter names verbatim) — e.g. an anonymous object with a `p_collection_id`
        /// property, not `collection_id`. Returns the parsed JSON result (or null for a 204/empty
        /// body); throws <see cref="CloudCollectionClientException"/> on any error response.
        /// </summary>
        public JToken CallRpc(string rpcName, object parametersWithPPrefixedKeys)
        {
            return ExecuteWithAuthRetry(() =>
            {
                var request = new RestRequest($"rest/v1/rpc/{rpcName}", Method.POST);
                AddCommonHeaders(request);
                // tc is a separate PostgREST-exposed schema (not the default "public"), so every
                // call must say so on both the request and response side (CONTRACTS.md v1.1).
                request.AddHeader("Content-Profile", "tc");
                request.AddHeader("Accept-Profile", "tc");
                AddJsonBody(request, parametersWithPPrefixedKeys);
                return request;
            });
        }

        /// <summary>
        /// Calls a Cloud Team Collection edge function (`/functions/v1/&lt;name&gt;`), per
        /// CONTRACTS.md. Returns the parsed JSON result; throws
        /// <see cref="CloudCollectionClientException"/> on any error response (including the
        /// 426 ClientOutOfDate and 409 LockHeldByOther/BaseVersionSuperseded/NameConflict/
        /// MissingOrBadUploads/VersionConflict shapes edge functions use).
        /// </summary>
        public JToken CallEdgeFunction(string functionName, object body)
        {
            return ExecuteWithAuthRetry(() =>
            {
                var request = new RestRequest($"functions/v1/{functionName}", Method.POST);
                AddCommonHeaders(request);
                AddJsonBody(request, body);
                return request;
            });
        }

        /// <summary>
        /// Serializes <paramref name="body"/> with Newtonsoft ourselves and attaches the resulting
        /// JSON text as a raw request-body parameter, rather than using RestSharp's own
        /// <c>AddJsonBody</c> (which defers serialization to RestSharp's own default JSON
        /// serializer). That matters here because several typed wrapper methods below (e.g.
        /// CheckinStart, CollectionFilesStart) embed a Newtonsoft <see cref="JArray"/>/
        /// <see cref="JObject"/> directly inside the anonymous body object -- RestSharp's default
        /// serializer does not know how to serialize a JToken as a native JSON array/object (it
        /// reflects over JToken's own CLR properties instead), which silently produced a malformed
        /// `files` payload and a cryptic Postgres "jsonb_to_recordset must be an array of objects"
        /// error. Discovered via the live round-trip test against the real local dev stack -- the
        /// FakeRestExecutor-based unit tests never actually serialize a request, so they couldn't
        /// have caught this.
        /// </summary>
        private static void AddJsonBody(RestRequest request, object body)
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(body ?? new object());
            request.AddParameter("application/json", json, ParameterType.RequestBody);
        }

        // ---------------------------------------------------------------
        // Typed wrappers over CallRpc/CallEdgeFunction, one per RPC/edge
        // function in CONTRACTS.md v1.2. Added by task 05 (CloudCollectionClient's
        // own doc comment above said these belong here). Each just builds the
        // p_-prefixed (RPC) or camelCase (edge function) body and casts the
        // result to the shape callers expect; CloudTeamCollection/CloudCollectionMonitor/
        // CloudJoinFlow parse the returned JToken with Newtonsoft, matching the
        // pattern CloudRepoCache already uses for JObject snapshots.
        // ---------------------------------------------------------------

        /// <summary>Creates a new collection, with the caller as its sole claimed admin.</summary>
        public JToken CreateCollection(string collectionId, string name) =>
            CallRpc("create_collection", new { p_id = collectionId, p_name = name });

        /// <summary>Lists the collections where the caller's email is approved (claimed or not).</summary>
        public JArray MyCollections() =>
            (JArray)(CallRpc("my_collections", new { }) ?? new JArray());

        /// <summary>Fills user_id on membership rows matching the caller's verified email.</summary>
        public JToken ClaimMemberships() => CallRpc("claim_memberships", new { });

        /// <summary>
        /// Full snapshot (sinceEventId null) or delta (sinceEventId set) of book rows, collection-file
        /// group versions, and max_event_id. Feeds <see cref="CloudRepoCache.ApplyFullSnapshot"/>/
        /// <see cref="CloudRepoCache.ApplyDelta"/>.
        /// </summary>
        public JObject GetCollectionState(string collectionId, long? sinceEventId = null) =>
            (JObject)CallRpc(
                "get_collection_state",
                new { p_collection_id = collectionId, p_since_event_id = sinceEventId }
            );

        /// <summary>Events + touched book rows since <paramref name="sinceEventId"/> (polling/catch-up).</summary>
        public JObject GetChanges(string collectionId, long sinceEventId) =>
            (JObject)CallRpc(
                "get_changes",
                new { p_collection_id = collectionId, p_since_event_id = sinceEventId }
            );

        /// <summary>
        /// v1.2: per-file current manifest for the pinned-version Receive path. Never-committed
        /// books are invisible except to their mid-Send lock holder.
        /// </summary>
        public JObject GetBookManifest(string bookId) =>
            (JObject)CallRpc("get_book_manifest", new { p_book_id = bookId });

        /// <summary>Conditional lock; result includes the winning holder's identity on failure.</summary>
        public JObject CheckoutBook(string bookId, string machine) =>
            (JObject)CallRpc("checkout_book", new { p_book_id = bookId, p_machine = machine });

        /// <summary>Releases the caller's own lock (undo checkout; no content change).</summary>
        public JObject UnlockBookRpc(string bookId) =>
            (JObject)CallRpc("unlock_book", new { p_book_id = bookId });

        /// <summary>Admin-only forced unlock; audited server-side, emits a ForcedUnlock event.</summary>
        public JObject ForceUnlockRpc(string bookId) =>
            (JObject)CallRpc("force_unlock", new { p_book_id = bookId });

        /// <summary>Requires the caller holds the lock; sets deleted_at and emits a Deleted event.</summary>
        public JObject DeleteBookRpc(string bookId) =>
            (JObject)CallRpc("delete_book", new { p_book_id = bookId });

        /// <summary>Admin-only; clears a tombstone (name-uniqueness re-enforced).</summary>
        public JObject UndeleteBookRpc(string bookId) =>
            (JObject)CallRpc("undelete_book", new { p_book_id = bookId });

        /// <summary>Advisory uniqueness pre-check for a proposed rename.</summary>
        public JObject RenameCheck(string bookId, string newName) =>
            (JObject)CallRpc("rename_check", new { p_book_id = bookId, p_new_name = newName });

        /// <summary>
        /// Admin-only approved-accounts list. RPC name is our best reading of CONTRACTS.md's
        /// "members: list/add/remove/set_role" shorthand (not spelled out precisely there) --
        /// flagged in the task 05 final report as a contract ambiguity to confirm with the server.
        /// </summary>
        public JArray MembersList(string collectionId) =>
            (JArray)(
                CallRpc("members_list", new { p_collection_id = collectionId }) ?? new JArray()
            );

        /// <summary>
        /// Adds an approved-account email (admin-only). <paramref name="role"/> defaults to
        /// "member" server-side if omitted. Returns the new member row's id, or null when the
        /// email was already approved (the RPC is idempotent: on conflict it does nothing and
        /// returns SQL NULL). NOTE the live RPC returns a bare bigint scalar (a JValue), not an
        /// object -- casting the result to JObject crashed the first real two-instance smoke
        /// test (7 Jul 2026) even though the row committed fine.
        /// </summary>
        public long? MembersAdd(string collectionId, string email, string role = "member")
        {
            var result = CallRpc(
                "members_add",
                new
                {
                    p_collection_id = collectionId,
                    p_email = email,
                    p_role = role,
                }
            );
            return result == null || result.Type == JTokenType.Null
                ? (long?)null
                : result.Value<long>();
        }

        /// <summary>
        /// Removes an approved-account (admin-only; force-unlocks their checkouts server-side).
        /// Task 06 live-verification fix: the deployed RPC's real signature is
        /// <c>members_remove(p_collection_id, p_member_id bigint)</c> -- task 05's original
        /// <c>p_email</c> guess (flagged there as a "contract ambiguity") does not match and fails
        /// with PGRST202 ("could not find the function"). <paramref name="memberId"/> is the row id
        /// from <see cref="MembersList"/>; callers that only have an email must resolve it via a
        /// MembersList lookup first (see SharingApi).
        /// </summary>
        public JObject MembersRemove(string collectionId, long memberId) =>
            (JObject)CallRpc(
                "members_remove",
                new { p_collection_id = collectionId, p_member_id = memberId }
            );

        /// <summary>
        /// Changes an approved-account's role (admin-only; last-admin guard enforced server-side).
        /// Task 06 live-verification fix: same <c>p_member_id</c> mismatch as
        /// <see cref="MembersRemove"/> -- the deployed RPC is
        /// <c>members_set_role(p_collection_id, p_member_id bigint, p_new_role)</c>.
        /// </summary>
        public JObject MembersSetRole(string collectionId, long memberId, string role) =>
            (JObject)CallRpc(
                "members_set_role",
                new
                {
                    p_collection_id = collectionId,
                    p_member_id = memberId,
                    p_new_role = role,
                }
            );

        /// <summary>Union merge (insert ... on conflict do nothing) of palette colors.</summary>
        public JObject AddPaletteColors(string collectionId, string palette, string[] colors) =>
            (JObject)CallRpc(
                "add_palette_colors",
                new
                {
                    p_collection_id = collectionId,
                    p_palette = palette,
                    p_colors = colors,
                }
            );

        /// <summary>
        /// Client-originated history entry. <paramref name="eventType"/> uses the same numeric
        /// values as <see cref="Bloom.History.BookHistoryEventType"/> (extended server-side with
        /// incident types such as WorkPreservedLocally). Exact parameter names are our best
        /// reading of CONTRACTS.md's "log_event(...)" shorthand -- flagged as a contract
        /// ambiguity in the task 05 final report.
        /// </summary>
        public JObject LogEvent(
            string collectionId,
            string bookId,
            int eventType,
            string comment = null
        ) =>
            (JObject)CallRpc(
                "log_event",
                new
                {
                    p_collection_id = collectionId,
                    p_book_id = bookId,
                    p_type = eventType,
                    // The deployed tc.log_event RPC's message parameter is `p_message`, NOT
                    // `p_comment` (CONTRACTS.md's "log_event(...)" shorthand was ambiguous -- this
                    // class's own doc comment flagged the guess). PostgREST matches functions by
                    // argument NAME, so the wrong key made every log_event call 404 (no function
                    // with that signature); the only caller (CloudTeamCollection.
                    // SaveLocalCopyForRecovery) wraps it in a Sentry-only catch, so the
                    // WorkPreservedLocally incident silently never reached the server's history.
                    // Found live by E2E-4's forced-check-in recovery scenario.
                    p_message = comment,
                }
            );

        /// <summary>
        /// Opens (or refreshes, if called again with the same open transaction) a check-in
        /// transaction. <paramref name="bookId"/> null means "first Send of a new book".
        /// <paramref name="files"/> is the diff (added/changed paths only) as
        /// [{path,sha256,size}]. Throws <see cref="CloudCollectionClientException"/> with
        /// LockHeldByOther/BaseVersionSuperseded/NameConflict/ClientOutOfDate on the documented
        /// 409/426s.
        /// </summary>
        public JObject CheckinStart(
            string collectionId,
            string bookId,
            string bookInstanceId,
            string proposedName,
            string baseVersionId,
            string checksum,
            string clientVersion,
            JArray files
        ) =>
            (JObject)CallEdgeFunction(
                "checkin-start",
                new
                {
                    collectionId,
                    bookId,
                    bookInstanceId,
                    proposedName,
                    baseVersionId,
                    checksum,
                    clientVersion,
                    files,
                }
            );

        /// <summary>
        /// Commits a check-in transaction: verifies uploads, writes the version/manifest rows,
        /// releases the lock (unless <paramref name="keepCheckedOut"/>), emits events.
        /// </summary>
        public JObject CheckinFinish(
            string transactionId,
            string comment = null,
            bool keepCheckedOut = false
        ) =>
            (JObject)CallEdgeFunction(
                "checkin-finish",
                new
                {
                    transactionId,
                    comment,
                    keepCheckedOut,
                }
            );

        /// <summary>Abandons an open check-in transaction (nothing was committed).</summary>
        public void CheckinAbort(string transactionId) =>
            CallEdgeFunction("checkin-abort", new { transactionId });

        /// <summary>Read-only STS creds (GetObject + GetObjectVersion) scoped to the collection prefix.</summary>
        public JObject DownloadStart(string collectionId) =>
            (JObject)CallEdgeFunction("download-start", new { collectionId });

        /// <summary>Phase 1 of the two-phase collection-files write (repo-wins on 409 VersionConflict).</summary>
        public JObject CollectionFilesStart(
            string collectionId,
            string groupKey,
            long expectedVersion,
            JArray files
        ) =>
            (JObject)CallEdgeFunction(
                "collection-files-start",
                new
                {
                    collectionId,
                    groupKey,
                    expectedVersion,
                    files,
                }
            );

        /// <summary>Phase 2: bumps the collection-file group version atomically.</summary>
        public JObject CollectionFilesFinish(string transactionId) =>
            (JObject)CallEdgeFunction("collection-files-finish", new { transactionId });

        /// <summary>
        /// The apikey header is required by PostgREST/edge-functions regardless of sign-in state;
        /// the bearer token (when we have one) is what RLS/edge functions actually authorize
        /// against. Deliberately does NOT fail if there is no token yet — an anonymous call will
        /// simply get a 401 from the server, which <see cref="ExecuteWithAuthRetry"/> handles.
        /// </summary>
        private void AddCommonHeaders(RestRequest request)
        {
            request.AddHeader("apikey", _environment.AnonKey);
            var token = _auth.GetAccessTokenOrNull();
            if (!string.IsNullOrEmpty(token))
                request.AddHeader("Authorization", $"Bearer {token}");
        }

        /// <summary>
        /// Runs <paramref name="makeRequest"/> (called twice if a retry-after-refresh happens, so
        /// it must build a fresh RestRequest each time rather than reusing one). On a 401, tries
        /// exactly one <see cref="CloudAuth.TryRefreshOn401"/> and retries once; if that still
        /// comes back 401 (or there was no session to refresh), aborts with a NotSignedIn error
        /// rather than looping — per the task brief, a failed refresh mid-operation must abort
        /// cleanly and surface "please sign in", not block or retry indefinitely.
        /// </summary>
        private JToken ExecuteWithAuthRetry(Func<RestRequest> makeRequest)
        {
            var response = RestClient.Execute(makeRequest());

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (_auth.TryRefreshOn401())
                    response = RestClient.Execute(makeRequest());

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    throw new CloudCollectionClientException(
                        CloudErrorCode.NotSignedIn,
                        "Please sign in to continue."
                    );
            }

            return HandleResponse(response);
        }

        private JToken HandleResponse(IRestResponse response)
        {
            var statusCode = (int)response.StatusCode;
            if (statusCode >= 200 && statusCode < 300)
                return string.IsNullOrWhiteSpace(response.Content)
                    ? null
                    : JToken.Parse(response.Content);

            throw MapError(response);
        }

        /// <summary>
        /// Classifies an error response into a <see cref="CloudCollectionClientException"/>.
        /// Edge functions return CONTRACTS.md's standard envelope `{error: "<Code>", ...extra}`
        /// (built by supabase/functions/_shared/errors.ts) for the documented 409s/426s;
        /// Postgres RPC errors arrive as `{code, message, details, hint}` (PostgREST's shape,
        /// where `code` is the SQLSTATE from the RAISE EXCEPTION in the SQL functions). Anything
        /// we don't recognize maps to <see cref="CloudErrorCode.Unknown"/> with the server's own
        /// message preserved.
        /// </summary>
        private CloudCollectionClientException MapError(IRestResponse response)
        {
            JToken body = null;
            string serverCode = null;
            string message = response.Content;
            try
            {
                if (!string.IsNullOrWhiteSpace(response.Content))
                {
                    body = JToken.Parse(response.Content);
                    // The `error` key is the CONTRACTS.md envelope every edge function actually
                    // uses; `code` is kept as a fallback for PostgREST RPC error bodies. An
                    // earlier version of this method read ONLY `code`, which classified every
                    // documented edge-function 409 (NameConflict/LockHeldByOther/
                    // BaseVersionSuperseded/MissingOrBadUploads/VersionConflict) as Unknown --
                    // found live by E2E-9's same-name race, where PutBookInRepo's NameConflict
                    // retry loop never engaged because its exception filter never matched.
                    serverCode = (string)body["error"] ?? (string)body["code"];
                    message = (string)body["message"] ?? message;
                }
            }
            catch (Exception e)
            {
                // Not JSON (or not an object) — fall back to the raw content/status as the message.
                Logger.WriteEvent(
                    "CloudCollectionClient: error response was not parseable JSON: " + e.Message
                );
            }

            if (response.StatusCode == (HttpStatusCode)426)
                return new CloudCollectionClientException(
                    CloudErrorCode.ClientOutOfDate,
                    message ?? "This version of Bloom is out of date.",
                    body
                );

            switch (serverCode)
            {
                case "LockHeldByOther":
                    return new CloudCollectionClientException(
                        CloudErrorCode.LockHeldByOther,
                        message,
                        body
                    );
                case "BaseVersionSuperseded":
                    return new CloudCollectionClientException(
                        CloudErrorCode.BaseVersionSuperseded,
                        message,
                        body
                    );
                case "NameConflict":
                    return new CloudCollectionClientException(
                        CloudErrorCode.NameConflict,
                        message,
                        body
                    );
                case "MissingOrBadUploads":
                    return new CloudCollectionClientException(
                        CloudErrorCode.MissingOrBadUploads,
                        message,
                        body
                    );
                case "VersionConflict":
                    return new CloudCollectionClientException(
                        CloudErrorCode.VersionConflict,
                        message,
                        body
                    );
                case "ClientOutOfDate":
                    return new CloudCollectionClientException(
                        CloudErrorCode.ClientOutOfDate,
                        message,
                        body
                    );
            }

            if (response.StatusCode == HttpStatusCode.Gone)
                return new CloudCollectionClientException(
                    CloudErrorCode.TransactionExpired,
                    message ?? "The upload transaction has expired.",
                    body
                );

            return new CloudCollectionClientException(
                CloudErrorCode.Unknown,
                message ?? response.StatusDescription,
                body
            );
        }
    }
}
