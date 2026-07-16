using System;

namespace Bloom.TeamCollection.Cloud
{
    /// <summary>
    /// Which backend a Cloud Team Collection authenticates against. "Dev" talks to the local
    /// GoTrue instance bundled with the local Supabase dev stack (accepts any email/password);
    /// "Cloud" is the real BloomLibrary/Firebase sign-in (Option A, decided 8 Jul 2026 -- see
    /// Design/CloudTeamCollections.md and GOING-LIVE.md Phase 3). The string value ("dev"/
    /// "cloud") is also what travels over the wire in CloudLoginState/sharing/loginState, and
    /// must keep matching BloomBrowserUI's SharingLoginMode type in sharingApi.ts.
    /// </summary>
    public enum CloudAuthMode
    {
        Dev,
        Cloud,
    }

    /// <summary>
    /// The single place that resolves Cloud Team Collection configuration (Supabase URL, anon
    /// key, S3 endpoint/bucket/path-style, auth mode) from the `BLOOM_CLOUDTC_*` environment
    /// variables documented in server/dev/README.md, falling back to compiled defaults that
    /// point at the local dev stack. Nothing else in Bloom should read these environment
    /// variables directly; switching local &lt;-&gt; sandbox &lt;-&gt; production is a matter of
    /// setting environment variables, not changing code.
    /// </summary>
    public class CloudEnvironment
    {
        // Compiled defaults match the "Dev value" column of server/dev/README.md's environment
        // variable table, so a plain checkout of Bloom talks to the local dev stack with no
        // environment configuration at all.
        private const string DefaultSupabaseUrl = "http://127.0.0.1:54321";
        private const string DefaultAnonKey = "";
        private const string DefaultS3Endpoint = "http://127.0.0.1:9000";
        private const CloudAuthMode DefaultAuthMode = CloudAuthMode.Dev;

        // Firebase Web API key (Option A): the compiled default is an empty placeholder (never
        // call the securetoken API before an override is set); the production value is set via
        // GOING-LIVE.md Phase 3.5's client-defaults change, sandbox/dev via the env-var
        // override below.
        private const string DefaultFirebaseApiKey = "";

        /// <summary>The Supabase (or PostgREST/GoTrue-compatible) API base URL.</summary>
        public string SupabaseUrl { get; }

        /// <summary>The Supabase anon/public JWT key, sent as the `apikey` header on every call.</summary>
        public string AnonKey { get; }

        /// <summary>The S3-compatible endpoint used for book/collection-file storage. NOTE:
        /// there is deliberately no bucket setting here — every bucket name arrives in server
        /// responses (checkin-start/download-start's s3 blocks), never from client config.</summary>
        public string S3Endpoint { get; }

        /// <summary>
        /// True when the S3 endpoint requires path-style requests (http://host/bucket/key), as
        /// MinIO does. Real AWS uses virtual-hosted style. Currently true whenever a non-empty
        /// S3 endpoint override is configured (i.e. we are NOT talking to real AWS), since only
        /// local/sandbox dev stacks set BLOOM_CLOUDTC_S3_ENDPOINT explicitly.
        /// </summary>
        public bool S3ForcePathStyle { get; }

        /// <summary>Which auth provider CloudAuth should use. See <see cref="CloudAuthMode"/>.</summary>
        public CloudAuthMode AuthMode { get; }

        /// <summary>
        /// The Firebase Web API key used to call the Google securetoken refresh endpoint
        /// (`BLOOM_CLOUDTC_FIREBASE_API_KEY`). Firebase Web API keys are not secret (they only
        /// identify the project to Google's client APIs; the actual authorization is the
        /// refresh/ID token itself), so committing a real default here later is fine -- see
        /// GOING-LIVE.md Phase 3.5.
        /// </summary>
        public string FirebaseApiKey { get; }

        /// <summary>
        /// Optional email to silently auto-sign-in as, bypassing any stored session tokens. Set
        /// via BLOOM_CLOUDTC_USER. This is what lets two Bloom instances on one machine run as
        /// two different users (see server/dev/README.md "Two Bloom instances on one machine").
        /// </summary>
        public string DevUser { get; }

        /// <summary>The password to pair with <see cref="DevUser"/>, from BLOOM_CLOUDTC_PASSWORD.</summary>
        public string DevPassword { get; }

        /// <summary>
        /// How often CloudCollectionMonitor polls the server for remote changes, from
        /// BLOOM_CLOUDTC_POLL_SECONDS. The 60s default is right for real users (change
        /// visibility within a minute at negligible server load); E2E tests and hands-on
        /// testing of a freshly-deployed server want a much shorter interval so cross-instance
        /// changes show up promptly. Fail-fast on an unparsable/non-positive value: a silently
        /// ignored typo here would make tests subtly slow instead of obviously misconfigured.
        /// </summary>
        public TimeSpan PollInterval { get; }

        /// <summary>
        /// The one process-wide instance, built from the real environment the first time it is
        /// asked for. Tests should use the constructor directly (with a fake variable lookup)
        /// rather than touching this singleton.
        /// </summary>
        private static CloudEnvironment _current;
        public static CloudEnvironment Current => _current ?? (_current = FromEnvironment());

        /// <summary>Rebuilds <see cref="Current"/> from the real process environment variables.</summary>
        public static CloudEnvironment FromEnvironment() =>
            new CloudEnvironment(Environment.GetEnvironmentVariable);

        /// <summary>Test-only hook: force <see cref="Current"/> to a specific instance.</summary>
        public static void SetCurrentForTests(CloudEnvironment environment) =>
            _current = environment;

        /// <summary>Test-only hook: forget any override so the next access reads real env vars again.</summary>
        public static void ResetCurrentForTests() => _current = null;

        /// <summary>
        /// Builds a CloudEnvironment by asking <paramref name="getEnvironmentVariable"/> for each
        /// BLOOM_CLOUDTC_* variable. Taking the lookup as a delegate (rather than reading
        /// System.Environment directly) is what makes this class trivial to unit test.
        /// </summary>
        public CloudEnvironment(Func<string, string> getEnvironmentVariable)
        {
            string Get(string name, string fallback) =>
                string.IsNullOrEmpty(getEnvironmentVariable(name))
                    ? fallback
                    : getEnvironmentVariable(name);

            SupabaseUrl = Get("BLOOM_CLOUDTC_SUPABASE_URL", DefaultSupabaseUrl);
            AnonKey = Get("BLOOM_CLOUDTC_ANON_KEY", DefaultAnonKey);
            S3Endpoint = Get("BLOOM_CLOUDTC_S3_ENDPOINT", DefaultS3Endpoint);

            var pollSecondsRaw = Get("BLOOM_CLOUDTC_POLL_SECONDS", "60");
            if (!int.TryParse(pollSecondsRaw, out var pollSeconds) || pollSeconds <= 0)
                throw new ApplicationException(
                    $"BLOOM_CLOUDTC_POLL_SECONDS must be a positive whole number of seconds; got '{pollSecondsRaw}'."
                );
            PollInterval = TimeSpan.FromSeconds(pollSeconds);
            // Real AWS never sets this override; only local/sandbox dev stacks (MinIO) do.
            S3ForcePathStyle = !string.IsNullOrEmpty(S3Endpoint);

            var authModeRaw = Get(
                "BLOOM_CLOUDTC_AUTH_MODE",
                DefaultAuthMode == CloudAuthMode.Dev ? "dev" : "cloud"
            );
            AuthMode = string.Equals(authModeRaw, "cloud", StringComparison.OrdinalIgnoreCase)
                ? CloudAuthMode.Cloud
                : CloudAuthMode.Dev;

            DevUser = Get("BLOOM_CLOUDTC_USER", null);
            DevPassword = Get("BLOOM_CLOUDTC_PASSWORD", null);
            FirebaseApiKey = Get("BLOOM_CLOUDTC_FIREBASE_API_KEY", DefaultFirebaseApiKey);
        }
    }
}
