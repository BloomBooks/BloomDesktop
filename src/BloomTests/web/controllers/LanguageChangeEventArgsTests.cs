using Bloom.WebLibraryIntegration;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
    /// <summary>
    /// Covers how Bloom decides whether the name attached to a language chosen in the language
    /// chooser is the language's own name or one the user typed. Getting that wrong means a custom
    /// name is silently dropped, or a default name is written into the collection settings as
    /// though the user had asked for it.
    /// </summary>
    [TestFixture]
    public class LanguageChangeEventArgsTests
    {
        private static LanguageChangeEventArgs Selection(string defaultName, string desiredName)
        {
            return new LanguageChangeEventArgs
            {
                LanguageTag = "de",
                DefaultName = defaultName,
                DesiredName = desiredName,
            };
        }

        [Test]
        public void IsCustomName_UntouchedName_IsNotCustom()
        {
            var args = Selection("Deutsch", "Deutsch");
            Assert.That(args.DefaultName, Is.EqualTo(args.DesiredName), "test setup");
            Assert.That(args.IsCustomName, Is.False);
        }

        [Test]
        public void IsCustomName_UserTypedSomethingElse_IsCustom()
        {
            Assert.That(Selection("Deutsch", "German").IsCustomName, Is.True);
        }

        // The chooser sends null rather than "" when it has no default name to offer -- an unlisted
        // language has no name of its own, so whatever the user typed is custom by definition.
        [Test]
        public void IsCustomName_NoDefaultOffered_IsCustom()
        {
            Assert.That(Selection(null, "Whatcham").IsCustomName, Is.True);
        }

        [Test]
        public void IsCustomName_BothMissing_IsNotCustom()
        {
            Assert.That(Selection(null, null).IsCustomName, Is.False);
        }

        // Comparison is exact: these are names a person typed, so trimming or case-folding them
        // would silently discard a deliberate choice.
        [TestCase("Deutsch", "deutsch")]
        [TestCase("Deutsch", "Deutsch ")]
        [TestCase("Deutsch", "")]
        public void IsCustomName_DiffersOnlySlightly_IsStillCustom(
            string defaultName,
            string desiredName
        )
        {
            Assert.That(Selection(defaultName, desiredName).IsCustomName, Is.True);
        }

        /// <summary>
        /// Both places that let you choose the collection's sign language -- the Collection
        /// Settings dialog and the Publish tab -- must reach the same answer for the same
        /// selection. They did not before BL-16760: the Publish tab set the writing system's Tag
        /// first and compared DesiredName against the name LibPalaso derived from it, so for a
        /// language the two naming systems disagree about (mzc: LibPalaso "Madagascar Sign
        /// Language", the chooser "Malagasy Sign Language") it recorded an untouched name as
        /// custom. Both now ask IsCustomName, which compares only the two names the chooser sent.
        /// </summary>
        [Test]
        public void IsCustomName_UntouchedName_IsNotCustom_EvenWhenLibPalasoDisagreesAboutTheName()
        {
            // What the chooser sends for mzc when the user accepts the name it offers. The front
            // end sets DesiredName = customDisplayName || defaultName (see languageData.ts), so
            // with nothing typed these are identical by construction.
            var args = new LanguageChangeEventArgs
            {
                LanguageTag = "mzc",
                DefaultName = "Malagasy Sign Language",
                DesiredName = "Malagasy Sign Language",
            };
            Assert.That(args.DefaultName, Is.EqualTo(args.DesiredName), "test setup");

            // The old Publish-tab rule, which is what a caller gets if it reads the name back out
            // of the writing system after setting its Tag. Sanity check that it really does answer
            // differently here -- otherwise this test would pass for the wrong reason.
            const string nameLibPalasoDerivesFromTheTag = "Madagascar Sign Language";
            var oldPublishApiRule = nameLibPalasoDerivesFromTheTag != args.DesiredName;
            Assert.That(oldPublishApiRule, Is.True, "test setup: the two naming systems disagree");

            Assert.That(
                args.IsCustomName,
                Is.False,
                "the user typed nothing, so the name is not custom"
            );
        }

        /// <summary>
        /// And the error ran the other way too: a name the user really did type was recorded as
        /// not custom whenever it happened to match LibPalaso's name for the tag.
        /// </summary>
        [Test]
        public void IsCustomName_TypedNameMatchingLibPalasos_IsStillCustom()
        {
            var args = new LanguageChangeEventArgs
            {
                LanguageTag = "mzc",
                DefaultName = "Malagasy Sign Language",
                DesiredName = "Madagascar Sign Language", // typed by the user
            };

            const string nameLibPalasoDerivesFromTheTag = "Madagascar Sign Language";
            var oldPublishApiRule = nameLibPalasoDerivesFromTheTag != args.DesiredName;
            Assert.That(oldPublishApiRule, Is.False, "test setup: the old rule missed this one");

            Assert.That(args.IsCustomName, Is.True);
        }
    }
}
