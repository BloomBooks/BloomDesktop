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
    /// RestSharp client for Cloud Team Collection Postgres RPCs (PostgREST) and edge functions,
    /// per CONTRACTS.md. Modeled on <see cref="Bloom.WebLibraryIntegration.BloomLibraryBookApiClient"/>.
    /// Injects the bearer token from <see cref="CloudAuth"/> on every call, retries once via
    /// <see cref="CloudAuth.TryRefreshOn401"/> on a 401, and maps error responses to a
    /// <see cref="CloudCollectionClientException"/> with a typed <see cref="CloudErrorCode"/>.
    /// This class only owns the transport; the RPC/edge-function-specific methods (get_collection_state,
    /// checkout_book, checkin-start, etc.) are built on top of it by later tasks.
    /// </summary>
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
                request.AddJsonBody(parametersWithPPrefixedKeys ?? new object());
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
                request.AddJsonBody(body ?? new object());
                return request;
            });
        }

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
        /// Postgres RPC errors arrive as `{code, message, details, hint}` (the RAISE EXCEPTION
        /// `code`/message set in the SQL functions); edge functions are expected to return
        /// `{code: "LockHeldByOther", ...}`-shaped bodies for the documented 409s and a plain
        /// error for 426 ClientOutOfDate. Anything we don't recognize maps to
        /// <see cref="CloudErrorCode.Unknown"/> with the server's own message preserved.
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
                    serverCode = (string)body["code"];
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
