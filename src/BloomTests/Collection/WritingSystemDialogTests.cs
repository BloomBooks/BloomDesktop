using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Collection;
using NUnit.Framework;

// TODO (default name BL-13703) currently, the Tag setter also automatically sets the name using LibPalasso logic.
// If we make changes to that logic now that we are changing default names with the new
// language chooser, we need to check through the tests in this file
namespace BloomTests.Collection
{
    [TestFixture]
    public class WritingSystemDialogTests
    {
        [OneTimeSetUp]
        public void FixtureSetup()
        {
            SIL.Reporting.ErrorReport.IsOkToInteractWithUser = false;
        }

        private string DefaultLanguageForNames()
        {
            return "en";
        }

        [Test]
        public void UpdateLanguageSettings_0()
        {
            /*
             * original     pending      final list
             * ----------   ----------   ----------
             * [0] = "en"   [0] = "en"   [0] = "en"
             * [1] = "en"   [1] = "en"   [1] = "en"
             * [2] = ""     [2] = ""     [2] = ""
             */
            var languages = new List<WritingSystem>();
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "en", FontName = "Andika" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "en", FontName = "Andika" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "", FontName = "Andika" }
            );

            var pending = new WritingSystem[3];
            pending[0] = new WritingSystem(DefaultLanguageForNames) { Tag = "en" };
            pending[1] = new WritingSystem(DefaultLanguageForNames) { Tag = "en" };
            pending[2] = new WritingSystem(DefaultLanguageForNames) { Tag = "" };

            var fonts = new string[3] { "Andika", "Andika", "Andika" };

            CollectionSettingsDialog.UpdateLanguageSettings(languages, pending, fonts);

            Assert.AreEqual(3, languages.Count);
            Assert.AreEqual("en", languages[0].Tag);
            Assert.AreEqual("Andika", languages[0].FontName);
            Assert.AreEqual("en", languages[1].Tag);
            Assert.AreEqual("Andika", languages[1].FontName);
            Assert.AreEqual("", languages[2].Tag);
            Assert.AreEqual("Andika", languages[2].FontName);
        }

        [Test]
        public void UpdateLanguageSettings_1()
        {
            /*
             * original     pending      final list
             * ----------   ----------   ----------
             * [0] = "en"   [0] = "en"   [0] = "en"
             * [1] = "en"   [1] = "fr"   [1] = "fr"
             * [2] = ""     [2] = "de"   [2] = "de"
             * [3] = "es"                [3] = "es"
             * [4] = "pt"                [4] = "pt"
             */
            var languages = new List<WritingSystem>();
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "en", FontName = "Andika 1" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "en", FontName = "Andika 2" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "", FontName = "Andika 3" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "es", FontName = "Andika 4" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "pt", FontName = "Andika 5" }
            );

            var pending = new WritingSystem[3];
            pending[0] = new WritingSystem(DefaultLanguageForNames) { Tag = "en" };
            pending[1] = new WritingSystem(DefaultLanguageForNames) { Tag = "fr" };
            pending[2] = new WritingSystem(DefaultLanguageForNames) { Tag = "de" };

            var fonts = new string[3] { "Andika", "Andika", "Andika" };

            CollectionSettingsDialog.UpdateLanguageSettings(languages, pending, fonts);

            Assert.AreEqual(5, languages.Count);
            Assert.AreEqual("en", languages[0].Tag);
            Assert.AreEqual("Andika", languages[0].FontName);
            Assert.AreEqual("fr", languages[1].Tag);
            Assert.AreEqual("Andika", languages[1].FontName);
            Assert.AreEqual("de", languages[2].Tag);
            Assert.AreEqual("Andika", languages[2].FontName);
            Assert.AreEqual("es", languages[3].Tag);
            Assert.AreEqual("Andika 4", languages[3].FontName);
            Assert.AreEqual("pt", languages[4].Tag);
            Assert.AreEqual("Andika 5", languages[4].FontName);
        }

        [Test]
        public void UpdateLanguageSettings_2()
        {
            /*
             * original     pending      final list
             * ----------   ----------   ----------
             * [0] = "en"   [0] = "en"   [0] = "en"
             * [1] = "en"   [1] = "fr"   [1] = "fr"
             * [2] = ""     [2] = "de"   [2] = "de"
             * [3] = "fr"                [3] = "es"
             * [4] = "es"                [4] = "pt"
             * [5] = "de"
             * [6] = "pt"
             */
            var languages = new List<WritingSystem>();
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "en", FontName = "Andika 1" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "en", FontName = "Andika 2" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "", FontName = "Andika 3" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "fr", FontName = "Andika 4" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "es", FontName = "Andika 5" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "de", FontName = "Andika 6" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "pt", FontName = "Andika 7" }
            );

            var pending = new WritingSystem[3];
            pending[0] = new WritingSystem(DefaultLanguageForNames) { Tag = "en" };
            pending[1] = new WritingSystem(DefaultLanguageForNames) { Tag = "fr" };
            pending[2] = new WritingSystem(DefaultLanguageForNames) { Tag = "de" };

            var fonts = new string[3] { "Andika", "Andika", "Andika" };

            CollectionSettingsDialog.UpdateLanguageSettings(languages, pending, fonts);

            Assert.AreEqual(5, languages.Count);
            Assert.AreEqual("en", languages[0].Tag);
            Assert.AreEqual("Andika", languages[0].FontName);
            Assert.AreEqual("fr", languages[1].Tag);
            Assert.AreEqual("Andika", languages[1].FontName);
            Assert.AreEqual("de", languages[2].Tag);
            Assert.AreEqual("Andika", languages[2].FontName);
            Assert.AreEqual("es", languages[3].Tag);
            Assert.AreEqual("Andika 5", languages[3].FontName);
            Assert.AreEqual("pt", languages[4].Tag);
            Assert.AreEqual("Andika 7", languages[4].FontName);
        }

        [Test]
        public void UpdateLanguageSettings_3()
        {
            /*
             * original     pending      final list
             * ----------   ----------   ----------
             * [0] = "en"   [0] = "es"   [0] = "es"
             * [1] = "en"   [1] = "pt"   [1] = "pt"
             * [2] = ""     [2] = ""     [2] = ""
             * [3] = "fr"                [3] = "fr"
             * [4] = "es"                [4] = "de"
             * [5] = "de"                [5] = "en"
             * [6] = "pt"
             */
            var languages = new List<WritingSystem>();
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "en", FontName = "Andika 1" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "en", FontName = "Andika 2" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "", FontName = "Andika 3" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "fr", FontName = "Andika 4" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "es", FontName = "Andika 5" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "de", FontName = "Andika 6" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "pt", FontName = "Andika 7" }
            );

            var pending = new WritingSystem[3];
            pending[0] = new WritingSystem(DefaultLanguageForNames) { Tag = "es" };
            pending[1] = new WritingSystem(DefaultLanguageForNames) { Tag = "pt" };
            pending[2] = new WritingSystem(DefaultLanguageForNames) { Tag = "" };

            var fonts = new string[3] { "Andika", "Andika", "Andika" };

            CollectionSettingsDialog.UpdateLanguageSettings(languages, pending, fonts);

            Assert.AreEqual(6, languages.Count);
            Assert.AreEqual("es", languages[0].Tag);
            Assert.AreEqual("Andika", languages[0].FontName);
            Assert.AreEqual("pt", languages[1].Tag);
            Assert.AreEqual("Andika", languages[1].FontName);
            Assert.AreEqual("", languages[2].Tag);
            Assert.AreEqual("Andika", languages[2].FontName);
            Assert.AreEqual("fr", languages[3].Tag);
            Assert.AreEqual("Andika 4", languages[3].FontName);
            Assert.AreEqual("de", languages[4].Tag);
            Assert.AreEqual("Andika 6", languages[4].FontName);
            Assert.AreEqual("en", languages[5].Tag);
            Assert.AreEqual("Andika 1", languages[5].FontName);
        }

        [Test]
        public void UpdateLanguageSettings_4()
        {
            /*
             * original     pending      final list
             * ----------   ----------   ----------
             * [0] = "en"   [0] = "se"   [0] = "se"
             * [1] = "fr"   [1] = "fr"   [1] = "fr"
             * [2] = "es"   [2] = ""     [2] = ""
             * [3] = "de"                [3] = "de"
             * [4] = "pt"                [4] = "pt"
             *                           [5] = "en"
             *                           [6] = "es"
             */
            var languages = new List<WritingSystem>();
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "en", FontName = "Andika 1" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "fr", FontName = "Andika 2" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "es", FontName = "Andika 3" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "de", FontName = "Andika 4" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "pt", FontName = "Andika 5" }
            );

            var pending = new WritingSystem[3];
            pending[0] = new WritingSystem(DefaultLanguageForNames) { Tag = "se" };
            pending[1] = new WritingSystem(DefaultLanguageForNames) { Tag = "fr" };
            pending[2] = new WritingSystem(DefaultLanguageForNames) { Tag = "" };

            var fonts = new string[3] { "Andika", "Andika", "Andika" };

            CollectionSettingsDialog.UpdateLanguageSettings(languages, pending, fonts);

            Assert.AreEqual(7, languages.Count);
            Assert.AreEqual("se", languages[0].Tag);
            Assert.AreEqual("Andika", languages[0].FontName);
            Assert.AreEqual("fr", languages[1].Tag);
            Assert.AreEqual("Andika", languages[1].FontName);
            Assert.AreEqual("", languages[2].Tag);
            Assert.AreEqual("Andika", languages[2].FontName);
            Assert.AreEqual("de", languages[3].Tag);
            Assert.AreEqual("Andika 4", languages[3].FontName);
            Assert.AreEqual("pt", languages[4].Tag);
            Assert.AreEqual("Andika 5", languages[4].FontName);
            Assert.AreEqual("en", languages[5].Tag);
            Assert.AreEqual("Andika 1", languages[5].FontName);
            Assert.AreEqual("es", languages[6].Tag);
            Assert.AreEqual("Andika 3", languages[6].FontName);
        }

        [Test]
        public void UpdateLanguageSettings_5()
        {
            /*
             * original     pending      final list
             * ----------   ----------   ----------
             * [0] = "en"   [0] = "de"   [0] = "de"
             * [1] = "fr"   [1] = "fr"   [1] = "fr"
             * [2] = "es"   [2] = "pt"   [2] = "pt"
             * [3] = "de"                [3] = "en"
             * [4] = "pt"                [4] = "es"
             */
            var languages = new List<WritingSystem>();
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "en", FontName = "Andika 1" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "fr", FontName = "Andika 2" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "es", FontName = "Andika 3" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "de", FontName = "Andika 4" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "pt", FontName = "Andika 5" }
            );

            var pending = new WritingSystem[3];
            pending[0] = new WritingSystem(DefaultLanguageForNames) { Tag = "de" };
            pending[1] = new WritingSystem(DefaultLanguageForNames) { Tag = "fr" };
            pending[2] = new WritingSystem(DefaultLanguageForNames) { Tag = "pt" };

            var fonts = new string[3] { "Andika", "Andika", "Andika" };

            CollectionSettingsDialog.UpdateLanguageSettings(languages, pending, fonts);

            Assert.AreEqual(5, languages.Count);
            Assert.AreEqual("de", languages[0].Tag);
            Assert.AreEqual("Andika", languages[0].FontName);
            Assert.AreEqual("fr", languages[1].Tag);
            Assert.AreEqual("Andika", languages[1].FontName);
            Assert.AreEqual("pt", languages[2].Tag);
            Assert.AreEqual("Andika", languages[2].FontName);
            Assert.AreEqual("en", languages[3].Tag);
            Assert.AreEqual("Andika 1", languages[3].FontName);
            Assert.AreEqual("es", languages[4].Tag);
            Assert.AreEqual("Andika 3", languages[4].FontName);
        }

        [Test]
        public void UpdateLanguageSettings_6()
        {
            /*
             * original     pending      final list
             * ----------   ----------   ----------
             * [0] = "en"   [0] = "fr"   [0] = "fr"
             * [1] = "fr"   [1] = "de"   [1] = "de"
             * [2] = "es"   [2] = "en"   [2] = "en"
             * [3] = "de"                [3] = "pt"
             * [4] = "pt"                [4] = "es"
             */
            var languages = new List<WritingSystem>();
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "en", FontName = "Andika 1" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "fr", FontName = "Andika 2" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "es", FontName = "Andika 3" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "de", FontName = "Andika 4" }
            );
            languages.Add(
                new WritingSystem(DefaultLanguageForNames) { Tag = "pt", FontName = "Andika 5" }
            );

            var pending = new WritingSystem[3];
            pending[0] = new WritingSystem(DefaultLanguageForNames) { Tag = "fr" };
            pending[1] = new WritingSystem(DefaultLanguageForNames) { Tag = "de" };
            pending[2] = new WritingSystem(DefaultLanguageForNames) { Tag = "en" };

            var fonts = new string[3] { "Andika", "Andika", "Andika" };

            CollectionSettingsDialog.UpdateLanguageSettings(languages, pending, fonts);

            Assert.AreEqual(5, languages.Count);
            Assert.AreEqual("fr", languages[0].Tag);
            Assert.AreEqual("Andika", languages[0].FontName);
            Assert.AreEqual("de", languages[1].Tag);
            Assert.AreEqual("Andika", languages[1].FontName);
            Assert.AreEqual("en", languages[2].Tag);
            Assert.AreEqual("Andika", languages[2].FontName);
            Assert.AreEqual("pt", languages[3].Tag);
            Assert.AreEqual("Andika 5", languages[3].FontName);
            Assert.AreEqual("es", languages[4].Tag);
            Assert.AreEqual("Andika 3", languages[4].FontName);
        }

        // The "Collection Language Rtl Overridden" event exists to find the scripts ethnolib gets
        // wrong, so it must count only reading directions the user really did correct and kept.
        // GetRtlOverridesToReport is the decision; the dialog calls it from its OK handler, which
        // is why nothing is reported at all when the user cancels.

        private WritingSystem[] PendingLanguages(params (string tag, bool rtl)[] languages)
        {
            var result = new WritingSystem[3];
            for (var i = 0; i < languages.Length; i++)
                result[i] = new WritingSystem(DefaultLanguageForNames)
                {
                    Tag = languages[i].tag,
                    IsRightToLeft = languages[i].rtl,
                };
            return result;
        }

        [Test]
        public void GetRtlOverridesToReport_UserChangedTheDirection_Reports()
        {
            // ethnolib said Arabic is left-to-right; the user put it right.
            var pending = PendingLanguages(("ar", true));
            var derived = new CollectionSettingsDialog.RtlBaseline[3];
            derived[0] = new CollectionSettingsDialog.RtlBaseline("ar", false);
            // Sanity check: the two disagree, or this test proves nothing.
            Assert.AreNotEqual(
                derived[0].IsRightToLeft,
                pending[0].IsRightToLeft,
                "test data is wrong: nothing was overridden"
            );

            var reported = CollectionSettingsDialog
                .GetRtlOverridesToReport(pending, derived)
                .ToList();

            Assert.AreEqual(1, reported.Count, "should report the one language the user corrected");
            Assert.AreEqual("ar", reported[0].tag);
            Assert.IsTrue(reported[0].rtl, "should report the direction the user ended up with");
        }

        [Test]
        public void GetRtlOverridesToReport_UserChangedItBackAgain_ReportsNothing()
        {
            // Two visits to Script Settings that cancel out. The baseline is what the script
            // gave us, not what the user chose last time, so there is nothing to report.
            var pending = PendingLanguages(("ar", false));
            var derived = new CollectionSettingsDialog.RtlBaseline[3];
            derived[0] = new CollectionSettingsDialog.RtlBaseline("ar", false);

            Assert.IsEmpty(
                CollectionSettingsDialog.GetRtlOverridesToReport(pending, derived).ToList()
            );
        }

        [Test]
        public void GetRtlOverridesToReport_ScriptSettingsNeverOpened_ReportsNothing()
        {
            // No baseline for any slot: the user never questioned what the script chose, whatever
            // those values happen to be.
            var pending = PendingLanguages(("ar", true), ("en", false), ("fr", false));

            Assert.IsEmpty(
                CollectionSettingsDialog
                    .GetRtlOverridesToReport(pending, new CollectionSettingsDialog.RtlBaseline[3])
                    .ToList()
            );
        }

        [Test]
        public void GetRtlOverridesToReport_LanguageReplacedAfterTheBaselineWasTaken_ReportsNothing()
        {
            // The user opened Script Settings on Arabic, then went back and picked English for that
            // slot instead. English's own left-to-right has never been questioned, and comparing it
            // with Arabic's value would invent an override nobody made.
            var pending = PendingLanguages(("en", false));
            var derived = new CollectionSettingsDialog.RtlBaseline[3];
            derived[0] = new CollectionSettingsDialog.RtlBaseline("ar", true);
            // Sanity check: without the tag check these values WOULD look like an override.
            Assert.AreNotEqual(
                derived[0].IsRightToLeft,
                pending[0].IsRightToLeft,
                "test data is wrong: the values must differ for this test to mean anything"
            );

            Assert.IsEmpty(
                CollectionSettingsDialog.GetRtlOverridesToReport(pending, derived).ToList()
            );
        }

        [Test]
        public void GetRtlOverridesToReport_TwoLanguagesCorrected_ReportsBoth()
        {
            var pending = PendingLanguages(("ar", true), ("en", true), ("fr", false));
            var derived = new CollectionSettingsDialog.RtlBaseline[3];
            derived[0] = new CollectionSettingsDialog.RtlBaseline("ar", false);
            derived[1] = new CollectionSettingsDialog.RtlBaseline("en", false);
            // The third language was left alone even though Script Settings was opened on it.
            derived[2] = new CollectionSettingsDialog.RtlBaseline("fr", false);

            var reported = CollectionSettingsDialog
                .GetRtlOverridesToReport(pending, derived)
                .ToList();

            Assert.AreEqual(2, reported.Count);
            Assert.AreEqual(new[] { "ar", "en" }, reported.Select(r => r.tag).ToArray());
        }
    }
}
