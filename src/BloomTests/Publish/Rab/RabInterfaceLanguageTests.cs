using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Bloom.Publish.Rab;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Publish.Rab
{
    /// <summary>
    /// Tests for choosing and setting the app's interface (UI) language for a Reading App Builder
    /// project. RAB requires at least one enabled interface language or it will not build
    /// (BL-16467); Bloom derives a default from the collection's languages (L1, then L2, then L3),
    /// falling back to English, but never overrides an interface language that is already enabled.
    /// </summary>
    [TestFixture]
    public class RabInterfaceLanguageTests
    {
        // Builds a minimal RAB appDef whose <writing-systems> contains the given inner XML.
        private static string AppDefWithInterfaceWritingSystems(string writingSystemsInnerXml)
        {
            return $@"<?xml version='1.0' encoding='utf-8'?>
<app-definition type='RAB' program-version='13.4'>
  <project-name>Sample Project</project-name>
  <app-name lang='default'>Sample Project</app-name>
  <package>org.sil.sample</package>
  <version code='1' name='1.0'/>
  <interface-languages>
    <trait name='use-system-language' value='true'/>
    <writing-systems>{writingSystemsInnerXml}</writing-systems>
  </interface-languages>
  <books id='C01'>
    <book-collection-name>Main Collection</book-collection-name>
  </books>
</app-definition>";
        }

        // Same project but with no <interface-languages> element at all.
        private const string kAppDefWithoutInterfaceLanguages =
            @"<?xml version='1.0' encoding='utf-8'?>
<app-definition type='RAB' program-version='13.4'>
  <project-name>Sample Project</project-name>
  <app-name lang='default'>Sample Project</app-name>
  <package>org.sil.sample</package>
  <version code='1' name='1.0'/>
  <books id='C01'>
    <book-collection-name>Main Collection</book-collection-name>
  </books>
</app-definition>";

        #region ChooseInterfaceLanguage

        [Test]
        public void ChooseInterfaceLanguage_UsesFirstSupportedCollectionLanguage()
        {
            // L1 (Sena) is not a supported interface language, but L2 (Portuguese) is. This is the
            // BL-16467 collection: minority L1, Portuguese L2, no L3.
            var language = RabProjectService.ChooseInterfaceLanguage(new[] { "seh", "pt", null });

            Assert.That(language.Code, Is.EqualTo("pt"));
            Assert.That(language.EnglishName, Is.EqualTo("Portuguese"));
        }

        [Test]
        public void ChooseInterfaceLanguage_PrefersL1WhenItIsSupported()
        {
            var language = RabProjectService.ChooseInterfaceLanguage(new[] { "es", "fr" });

            Assert.That(language.Code, Is.EqualTo("es"));
        }

        [Test]
        public void ChooseInterfaceLanguage_IgnoresRegionAndScriptSubtags()
        {
            var language = RabProjectService.ChooseInterfaceLanguage(new[] { "fr-FR" });

            Assert.That(language.Code, Is.EqualTo("fr"));
        }

        [Test]
        public void ChooseInterfaceLanguage_NoSupportedLanguage_FallsBackToEnglish()
        {
            // A collection whose languages RAB does not have UI translations for.
            var language = RabProjectService.ChooseInterfaceLanguage(new[] { "seh", "tpi", "" });

            Assert.That(language.Code, Is.EqualTo("en"));
        }

        [Test]
        public void ChooseInterfaceLanguage_NoLanguages_FallsBackToEnglish()
        {
            Assert.That(RabProjectService.ChooseInterfaceLanguage(null).Code, Is.EqualTo("en"));
            Assert.That(
                RabProjectService.ChooseInterfaceLanguage(new string[] { null, "" }).Code,
                Is.EqualTo("en")
            );
        }

        [Test]
        public void ChooseInterfaceLanguage_MarksRightToLeftLanguages()
        {
            Assert.That(
                RabProjectService.ChooseInterfaceLanguage(new[] { "ar" }).IsRightToLeft,
                Is.True
            );
            Assert.That(
                RabProjectService.ChooseInterfaceLanguage(new[] { "en" }).IsRightToLeft,
                Is.False
            );
        }

        #endregion

        #region HasEnabledInterfaceLanguage

        private static bool HasEnabledInterfaceLanguage(string appDefContents)
        {
            using var tempFile = TempFile.WithExtension(".appDef");
            RobustFile.WriteAllText(tempFile.Path, appDefContents);
            return RabAppProject.Load(tempFile.Path).HasEnabledInterfaceLanguage();
        }

        [Test]
        public void HasEnabledInterfaceLanguage_EmptyWritingSystems_False()
        {
            Assert.That(
                HasEnabledInterfaceLanguage(AppDefWithInterfaceWritingSystems("")),
                Is.False
            );
        }

        [Test]
        public void HasEnabledInterfaceLanguage_NoInterfaceLanguagesElement_False()
        {
            Assert.That(HasEnabledInterfaceLanguage(kAppDefWithoutInterfaceLanguages), Is.False);
        }

        [Test]
        public void HasEnabledInterfaceLanguage_AnEnabledLanguage_True()
        {
            var appDef = AppDefWithInterfaceWritingSystems(
                "<writing-system code='fr' type='interface' enabled='true'/>"
            );
            Assert.That(HasEnabledInterfaceLanguage(appDef), Is.True);
        }

        [Test]
        public void HasEnabledInterfaceLanguage_OnlyDisabledLanguages_False()
        {
            var appDef = AppDefWithInterfaceWritingSystems(
                "<writing-system code='fr' type='interface' enabled='false'/>"
            );
            Assert.That(HasEnabledInterfaceLanguage(appDef), Is.False);
        }

        [Test]
        public void HasEnabledInterfaceLanguage_EnabledAttributeOmitted_TreatedAsEnabled()
        {
            // Reading App Builder treats a missing enabled attribute as enabled, so we must too.
            var appDef = AppDefWithInterfaceWritingSystems(
                "<writing-system code='fr' type='interface'/>"
            );
            Assert.That(HasEnabledInterfaceLanguage(appDef), Is.True);
        }

        #endregion

        #region SetInterfaceLanguage

        private static XElement SetInterfaceLanguageAndReload(
            string appDefContents,
            string code,
            string englishName,
            bool isRightToLeft
        )
        {
            using var tempFile = TempFile.WithExtension(".appDef");
            RobustFile.WriteAllText(tempFile.Path, appDefContents);

            var project = RabAppProject.Load(tempFile.Path);
            project.SetInterfaceLanguage(code, englishName, isRightToLeft);
            project.Save();

            return XDocument.Load(tempFile.Path).Root;
        }

        private static List<XElement> InterfaceWritingSystems(XElement root)
        {
            return root.Element("interface-languages")
                    ?.Element("writing-systems")
                    ?.Elements("writing-system")
                    .ToList() ?? new List<XElement>();
        }

        [Test]
        public void SetInterfaceLanguage_EmptyWritingSystems_AddsEnabledInterfaceLanguage()
        {
            var root = SetInterfaceLanguageAndReload(
                AppDefWithInterfaceWritingSystems(""),
                "pt",
                "Portuguese",
                false
            );

            var writingSystems = InterfaceWritingSystems(root);
            Assert.That(writingSystems, Has.Count.EqualTo(1));

            var ws = writingSystems.Single();
            Assert.That((string)ws.Attribute("code"), Is.EqualTo("pt"));
            Assert.That((string)ws.Attribute("type"), Is.EqualTo("interface"));
            Assert.That((string)ws.Attribute("enabled"), Is.EqualTo("true"));
            Assert.That(
                ws.Element("trait")?.Attribute("value")?.Value,
                Is.EqualTo("LTR"),
                "Portuguese should be left-to-right."
            );
        }

        [Test]
        public void SetInterfaceLanguage_NoInterfaceLanguagesElement_CreatesItAndEnablesLanguage()
        {
            var root = SetInterfaceLanguageAndReload(
                kAppDefWithoutInterfaceLanguages,
                "pt",
                "Portuguese",
                false
            );

            Assert.That(
                root.Element("interface-languages"),
                Is.Not.Null,
                "The interface-languages element should be created when missing."
            );
            var writingSystems = InterfaceWritingSystems(root);
            Assert.That(writingSystems, Has.Count.EqualTo(1));
            Assert.That((string)writingSystems.Single().Attribute("code"), Is.EqualTo("pt"));
        }

        [Test]
        public void SetInterfaceLanguage_RightToLeftLanguage_MarksDirectionRtl()
        {
            var root = SetInterfaceLanguageAndReload(
                AppDefWithInterfaceWritingSystems(""),
                "ar",
                "Arabic",
                true
            );

            var ws = InterfaceWritingSystems(root).Single();
            Assert.That(ws.Element("trait")?.Attribute("value")?.Value, Is.EqualTo("RTL"));
        }

        [Test]
        public void SetInterfaceLanguage_CalledTwice_DoesNotDuplicate()
        {
            using var tempFile = TempFile.WithExtension(".appDef");
            RobustFile.WriteAllText(tempFile.Path, AppDefWithInterfaceWritingSystems(""));

            var project = RabAppProject.Load(tempFile.Path);
            project.SetInterfaceLanguage("pt", "Portuguese", false);
            project.SetInterfaceLanguage("pt", "Portuguese", false);
            project.Save();

            var writingSystems = InterfaceWritingSystems(XDocument.Load(tempFile.Path).Root);
            Assert.That(
                writingSystems.Count(ws => (string)ws.Attribute("code") == "pt"),
                Is.EqualTo(1),
                "Setting the same interface language twice should not duplicate it."
            );
        }

        #endregion
    }
}
