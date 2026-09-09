using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.FontProcessing;
using Bloom.Publish;
using Bloom.web;
using Bloom.web.controllers;
using BloomTests.FontProcessing;
using NUnit.Framework;
using SIL.IO;
using SIL.Progress;
using SIL.TestUtilities;

namespace BloomTests.Publish
{
    /// <summary>
    /// Tests for the parts of PublishHelper that deal with the fonts Bloom stored as files, and
    /// for the CSS rewriting that replaces fonts we cannot embed.
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
        public void CheckFontsForEmbedding_StoredFamilyShadowsUnsuitableInstalledFont()
        {
            var goodGroup = InstalledTestFonts.FindFamilyWithGoodLicense(out _);
            if (goodGroup == null)
                Assert.Ignore("No font with a known-good license is installed on this computer.");
            using (var tempFontFolder = new TemporaryFolder("StoredFamilyShadows"))
            {
                var fontPath = Path.Combine(tempFontFolder.Path, "Shadowed.ttf");
                File.Copy(goodGroup.Normal, fontPath, true);
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
                    new Dictionary<string, StoredFontGroup>
                    {
                        { "Shadowed", new StoredFontGroup(group, FontMetadata.kSourceCollection) },
                    }
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
        public void CheckFontsForEmbedding_StoredFontWithUnreadableFile_IsRejected()
        {
            using (var tempFontFolder = new TemporaryFolder("StoredFontWithUnreadableFile"))
            {
                var fontPath = Path.Combine(tempFontFolder.Path, "Restricted.ttf");
                File.WriteAllText(fontPath, "not really a font");
                var group = new FontGroup { Normal = fontPath };
                // Sanity check: the file we just wrote really is unusable.
                Assert.That(
                    EmbeddedFonts
                        .MakeEmbeddedFontMetadata(
                            "Restricted",
                            group,
                            FontMetadata.kSourceCollection
                        )
                        .determinedSuitability,
                    Is.EqualTo(FontMetadata.kInvalid)
                );

                FontsApi.AvailableFontMetadataDictionary.Clear();
                PublishHelper.ClearFontMetadataMapForTests();

                var fontFileFinder = new StubFontFinder();
                fontFileFinder.FontGroups["Restricted"] = group;
                fontFileFinder.FilesForFont["Restricted"] = fontPath;

                var fontsWanted = new HashSet<PublishHelper.FontInfo>
                {
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Restricted",
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
                    new Dictionary<string, StoredFontGroup>
                    {
                        {
                            "Restricted",
                            new StoredFontGroup(group, FontMetadata.kSourceCollection)
                        },
                    }
                );

                Assert.That(badFonts, Does.Contain("Restricted"));
                Assert.That(filesToEmbed, Is.Empty);
            }
        }

        /// <summary>
        /// Records what was written to the progress output, with the color of each message.
        /// </summary>
        private class RecordingProgress : GenericProgress
        {
            public readonly List<string> Messages = new List<string>();
            public readonly List<string> RedMessages = new List<string>();

            public override void WriteMessage(string message, params object[] args)
            {
                Messages.Add(string.Format(message, args));
            }

            public override void WriteMessageWithColor(
                string colorName,
                string message,
                params object[] args
            )
            {
                var text = string.Format(message, args);
                Messages.Add(text);
                if (colorName == "Red")
                    RedMessages.Add(text);
            }
        }

        [Test]
        public void ReportFontProblems_ReportsAndBlocksOnlyTheUnsuitableFont()
        {
            using (var tempFontFolder = new TemporaryFolder("ReportFontProblems"))
            {
                var goodPath = Path.Combine(tempFontFolder.Path, "Good.ttf");
                File.WriteAllText(goodPath, "phony ttf");
                var badPath = Path.Combine(tempFontFolder.Path, "Bad.ttf");
                File.WriteAllText(badPath, "phony ttf");
                var good = new FontMetadata("Good", new FontGroup { Normal = goodPath });
                good.SetSuitabilityForTest(FontMetadata.kOK);
                var bad = new FontMetadata("Bad", new FontGroup { Normal = badPath });
                bad.SetSuitabilityForTest(FontMetadata.kUnsuitable);
                // Sanity check: the two fonts really do differ before we call the method.
                Assert.That(good.determinedSuitability, Is.EqualTo(FontMetadata.kOK));
                Assert.That(bad.determinedSuitability, Is.EqualTo(FontMetadata.kUnsuitable));

                var progress = new RecordingProgress();
                var blocking = PublishHelper.ReportFontProblems(
                    new[] { "Good", "Bad" },
                    family => family == "Good" ? good : bad,
                    progress
                );

                Assert.That(blocking, Is.EquivalentTo(new[] { "Bad" }));
                Assert.That(
                    progress.RedMessages.Any(m =>
                        m.Contains("Bloom will not upload this book to BloomLibrary.org")
                    ),
                    Is.True,
                    "the user should be told that the upload is blocked"
                );
                Assert.That(
                    progress.RedMessages.Any(m => m.Contains("Good")),
                    Is.False,
                    "nothing should be reported for a font that is fine"
                );
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
