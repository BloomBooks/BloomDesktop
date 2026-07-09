using Bloom.TeamCollection.Cloud;
using NUnit.Framework;

namespace BloomTests.TeamCollection.Cloud
{
    [TestFixture]
    public class CloudEnvironmentTests
    {
        [Test]
        public void NoEnvironmentVariablesSet_FallsBackToLocalDevStackDefaults()
        {
            var env = new CloudEnvironment(name => null);

            // These defaults must match server/dev/README.md's "Dev value" column so a plain
            // checkout of Bloom talks to the local dev stack with zero configuration.
            Assert.That(env.SupabaseUrl, Is.EqualTo("http://127.0.0.1:54321"));
            Assert.That(env.S3Endpoint, Is.EqualTo("http://127.0.0.1:9000"));
            Assert.That(env.S3Bucket, Is.EqualTo("bloom-teams-local"));
            Assert.That(env.AuthMode, Is.EqualTo(CloudAuthMode.Dev));
            Assert.That(env.DevUser, Is.Null);
            Assert.That(env.DevPassword, Is.Null);
            // The real-user default: change visibility within a minute at negligible load.
            Assert.That(env.PollInterval, Is.EqualTo(System.TimeSpan.FromSeconds(60)));
        }

        [Test]
        public void PollSeconds_Override_ShortensThePollInterval()
        {
            var env = new CloudEnvironment(name =>
                name == "BLOOM_CLOUDTC_POLL_SECONDS" ? "5" : null
            );

            Assert.That(env.PollInterval, Is.EqualTo(System.TimeSpan.FromSeconds(5)));
        }

        [TestCase("abc")]
        [TestCase("0")]
        [TestCase("-3")]
        public void PollSeconds_Invalid_ThrowsInsteadOfSilentlyIgnoring(string badValue)
        {
            // Fail fast: a typo here silently reverting to 60s would make E2E runs subtly
            // slow instead of obviously misconfigured.
            Assert.Throws<System.ApplicationException>(() =>
                new CloudEnvironment(name => name == "BLOOM_CLOUDTC_POLL_SECONDS" ? badValue : null)
            );
        }

        [Test]
        public void EnvironmentVariables_OverrideCompiledDefaults()
        {
            var values = new System.Collections.Generic.Dictionary<string, string>
            {
                ["BLOOM_CLOUDTC_SUPABASE_URL"] = "https://sandbox.example.org",
                ["BLOOM_CLOUDTC_ANON_KEY"] = "sandbox-anon-key",
                ["BLOOM_CLOUDTC_S3_ENDPOINT"] = "https://s3.sandbox.example.org",
                ["BLOOM_CLOUDTC_S3_BUCKET"] = "sandbox-bucket",
                ["BLOOM_CLOUDTC_AUTH_MODE"] = "cloud",
                ["BLOOM_CLOUDTC_USER"] = "override@dev.local",
                ["BLOOM_CLOUDTC_PASSWORD"] = "pw",
                ["BLOOM_CLOUDTC_FIREBASE_API_KEY"] = "sandbox-firebase-key",
                ["BLOOM_CLOUDTC_FIREBASE_PROJECT_ID"] = "sandbox-firebase-project",
            };

            var env = new CloudEnvironment(name =>
                values.TryGetValue(name, out var value) ? value : null
            );

            Assert.That(env.SupabaseUrl, Is.EqualTo("https://sandbox.example.org"));
            Assert.That(env.AnonKey, Is.EqualTo("sandbox-anon-key"));
            Assert.That(env.S3Endpoint, Is.EqualTo("https://s3.sandbox.example.org"));
            Assert.That(env.S3Bucket, Is.EqualTo("sandbox-bucket"));
            Assert.That(env.AuthMode, Is.EqualTo(CloudAuthMode.Cloud));
            Assert.That(env.DevUser, Is.EqualTo("override@dev.local"));
            Assert.That(env.DevPassword, Is.EqualTo("pw"));
            Assert.That(env.FirebaseApiKey, Is.EqualTo("sandbox-firebase-key"));
            Assert.That(env.FirebaseProjectId, Is.EqualTo("sandbox-firebase-project"));
        }

        [Test]
        public void NoFirebaseEnvironmentVariablesSet_FallsBackToEmptyPlaceholders()
        {
            var env = new CloudEnvironment(name => null);

            // Sanity check: the placeholders must be empty (not null) so callers can safely
            // build a URL query string without a null-check, and so it's obvious in a log that
            // no override was configured yet.
            Assert.That(env.FirebaseApiKey, Is.EqualTo(""));
            Assert.That(env.FirebaseProjectId, Is.EqualTo(""));
        }

        [TestCase("dev", CloudAuthMode.Dev)]
        [TestCase("DEV", CloudAuthMode.Dev)]
        [TestCase("cloud", CloudAuthMode.Cloud)]
        [TestCase("CLOUD", CloudAuthMode.Cloud)]
        [TestCase("", CloudAuthMode.Dev)]
        [TestCase("garbage", CloudAuthMode.Dev)]
        public void AuthMode_ParsesCaseInsensitivelyAndDefaultsToDev(
            string raw,
            CloudAuthMode expected
        )
        {
            var env = new CloudEnvironment(name => name == "BLOOM_CLOUDTC_AUTH_MODE" ? raw : null);

            Assert.That(env.AuthMode, Is.EqualTo(expected));
        }

        [Test]
        public void S3ForcePathStyle_TrueWhenEndpointConfigured()
        {
            // Every configuration this class ever sees has a non-empty S3 endpoint (either the
            // compiled MinIO default or an explicit override) — path-style is what MinIO
            // requires, and real AWS is reached the same way once its endpoint is configured.
            var withDefault = new CloudEnvironment(name => null);
            Assert.That(withDefault.S3ForcePathStyle, Is.True);

            var withOverride = new CloudEnvironment(name =>
                name == "BLOOM_CLOUDTC_S3_ENDPOINT" ? "https://s3.example.org" : null
            );
            Assert.That(withOverride.S3ForcePathStyle, Is.True);
        }

        [Test]
        public void Current_ReflectsSetCurrentForTestsUntilReset()
        {
            CloudEnvironment.ResetCurrentForTests();
            try
            {
                var fake = new CloudEnvironment(name =>
                    name == "BLOOM_CLOUDTC_SUPABASE_URL" ? "https://fake.example.org" : null
                );
                CloudEnvironment.SetCurrentForTests(fake);

                Assert.That(CloudEnvironment.Current, Is.SameAs(fake));
                Assert.That(
                    CloudEnvironment.Current.SupabaseUrl,
                    Is.EqualTo("https://fake.example.org")
                );
            }
            finally
            {
                CloudEnvironment.ResetCurrentForTests();
            }
        }
    }
}
