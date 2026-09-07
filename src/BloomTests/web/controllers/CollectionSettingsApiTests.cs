using Bloom.web.controllers;
using Newtonsoft.Json;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
    [TestFixture]
    public class CollectionSettingsApiTests
    {
        // A language display name is arbitrary user text. Apostrophes are common in real language
        // names, and BL-16209 showed that a user can put anything at all in there, including the
        // characters that break a hand-built JSON string.
        private const string kNastyDisplayName = "Unsafe ~!@#$%^&*()_-+={}[]\\|:;\"'<>.?/";

        /// <summary>
        /// BL-16209: the languageData reply used to be assembled by string interpolation, so a
        /// display name containing a double quote or a backslash produced invalid JSON. The
        /// collection tab then read undefined for languageName, threw, and left the user with a
        /// blank screen they could not get past.
        /// </summary>
        [Test]
        public void MakeLanguageDataJson_NameHasJsonBreakingCharacters_ProducesValidParseableJson()
        {
            // Sanity check the test data: this only proves anything if the name really does
            // contain the characters that JSON has to escape.
            Assert.That(
                kNastyDisplayName,
                Does.Contain("\"").And.Contain("\\"),
                "Test data should contain a double quote and a backslash"
            );

            var json = CollectionSettingsApi.MakeLanguageDataJson(
                kNastyDisplayName,
                "qaa-BA-x-Unsafeam"
            );

            dynamic result = JsonConvert.DeserializeObject(json);
            Assert.That((string)result.languageName, Is.EqualTo(kNastyDisplayName));
            Assert.That((string)result.languageCode, Is.EqualTo("qaa-BA-x-Unsafeam"));
        }

        /// <summary>
        /// The clients treat languageName as a string (BooksOnBlorgProgressBar asks it for its
        /// .length), and the hand-built string this replaced turned a null name into "". Keep
        /// doing that, so a null can't reintroduce the BL-16209 crash by another route.
        /// </summary>
        [Test]
        public void MakeLanguageDataJson_NullName_ProducesEmptyStringNotNull()
        {
            var json = CollectionSettingsApi.MakeLanguageDataJson(null, null);

            dynamic result = JsonConvert.DeserializeObject(json);
            Assert.That((string)result.languageName, Is.EqualTo(""));
            Assert.That((string)result.languageCode, Is.EqualTo(""));
        }

        [Test]
        public void MakeLanguageDataJson_OrdinaryName_ProducesExpectedFields()
        {
            var json = CollectionSettingsApi.MakeLanguageDataJson("Kaqchikel", "cak");

            dynamic result = JsonConvert.DeserializeObject(json);
            Assert.That((string)result.languageName, Is.EqualTo("Kaqchikel"));
            Assert.That((string)result.languageCode, Is.EqualTo("cak"));
        }
    }
}
