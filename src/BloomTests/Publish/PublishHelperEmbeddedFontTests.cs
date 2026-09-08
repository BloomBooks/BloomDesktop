using System.Collections.Generic;
using System.IO;
using Bloom.FontProcessing;
using Bloom.Publish;
using Bloom.web;
using Bloom.web.controllers;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Publish
{
    /// <summary>
    /// Tests for the parts of PublishHelper that deal with fonts the user embedded in the book
    /// folder, and for the CSS rewriting that replaces fonts we cannot embed.
    /// </summary>
    [TestFixture]
    public class PublishHelperEmbeddedFontTests
    {
        /// <summary>
        /// A font finder that knows only what the test tells it.
        /// </summary>
        private class StubFontFinder : IFontFinder
        {
            public StubFontFinder()
            {
                FontsWeCantInstall = new HashSet<string>();
            }

            public Dictionary<string, string> FilesForFont = new Dictionary<string, string>();
            public Dictionary<string, FontGroup> FontGroups = new Dictionary<string, FontGroup>();

            public string GetFileForFont(string fontName, string fontStyle, string fontWeight)
            {
                FilesForFont.TryGetValue(fontName, out string result);
                return result;
            }

            public bool NoteFontsWeCantInstall { get; set; }
            public HashSet<string> FontsWeCantInstall { get; }

            public FontGroup GetGroupForFont(string fontName)
            {
                FontGroups.TryGetValue(fontName, out FontGroup result);
                return result;
            }

            public void AddEmbeddedFonts(IDictionary<string, FontGroup> embedded)
            {
                foreach (var kvp in embedded)
                {
                    FontGroups[kvp.Key] = kvp.Value;
                    if (!string.IsNullOrEmpty(kvp.Value.Normal))
                        FilesForFont[kvp.Key] = kvp.Value.Normal;
                }
            }
        }

        [TearDown]
        public void TearDown()
        {
            FontsApi.AvailableFontMetadataDictionary.Clear();
            PublishHelper.ClearFontMetadataMapForTests();
        }

        [Test]
        public void CheckFontsForEmbedding_EmbeddedFamilyShadowsUnsuitableInstalledFont()
        {
            using (var tempFontFolder = new TemporaryFolder("EmbeddedFamilyShadows"))
            {
                var fontPath = Path.Combine(tempFontFolder.Path, "Shadowed.woff2");
                File.WriteAllText(fontPath, "phony woff2");
                var group = new FontGroup { Normal = fontPath };

                // An installed font of the same name whose license forbids embedding.
                FontsApi.AvailableFontMetadataDictionary.Clear();
                var installedPath = Path.Combine(tempFontFolder.Path, "SomeInstalled.ttf");
                File.WriteAllText(installedPath, "phony ttf");
                var installedMeta = new FontMetadata(
                    "Shadowed",
                    new FontGroup { Normal = installedPath }
                );
                installedMeta.SetSuitabilityForTest(FontMetadata.kUnsuitable);
                FontsApi.AvailableFontMetadataDictionary.Add("Shadowed", installedMeta);
                PublishHelper.ClearFontMetadataMapForTests();
                // Sanity check: the metadata we seeded really does forbid embedding.
                Assert.That(
                    FontsApi.AvailableFontMetadataDictionary["Shadowed"].determinedSuitability,
                    Is.EqualTo(FontMetadata.kUnsuitable)
                );

                var fontFileFinder = new StubFontFinder();
                fontFileFinder.FontGroups["Shadowed"] = group;
                fontFileFinder.FilesForFont["Shadowed"] = fontPath;

                var fontsWanted = new HashSet<PublishHelper.FontInfo>
                {
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Shadowed",
                        fontStyle = "normal",
                        fontWeight = "400",
                    },
                };

                PublishHelper.CheckFontsForEmbedding(
                    new NullWebSocketProgress(),
                    fontsWanted,
                    fontFileFinder,
                    out List<string> filesToEmbed,
                    out HashSet<string> badFonts,
                    new[] { "Shadowed" }
                );

                Assert.That(
                    badFonts,
                    Is.Empty,
                    "the book's own font must not be rejected because of a same-named installed font"
                );
                Assert.That(filesToEmbed, Does.Contain(fontPath));
            }
        }

        [Test]
        public void CheckFontsForEmbedding_WithoutEmbeddedFamilies_UnsuitableFontIsRejected()
        {
            using (var tempFontFolder = new TemporaryFolder("UnsuitableStillRejected"))
            {
                var fontPath = Path.Combine(tempFontFolder.Path, "Shadowed.woff2");
                File.WriteAllText(fontPath, "phony woff2");

                FontsApi.AvailableFontMetadataDictionary.Clear();
                var installedPath = Path.Combine(tempFontFolder.Path, "SomeInstalled.ttf");
                File.WriteAllText(installedPath, "phony ttf");
                var installedMeta = new FontMetadata(
                    "Shadowed",
                    new FontGroup { Normal = installedPath }
                );
                installedMeta.SetSuitabilityForTest(FontMetadata.kUnsuitable);
                FontsApi.AvailableFontMetadataDictionary.Add("Shadowed", installedMeta);
                PublishHelper.ClearFontMetadataMapForTests();

                var fontFileFinder = new StubFontFinder();
                fontFileFinder.FontGroups["Shadowed"] = new FontGroup { Normal = fontPath };
                fontFileFinder.FilesForFont["Shadowed"] = fontPath;

                var fontsWanted = new HashSet<PublishHelper.FontInfo>
                {
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Shadowed",
                        fontStyle = "normal",
                        fontWeight = "400",
                    },
                };

                PublishHelper.CheckFontsForEmbedding(
                    new NullWebSocketProgress(),
                    fontsWanted,
                    fontFileFinder,
                    out List<string> filesToEmbed,
                    out HashSet<string> badFonts
                );

                Assert.That(badFonts, Does.Contain("Shadowed"));
                Assert.That(filesToEmbed, Is.Empty);
            }
        }

        [Test]
        public void FixCssReferencesForBadFonts_RewritesUseButNotFontFaceDeclaration()
        {
            using (var folder = new TemporaryFolder("FixCssReferencesForBadFonts"))
            {
                var fontFaceRule =
                    "@font-face {font-family:'Bad'; font-weight:normal; font-style:normal; src:url('Bad.woff2') format('woff2');}";
                var cssPath = Path.Combine(folder.Path, "defaultLangStyles.css");
                RobustFile.WriteAllText(cssPath, fontFaceRule + "\n.foo { font-family: 'Bad'; }\n");

                PublishHelper.FixCssReferencesForBadFonts(
                    folder.Path,
                    "Andika",
                    new HashSet<string> { "Bad" }
                );

                var css = RobustFile.ReadAllText(cssPath);
                Assert.That(
                    css,
                    Does.Contain(fontFaceRule),
                    "the @font-face rule declares the font and must be left alone"
                );
                Assert.That(css, Does.Contain(".foo { font-family: 'Andika'; }"));
                Assert.That(css, Does.Not.Contain(".foo { font-family: 'Bad'; }"));
            }
        }
    }
}
