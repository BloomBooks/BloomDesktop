using System;

namespace Bloom.TeamCollection.Cloud
{
    /// <summary>
    /// Which backend a Cloud Team Collection authenticates against. "Dev" talks to the local
    /// GoTrue instance bundled with the local Supabase dev stack (accepts any email/password);
    /// "Real" is the not-yet-implemented BloomLibrary/Firebase sign-in (Option A/B/C, see the
    /// design doc). See Design/CloudTeamCollections/tasks/03-auth.md.
    /// </summary>
    public enum CloudAuthMode
    {
        Dev,
        Real,
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
        private const string DefaultS3Bucket = "bloom-teams-local";
        private const CloudAuthMode DefaultAuthMode = CloudAuthMode.Dev;

        /// <summary>The Supabase (or PostgREST/GoTrue-compatible) API base URL.</summary>
        public string SupabaseUrl { get; }

        /// <summary>The Supabase anon/public JWT key, sent as the `apikey` header on every call.</summary>
        public string AnonKey { get; }

        /// <summary>The S3-compatible endpoint used for book/collection-file storage.</summary>
        public string S3Endpoint { get; }

        /// <summary>The bucket that holds this deployment's Cloud Team Collection objects.</summary>
        public string S3Bucket { get; }

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
        /// Optional email to silently auto-sign-in as, bypassing any stored session tokens. Set
        /// via BLOOM_CLOUDTC_USER. This is what lets two Bloom instances on one machine run as
        /// two different users (see server/dev/README.md "Two Bloom instances on one machine").
        /// </summary>
        public string DevUser { get; }

        /// <summary>The password to pair with <see cref="DevUser"/>, from BLOOM_CLOUDTC_PASSWORD.</summary>
        public string DevPassword { get; }

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
            S3Bucket = Get("BLOOM_CLOUDTC_S3_BUCKET", DefaultS3Bucket);
            // Real AWS never sets this override; only local/sandbox dev stacks (MinIO) do.
            S3ForcePathStyle = !string.IsNullOrEmpty(S3Endpoint);

            var authModeRaw = Get(
                "BLOOM_CLOUDTC_AUTH_MODE",
                DefaultAuthMode == CloudAuthMode.Dev ? "dev" : "real"
            );
            AuthMode = string.Equals(authModeRaw, "real", StringComparison.OrdinalIgnoreCase)
                ? CloudAuthMode.Real
                : CloudAuthMode.Dev;

            DevUser = Get("BLOOM_CLOUDTC_USER", null);
            DevPassword = Get("BLOOM_CLOUDTC_PASSWORD", null);
        }
    }
}
