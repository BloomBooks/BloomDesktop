using System;
using System.Net;
using Bloom.TeamCollection.Cloud;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BloomTests.TeamCollection.Cloud
{
    /// <summary>
    /// Tests for CloudCollectionMonitor's polling: cursor advancement (self-echo suppression falls
    /// out of always polling "since" the last max_event_id seen) and that PollNow can be triggered
    /// on demand (the "on-activation" path) without waiting for the periodic timer.
    /// </summary>
    [TestFixture]
    public class CloudCollectionMonitorTests
    {
        private static CloudEnvironment MakeEnvironment() =>
            new CloudEnvironment(name => name == "BLOOM_CLOUDTC_ANON_KEY" ? "test-anon-key" : null);

        private (CloudCollectionClient client, FakeRestExecutor executor) MakeClient()
        {
            var auth = new CloudAuth(new StubCloudAuthProvider(), new InMemoryCloudTokenStore());
            auth.SignIn("test@somewhere.org", "irrelevant");
            var client = new CloudCollectionClient(MakeEnvironment(), auth);
            var executor = new FakeRestExecutor();
            client.SetRestClientForTests(executor);
            return (client, executor);
        }

        [Test]
        public void PollNow_FetchesSinceLastCursor_AndAdvancesCursor()
        {
            var (client, executor) = MakeClient();
            long? sentSinceEventId = null;
            executor.Handler = req =>
            {
                var bodyParam = req.Parameters.Find(p =>
                    p.Type == RestSharp.ParameterType.RequestBody
                );
                var sentBody = JObject.FromObject(bodyParam.Value);
                sentSinceEventId = (long?)sentBody["p_since_event_id"];
                var response = new JObject { ["books"] = new JArray(), ["max_event_id"] = 42 };
                return FakeResponses.Make(HttpStatusCode.OK, response.ToString());
            };

            JObject received = null;
            var monitor = new CloudCollectionMonitor(
                client,
                "collection-1",
                initialLastSeenEventId: 10,
                onChanges: changes => received = changes
            );

            monitor.PollNow();

            Assert.That(sentSinceEventId, Is.EqualTo(10), "should poll since the initial cursor");
            Assert.That(received, Is.Not.Null);
            Assert.That(
                monitor.LastSeenEventId,
                Is.EqualTo(42),
                "cursor should advance to max_event_id"
            );
        }

        [Test]
        public void PollNow_CalledTwice_SecondCallUsesAdvancedCursor()
        {
            var (client, executor) = MakeClient();
            var sinceValuesSeen = new System.Collections.Generic.List<long?>();
            var nextMaxEventId = 5L;
            executor.Handler = req =>
            {
                var bodyParam = req.Parameters.Find(p =>
                    p.Type == RestSharp.ParameterType.RequestBody
                );
                var sentBody = JObject.FromObject(bodyParam.Value);
                sinceValuesSeen.Add((long?)sentBody["p_since_event_id"]);
                var response = new JObject
                {
                    ["books"] = new JArray(),
                    ["max_event_id"] = nextMaxEventId,
                };
                nextMaxEventId += 5;
                return FakeResponses.Make(HttpStatusCode.OK, response.ToString());
            };

            var monitor = new CloudCollectionMonitor(client, "collection-1", 0, changes => { });

            monitor.PollNow();
            monitor.PollNow();

            Assert.That(sinceValuesSeen, Is.EqualTo(new long?[] { 0, 5 }));
        }

        [Test]
        public void PollNow_OnError_InvokesErrorCallbackRatherThanThrowing()
        {
            var (client, executor) = MakeClient();
            executor.Handler = req => FakeResponses.Make(HttpStatusCode.InternalServerError, "{}");

            Exception caught = null;
            var monitor = new CloudCollectionMonitor(
                client,
                "collection-1",
                0,
                onChanges: changes => Assert.Fail("onChanges should not be called on error"),
                onError: e => caught = e
            );

            Assert.DoesNotThrow(() => monitor.PollNow());
            Assert.That(caught, Is.Not.Null);
        }
    }
}
