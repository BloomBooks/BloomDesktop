using System.Linq;
using Bloom.Keyboarding;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Keyboarding
{
    /// <summary>
    /// Tests the pure JSON-parsing methods of KeymanCloudClient against canned fixtures captured
    /// from the live Keyman API (see the fixtures/ folder; fetched 2026-07-09). These never touch
    /// the network.
    /// </summary>
    [TestFixture]
    public class KeymanCloudClientTests
    {
        private const string kFixtureDir = "src/BloomTests/Keyboarding/fixtures";

        private static string GetFixture(string name)
        {
            return RobustFile.ReadAllText(
                FileLocationUtilities.GetFileDistributedWithApplication(kFixtureDir, name)
            );
        }

        [Test]
        public void ParseSearchResults_RealFixture_OrdersByFinalWeightDescending()
        {
            var json = GetFixture("search-th.json");

            var results = KeymanCloudClient.ParseSearchResults(json);

            // Sanity: the fixture has several keyboards.
            Assert.That(results.Count, Is.EqualTo(6), "fixture keyboard count");

            // The de-facto default is the highest finalWeight.
            Assert.That(results.First().Id, Is.EqualTo("thai_kedmanee_mattix"), "top result id");
            Assert.That(
                results.First().FinalWeight,
                Is.EqualTo(6.087596335232384).Within(0.000001),
                "top result finalWeight"
            );
            Assert.That(results.First().Name, Is.EqualTo("Thai Kedmanee (Mattix)"));
            Assert.That(results.First().Downloads, Is.EqualTo(161), "top result downloads");

            // Verify the whole list is sorted descending by finalWeight.
            var weights = results.Select(r => r.FinalWeight).ToList();
            Assert.That(
                weights,
                Is.EqualTo(weights.OrderByDescending(w => w).ToList()),
                "results should be sorted by finalWeight descending"
            );
        }

        [Test]
        public void ParseSearchResults_UnsortedInput_ReordersByFinalWeight()
        {
            // Deliberately out of order so a no-op parser would fail this test.
            var json =
                @"{ ""keyboards"": [
                    { ""id"": ""low"",  ""name"": ""Low"",  ""match"": { ""finalWeight"": 1.0, ""downloads"": 5 } },
                    { ""id"": ""high"", ""name"": ""High"", ""match"": { ""finalWeight"": 9.0, ""downloads"": 50 } },
                    { ""id"": ""mid"",  ""name"": ""Mid"",  ""match"": { ""finalWeight"": 4.0, ""downloads"": 20 } }
                ] }";

            var results = KeymanCloudClient.ParseSearchResults(json);

            Assert.That(
                results.Select(r => r.Id),
                Is.EqualTo(new[] { "high", "mid", "low" }),
                "should be reordered by finalWeight descending"
            );
        }

        [Test]
        public void ParseSearchResults_MissingFields_ToleratedWithDefaults()
        {
            // An entry with no match block and no name should still parse, using defaults, while an
            // entry with no id is dropped.
            var json =
                @"{ ""keyboards"": [
                    { ""id"": ""noMatch"" },
                    { ""name"": ""NoId keyboard"" }
                ] }";

            var results = KeymanCloudClient.ParseSearchResults(json);

            Assert.That(results.Count, Is.EqualTo(1), "entry without an id should be dropped");
            Assert.That(results[0].Id, Is.EqualTo("noMatch"));
            Assert.That(results[0].Name, Is.EqualTo("noMatch"), "name falls back to id");
            Assert.That(
                results[0].FinalWeight,
                Is.EqualTo(0.0),
                "missing finalWeight defaults to 0"
            );
            Assert.That(results[0].Downloads, Is.EqualTo(0), "missing downloads defaults to 0");
        }

        [Test]
        public void ParseSearchResults_NoKeyboardsProperty_ReturnsEmpty()
        {
            var results = KeymanCloudClient.ParseSearchResults(
                @"{ ""message"": ""nothing here"" }"
            );
            Assert.That(results, Is.Empty);
        }

        [Test]
        public void ParseDownloadInfo_RealFixture_BuildsJsUrlAndFonts()
        {
            var json = GetFixture("download-sil_myanmar_my3-my.json");

            var info = KeymanCloudClient.ParseDownloadInfo(json, "sil_myanmar_my3", "my");

            Assert.That(info, Is.Not.Null);
            Assert.That(info.KeyboardId, Is.EqualTo("sil_myanmar_my3"));
            Assert.That(info.LanguageTag, Is.EqualTo("my"));
            Assert.That(info.Version, Is.EqualTo("1.7.5"));
            Assert.That(
                info.JsUrl,
                Is.EqualTo(
                    "https://s.keyman.com/keyboard/sil_myanmar_my3/1.7.5/sil_myanmar_my3-1.7.5.js"
                ),
                "jsUrl = keyboardBaseUri + filename"
            );

            Assert.That(info.FontInfo, Is.Not.Null);
            Assert.That(info.FontInfo.Family, Is.EqualTo("Pyidaungsu"));
            Assert.That(info.FontInfo.FileNames, Is.EqualTo(new[] { "Pyidaungsu-Regular.ttf" }));
            Assert.That(
                info.FontInfo.Urls,
                Is.EqualTo(new[] { "https://s.keyman.com/font/deploy/Pyidaungsu-Regular.ttf" }),
                "font url = fontBaseUri + source file name"
            );

            Assert.That(info.OskFontInfo, Is.Not.Null);
            Assert.That(info.OskFontInfo.Family, Is.EqualTo("Pyidaungsu"));
            Assert.That(info.OskFontInfo.FileNames, Is.EqualTo(new[] { "Pyidaungsu-Regular.ttf" }));
        }

        [Test]
        public void ParseDownloadInfo_KeyboardNotFound_ReturnsNull()
        {
            // The real API responds with this shape (and no "keyboard" block) for a bad id/lang.
            var info = KeymanCloudClient.ParseDownloadInfo(
                @"{ ""message"": ""Keyboard not found"" }",
                "no_such_keyboard",
                "xx"
            );
            Assert.That(info, Is.Null);
        }

        [Test]
        public void ParseDownloadInfo_NoFonts_LeavesFontInfoNull()
        {
            // A keyboard that specifies no fonts (like thai_kedmanee) should parse with null fonts.
            var json =
                @"{
                    ""options"": { ""keyboardBaseUri"": ""https://s.keyman.com/keyboard/"", ""fontBaseUri"": ""https://s.keyman.com/font/deploy/"" },
                    ""keyboard"": {
                        ""id"": ""thai_kedmanee"",
                        ""version"": ""1.0"",
                        ""filename"": ""thai_kedmanee/1.0/thai_kedmanee-1.0.js"",
                        ""languages"": [ { ""id"": ""th"", ""name"": ""Thai"" } ]
                    }
                }";

            var info = KeymanCloudClient.ParseDownloadInfo(json, "thai_kedmanee", "th");

            Assert.That(info, Is.Not.Null);
            Assert.That(
                info.JsUrl,
                Is.EqualTo("https://s.keyman.com/keyboard/thai_kedmanee/1.0/thai_kedmanee-1.0.js")
            );
            Assert.That(info.FontInfo, Is.Null, "no font block -> null FontInfo");
            Assert.That(info.OskFontInfo, Is.Null, "no oskFont block -> null OskFontInfo");
        }

        [Test]
        public void SearchKeyboardsForLanguage_NullOrEmptyTag_Throws()
        {
            var client = new KeymanCloudClient();
            Assert.That(
                () => client.SearchKeyboardsForLanguage(null),
                Throws.ArgumentException,
                "null tag is a programmer error and should throw"
            );
            Assert.That(() => client.SearchKeyboardsForLanguage("  "), Throws.ArgumentException);
        }

        [Test]
        public void BuildDownloadInfoUrl_IncludesVendoredEngineVersion()
        {
            // Regression guard: without an explicit `version`, the cloud API filters for engine
            // version "2.0" and 404s any keyboard needing a modern engine (e.g. thai_kedmanee_mattix
            // needs 17.0). That bug made keyboard downloads fail silently for most current keyboards.
            var url = KeymanCloudClient.BuildDownloadInfoUrl("thai_kedmanee_mattix", "th");
            Assert.That(
                url,
                Is.EqualTo(
                    "https://api.keyman.com/cloud/4.0/keyboards/thai_kedmanee_mattix/th"
                        + "?languageidtype=bcp47&version="
                        + KeymanCloudClient.kVendoredEngineVersion
                )
            );
            Assert.That(
                KeymanCloudClient.kVendoredEngineVersion,
                Is.Not.EqualTo("2.0"),
                "the whole point is to override the API's ancient default"
            );
        }

        [Test]
        public void GetDownloadInfo_BadArgs_Throw()
        {
            var client = new KeymanCloudClient();
            Assert.That(() => client.GetDownloadInfo(null, "th"), Throws.ArgumentException);
            Assert.That(
                () => client.GetDownloadInfo("thai_kedmanee", ""),
                Throws.ArgumentException
            );
        }
    }
}
