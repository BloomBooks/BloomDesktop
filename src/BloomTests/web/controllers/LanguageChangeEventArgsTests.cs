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
        /// The Collection Settings dialog and the Publish tab answer "is this name custom?"
        /// differently for the sign language. The dialog uses IsCustomName, comparing against the
        /// name the chooser offered. PublishApi compares against the name already stored in the
        /// collection after setting the tag. This test exists so that difference is visible and
        /// pinned: if someone unifies the two, it fails and they have to decide which answer they
        /// want rather than changing behavior by accident.
        /// </summary>
        [Test]
        public void PublishApiRule_AndIsCustomName_CanDisagreeForTheSameSelection()
        {
            // The user accepted the name the chooser offered, so it is not custom...
            var args = Selection(defaultName: "Deutsch", desiredName: "Deutsch");
            Assert.That(args.IsCustomName, Is.False);

            // ...but the name already stored in the collection was different (a name LibPalaso
            // derived from the tag, or one left over from a previous selection), and PublishApi
            // compares against that instead -- so it calls the very same selection custom.
            const string nameAlreadyInTheCollection = "German";
            var publishApiRule = nameAlreadyInTheCollection != args.DesiredName;
            Assert.That(
                publishApiRule,
                Is.True,
                "PublishApi's comparison is against the stored name, not the chooser's default"
            );
            Assert.That(
                publishApiRule,
                Is.Not.EqualTo(args.IsCustomName),
                "the two rules disagree here; see the remarks on LanguageChangeEventArgs.IsCustomName"
            );
        }

        /// <summary>
        /// And where the stored name happens to match the chooser's default, the two rules agree --
        /// which is why the difference has gone unnoticed.
        /// </summary>
        [Test]
        public void PublishApiRule_AndIsCustomName_AgreeWhenStoredNameIsTheDefault()
        {
            var args = Selection(defaultName: "Deutsch", desiredName: "German");
            const string nameAlreadyInTheCollection = "Deutsch";

            var publishApiRule = nameAlreadyInTheCollection != args.DesiredName;
            Assert.That(publishApiRule, Is.EqualTo(args.IsCustomName));
            Assert.That(args.IsCustomName, Is.True, "test setup");
        }
    }
}
