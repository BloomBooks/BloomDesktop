using Bloom.Book;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BloomTests.Book
{
    [TestFixture]
    public class BloomLibraryStatusTests
    {
        /// <summary>
        /// The status of each book is built while a collection loads, often during startup, so it
        /// must keep the ids it needs and build the Bloom Library URL only when asked (BL-16726).
        /// </summary>
        [Test]
        public void FromDynamicJson_ReadsFieldsAndBuildsDetailPageUrlOnRequest()
        {
            dynamic bookRecord = JObject.Parse(
                @"{""id"":""rec123"",""instanceId"":""inst456"",""draft"":true,""inCirculation"":false,""harvestState"":""Done""}"
            );

            var status = BloomLibraryStatus.FromDynamicJson(bookRecord, true);

            Assert.That(status.Draft, Is.True);
            Assert.That(status.NotInCirculation, Is.True);
            Assert.That(status.HarvesterState, Is.EqualTo(HarvesterState.Done));
            string url = status.GetBloomLibraryBookUrl();
            Assert.That(url, Does.EndWith("/book/rec123"));
            Assert.That(url, Does.StartWith("http"));
        }

        [Test]
        public void ForMultipleUploads_BuildsListingUrlForInstanceId()
        {
            var status = BloomLibraryStatus.ForMultipleUploads("inst456", true);

            Assert.That(status.HarvesterState, Is.EqualTo(HarvesterState.Multiple));
            string url = status.GetBloomLibraryBookUrl();
            Assert.That(url, Does.EndWith(":search:bookInstanceId%253Ainst456"));
            Assert.That(url, Does.StartWith("http"));
        }

        /// <summary>
        /// BookCollection compares statuses to decide whether a book's badge needs updating,
        /// so two statuses built from the same record must be equal, and a different record must not be.
        /// </summary>
        [Test]
        public void Equality_DependsOnIdsAndState()
        {
            const string json =
                @"{""id"":""rec123"",""instanceId"":""inst456"",""harvestState"":""done""}";
            dynamic record1 = JObject.Parse(json);
            dynamic record2 = JObject.Parse(json);
            dynamic otherRecord = JObject.Parse(json.Replace("rec123", "rec999"));

            BloomLibraryStatus status1 = BloomLibraryStatus.FromDynamicJson(record1, false);
            BloomLibraryStatus status2 = BloomLibraryStatus.FromDynamicJson(record2, false);
            BloomLibraryStatus other = BloomLibraryStatus.FromDynamicJson(otherRecord, false);

            Assert.That(status1 == status2, Is.True, "same record should give equal statuses");
            Assert.That(status1 != other, Is.True, "different record ids should differ");
            Assert.That(
                status1 != BloomLibraryStatus.ForMultipleUploads("inst456", false),
                Is.True,
                "a single upload should differ from multiple uploads of the same instance"
            );
        }
    }
}
