using System.IO;
using System.Linq;
using Bloom.FontProcessing;
using NUnit.Framework;
using SIL.TestUtilities;

namespace BloomTests.FontProcessing
{
    [TestFixture]
    public class EmbeddedFontsTests
    {
        private TemporaryFolder _folder;

        [SetUp]
        public void Setup()
        {
            _folder = new TemporaryFolder("EmbeddedFontsTests");
        }

        [TearDown]
        public void TearDown()
        {
            _folder.Dispose();
        }

        private string MakeFontFile(string fileName)
        {
            var path = Path.Combine(_folder.Path, fileName);
            // Content is irrelevant; the family/variant come from the filename.
            File.WriteAllText(path, "not really a font");
            return path;
        }

        [Test]
        public void GetEmbeddedFontGroups_GroupsVariantsByFamily()
        {
            // Sanity check: nothing there before we create files.
            Assert.That(EmbeddedFonts.GetEmbeddedFontGroups(_folder.Path), Is.Empty);

            MakeFontFile("Foo-Regular.woff2");
            MakeFontFile("Foo-Bold.woff2");
            MakeFontFile("Foo-Italic.woff2");
            MakeFontFile("Foo-BoldItalic.woff2");

            var groups = EmbeddedFonts.GetEmbeddedFontGroups(_folder.Path);

            Assert.That(groups.Keys, Is.EquivalentTo(new[] { "Foo" }));
            var group = groups["Foo"];
            Assert.That(Path.GetFileName(group.Normal), Is.EqualTo("Foo-Regular.woff2"));
            Assert.That(Path.GetFileName(group.Bold), Is.EqualTo("Foo-Bold.woff2"));
            Assert.That(Path.GetFileName(group.Italic), Is.EqualTo("Foo-Italic.woff2"));
            Assert.That(Path.GetFileName(group.BoldItalic), Is.EqualTo("Foo-BoldItalic.woff2"));
        }

        [Test]
        public void GetEmbeddedFontGroups_BareName_IsNormal()
        {
            MakeFontFile("Foo.woff2");

            var groups = EmbeddedFonts.GetEmbeddedFontGroups(_folder.Path);

            Assert.That(groups.Keys, Is.EquivalentTo(new[] { "Foo" }));
            Assert.That(Path.GetFileName(groups["Foo"].Normal), Is.EqualTo("Foo.woff2"));
            Assert.That(groups["Foo"].Bold, Is.Null);
        }

        [Test]
        public void GetEmbeddedFontGroups_SpaceSeparatedVariant_IsRecognized()
        {
            // The normal file is needed too, or the family is dropped for having no normal face.
            MakeFontFile("Foo.woff2");
            MakeFontFile("Foo Bold.woff2");

            var groups = EmbeddedFonts.GetEmbeddedFontGroups(_folder.Path);

            Assert.That(groups.Keys, Is.EquivalentTo(new[] { "Foo" }));
            Assert.That(Path.GetFileName(groups["Foo"].Bold), Is.EqualTo("Foo Bold.woff2"));
        }

        [Test]
        public void GetEmbeddedFontGroups_HyphenInFamilyButNoVariantSuffix_KeepsWholeName()
        {
            // "Font" is not a recognized variant, so the whole name is the family.
            MakeFontFile("My-Cool-Font.woff2");

            var groups = EmbeddedFonts.GetEmbeddedFontGroups(_folder.Path);

            Assert.That(groups.Keys, Is.EquivalentTo(new[] { "My-Cool-Font" }));
            Assert.That(
                Path.GetFileName(groups["My-Cool-Font"].Normal),
                Is.EqualTo("My-Cool-Font.woff2")
            );
        }

        [Test]
        public void GetEmbeddedFontGroups_IgnoresNonWebFontFiles()
        {
            MakeFontFile("Foo.woff2");
            MakeFontFile("Bar.ttf"); // not a web font format; not embeddable
            MakeFontFile("readme.txt");

            var groups = EmbeddedFonts.GetEmbeddedFontGroups(_folder.Path);

            Assert.That(groups.Keys, Is.EquivalentTo(new[] { "Foo" }));
        }

        [Test]
        public void MakeEmbeddedFontMetadata_IsSuitableAndMarkedAsBookSource()
        {
            MakeFontFile("Foo-Regular.woff2");
            MakeFontFile("Foo-Bold.woff2");
            var group = EmbeddedFonts.GetEmbeddedFontGroups(_folder.Path)["Foo"];

            var metadata = EmbeddedFonts.MakeEmbeddedFontMetadata("Foo", group);

            Assert.That(metadata.name, Is.EqualTo("Foo"));
            Assert.That(metadata.source, Is.EqualTo(FontMetadata.kSourceBook));
            Assert.That(metadata.determinedSuitability, Is.EqualTo(FontMetadata.kOK));
            Assert.That(metadata.fileExtension, Is.EqualTo(".woff2"));
            Assert.That(metadata.variants, Is.EquivalentTo(new[] { "regular", "bold" }));
        }

        [Test]
        public void GetFontFaceDeclarations_EmitsExpectedRules()
        {
            MakeFontFile("Foo-Regular.woff2");
            MakeFontFile("Foo-Bold.woff2");

            var css = EmbeddedFonts.GetFontFaceDeclarations(_folder.Path);

            Assert.That(
                css,
                Does.Contain(
                    "@font-face {font-family:'Foo'; font-weight:400; font-style:normal; src:url('Foo-Regular.woff2') format('woff2');}"
                )
            );
            Assert.That(
                css,
                Does.Contain(
                    "@font-face {font-family:'Foo'; font-weight:700; font-style:normal; src:url('Foo-Bold.woff2') format('woff2');}"
                )
            );
            // No italic file, so no italic rule.
            Assert.That(css, Does.Not.Contain("font-style:italic"));
        }

        [Test]
        public void GetEmbeddedFontGroups_FamilyWithNoNormalFile_IsDropped()
        {
            MakeFontFile("OnlyBold-Bold.woff2");
            MakeFontFile("Complete.woff2");

            var groups = EmbeddedFonts.GetEmbeddedFontGroups(_folder.Path);

            // "OnlyBold" cannot be used: GetFileForFont and FontMetadata both need a normal file.
            Assert.That(groups.Keys, Is.EquivalentTo(new[] { "Complete" }));
        }

        [Test]
        public void GetFontFaceDeclarations_QuoteInName_IsEscaped()
        {
            MakeFontFile("O'Brien.woff2");

            var css = EmbeddedFonts.GetFontFaceDeclarations(_folder.Path);

            Assert.That(
                css,
                Does.Contain(
                    @"@font-face {font-family:'O\'Brien'; font-weight:400; font-style:normal; src:url('O\'Brien.woff2') format('woff2');}"
                )
            );
        }
    }
}
