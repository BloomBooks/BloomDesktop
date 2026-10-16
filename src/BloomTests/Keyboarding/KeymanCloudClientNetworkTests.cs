using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Keyboarding;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Keyboarding
{
    /// <summary>
    /// Exercises KeymanCloudClient.SearchKeyboardsForLanguage through the HttpClient test seam to pin
    /// the offline/online behavior the Book Making tab's keyboard list depends on (BL-16524): when the
    /// Keyman server is unreachable the search must degrade to an EMPTY list without throwing (so the
    /// chooser still lists Automatic/Off/installed keyboards and never hangs the settings dialog), and
    /// when the server is reachable again the same call must repopulate the cloud list. Together these
    /// are the C# half of the manual "disconnect, confirm no freeze and keyboards still list; reconnect,
    /// confirm the cloud list appears" check. Unlike KeymanCloudClientTests (pure parsers), these drive
    /// the network path, but still never touch the real network — a fake handler stands in for it.
    /// </summary>
    [TestFixture]
    public class KeymanCloudClientNetworkTests
    {
        private const string kFixtureDir = "src/BloomTests/Keyboarding/fixtures";

        [TearDown]
        public void TearDown()
        {
            // The seam sets a static client; restore a real one so the fake doesn't leak into other fixtures.
            KeymanCloudClient.SetHttpClientForTests(new HttpClient());
        }

        // Returns the captured search fixture for any Keyman search request, and 404s anything else.
        private sealed class CannedSearchHandler : HttpMessageHandler
        {
            private readonly string _searchJson;

            public CannedSearchHandler(string searchJson)
            {
                _searchJson = searchJson;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                if (request.RequestUri.ToString().Contains("api.keyman.com/search"))
                    response.Content = new StringContent(_searchJson, Encoding.UTF8);
                else
                    response.StatusCode = HttpStatusCode.NotFound;
                return Task.FromResult(response);
            }
        }

        // Simulates being offline: every request throws, the way an unreachable host does.
        private sealed class AlwaysFailHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                throw new HttpRequestException("simulated offline");
            }
        }

        private static void UseReachableServer()
        {
            var searchJson = RobustFile.ReadAllText(
                FileLocationUtilities.GetFileDistributedWithApplication(
                    kFixtureDir,
                    "search-th.json"
                )
            );
            KeymanCloudClient.SetHttpClientForTests(
                new HttpClient(new CannedSearchHandler(searchJson))
            );
        }

        private static void UseOfflineServer()
        {
            KeymanCloudClient.SetHttpClientForTests(new HttpClient(new AlwaysFailHandler()));
        }

        [Test]
        public void SearchKeyboardsForLanguage_WhenServerReachable_ReturnsPopulatedCloudList()
        {
            UseReachableServer();
            var client = new KeymanCloudClient();

            var results = client.SearchKeyboardsForLanguage("th");

            // The search-th.json fixture holds six Thai keyboards; the top one by finalWeight is the
            // de-facto default the chooser offers first.
            Assert.That(
                results.Count,
                Is.EqualTo(6),
                "should return every keyboard in the fixture"
            );
            Assert.That(
                results.First().Id,
                Is.EqualTo("thai_kedmanee_mattix"),
                "top result should be the highest-finalWeight keyboard"
            );
        }

        [Test]
        public void SearchKeyboardsForLanguage_WhenOffline_ReturnsEmptyListWithoutThrowing()
        {
            UseOfflineServer();
            var client = new KeymanCloudClient();

            // The whole point: a network failure is swallowed as "no results", not surfaced as an
            // exception. An empty (not null) list is what lets the chooser still render its other
            // groups instead of hanging or erroring.
            var results = client.SearchKeyboardsForLanguage("th");

            Assert.That(
                results,
                Is.Not.Null,
                "offline search should return an empty list, not null"
            );
            Assert.That(results, Is.Empty, "offline search should return no cloud keyboards");
        }

        [Test]
        public void SearchKeyboardsForLanguage_OfflineThenReconnected_RepopulatesCloudList()
        {
            // Model the manual test: reachable -> disconnected -> reconnected, all for the same tag,
            // so the only thing changing is network reachability.
            var client = new KeymanCloudClient();

            UseReachableServer();
            var whileOnline = client.SearchKeyboardsForLanguage("th");
            // Sanity: this tag really does have keyboards when the server is reachable, so an empty
            // result below can only be the offline path, not a bad tag.
            Assert.That(
                whileOnline,
                Is.Not.Empty,
                "sanity: the tag has cloud keyboards while the server is reachable"
            );

            UseOfflineServer();
            var whileOffline = client.SearchKeyboardsForLanguage("th");
            Assert.That(
                whileOffline,
                Is.Empty,
                "going offline should empty the cloud list, not crash"
            );

            UseReachableServer();
            var afterReconnect = client.SearchKeyboardsForLanguage("th");
            Assert.That(
                afterReconnect.Count,
                Is.EqualTo(whileOnline.Count),
                "reconnecting should repopulate the cloud list to what it was before"
            );
        }
    }
}
