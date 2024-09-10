using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Collection;
using NUnit.Framework;

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
    }
}
