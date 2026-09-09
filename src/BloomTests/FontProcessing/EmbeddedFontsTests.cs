using System;
using System.Collections.Generic;
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
        private TemporaryFolder _collection;
        private string _bookFolder;
        private string _collectionFontsFolder;

        [SetUp]
        public void Setup()
        {
            _collection = new TemporaryFolder("EmbeddedFontsTests");
            _bookFolder = Path.Combine(_collection.Path, "My Book");
            Directory.CreateDirectory(_bookFolder);
            _collectionFontsFolder = Path.Combine(_collection.Path, "fonts");
        }

        [TearDown]
        public void TearDown()
        {
            StoredFontMetadataCache.ClearForTests();
            _collection.Dispose();
        }

        /// <summary>
        /// Write a file whose content is not a font. Everything the folder scan does comes from the
        /// filename, so the content only matters to the tests that read font metadata.
        /// </summary>
        private string MakeFontFile(string folder, string fileName)
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, fileName);
            File.WriteAllText(path, "not really a font");
            return path;
        }

        private string MakeBookFontFile(string fileName) => MakeFontFile(_bookFolder, fileName);

        private string MakeCollectionFontFile(string fileName) =>
            MakeFontFile(_collectionFontsFolder, fileName);

        [Test]
        public void GetCollectionFontsFolder_IsBesideTheBookFolder()
        {
            Assert.That(
                EmbeddedFonts.GetCollectionFontsFolder(_bookFolder),
                Is.EqualTo(_collectionFontsFolder)
            );
        }

        [Test]
        public void GetEmbeddedFontGroups_GroupsVariantsByFamily()
        {
            // Sanity check: nothing there before we create files.
            Assert.That(EmbeddedFonts.GetEmbeddedFontGroups(_bookFolder), Is.Empty);

            MakeBookFontFile("Foo-Regular.ttf");
            MakeBookFontFile("Foo-Bold.ttf");
            MakeBookFontFile("Foo-Italic.ttf");
            MakeBookFontFile("Foo-BoldItalic.ttf");

            var groups = EmbeddedFonts.GetEmbeddedFontGroups(_bookFolder);

            Assert.That(groups.Keys, Is.EquivalentTo(new[] { "Foo" }));
            var group = groups["Foo"];
            Assert.That(Path.GetFileName(group.Normal), Is.EqualTo("Foo-Regular.ttf"));
            Assert.That(Path.GetFileName(group.Bold), Is.EqualTo("Foo-Bold.ttf"));
            Assert.That(Path.GetFileName(group.Italic), Is.EqualTo("Foo-Italic.ttf"));
            Assert.That(Path.GetFileName(group.BoldItalic), Is.EqualTo("Foo-BoldItalic.ttf"));
        }

        [Test]
        public void GetEmbeddedFontGroups_BareName_IsNormal()
        {
            MakeBookFontFile("Foo.otf");

            var groups = EmbeddedFonts.GetEmbeddedFontGroups(_bookFolder);

            Assert.That(groups.Keys, Is.EquivalentTo(new[] { "Foo" }));
            Assert.That(Path.GetFileName(groups["Foo"].Normal), Is.EqualTo("Foo.otf"));
            Assert.That(groups["Foo"].Bold, Is.Null);
        }

        [Test]
        public void GetEmbeddedFontGroups_SpaceSeparatedVariant_IsRecognized()
        {
            // The normal file is needed too, or the family is dropped for having no normal face.
            MakeBookFontFile("Foo.ttf");
            MakeBookFontFile("Foo Bold.ttf");

            var groups = EmbeddedFonts.GetEmbeddedFontGroups(_bookFolder);

            Assert.That(groups.Keys, Is.EquivalentTo(new[] { "Foo" }));
            Assert.That(Path.GetFileName(groups["Foo"].Bold), Is.EqualTo("Foo Bold.ttf"));
        }

        [Test]
        public void GetEmbeddedFontGroups_HyphenInFamilyButNoVariantSuffix_KeepsWholeName()
        {
            // "Font" is not a recognized variant, so the whole name is the family.
            MakeBookFontFile("My-Cool-Font.ttf");

            var groups = EmbeddedFonts.GetEmbeddedFontGroups(_bookFolder);

            Assert.That(groups.Keys, Is.EquivalentTo(new[] { "My-Cool-Font" }));
            Assert.That(
                Path.GetFileName(groups["My-Cool-Font"].Normal),
                Is.EqualTo("My-Cool-Font.ttf")
            );
        }

        [Test]
        public void GetEmbeddedFontGroups_IgnoresFilesWeDoNotStore()
        {
            MakeBookFontFile("Foo.ttf");
            MakeBookFontFile("Bar.woff2"); // Bloom copies installed files, which are never web fonts
            MakeBookFontFile("readme.txt");

            var groups = EmbeddedFonts.GetEmbeddedFontGroups(_bookFolder);

            Assert.That(groups.Keys, Is.EquivalentTo(new[] { "Foo" }));
        }

        [Test]
        public void GetEmbeddedFontGroups_FamilyWithNoNormalFile_IsDropped()
        {
            MakeBookFontFile("OnlyBold-Bold.ttf");
            MakeBookFontFile("Complete.ttf");

            var groups = EmbeddedFonts.GetEmbeddedFontGroups(_bookFolder);

            // "OnlyBold" cannot be used: GetFileForFont and FontMetadata both need a normal file.
            Assert.That(groups.Keys, Is.EquivalentTo(new[] { "Complete" }));
        }

        [Test]
        public void GetCollectionFontGroups_FindsFontsBesideTheBook()
        {
            // Sanity check: a file in the book folder is not a collection font.
            MakeBookFontFile("InBook.ttf");
            Assert.That(EmbeddedFonts.GetCollectionFontGroups(_bookFolder), Is.Empty);

            MakeCollectionFontFile("Shared.ttf");

            var groups = EmbeddedFonts.GetCollectionFontGroups(_bookFolder);

            Assert.That(groups.Keys, Is.EquivalentTo(new[] { "Shared" }));
        }

        [Test]
        public void GetAvailableStoredFontGroups_BookFolderOverridesCollection()
        {
            MakeCollectionFontFile("Shared.ttf");
            MakeCollectionFontFile("Both.ttf");
            var bookCopy = MakeBookFontFile("Both.ttf");

            var groups = EmbeddedFonts.GetAvailableStoredFontGroups(_bookFolder);

            Assert.That(groups.Keys, Is.EquivalentTo(new[] { "Shared", "Both" }));
            Assert.That(groups["Shared"].Source, Is.EqualTo(FontMetadata.kSourceCollection));
            Assert.That(groups["Both"].Source, Is.EqualTo(FontMetadata.kSourceBook));
            Assert.That(groups["Both"].Group.Normal, Is.EqualTo(bookCopy));
        }

        [Test]
        public void GetFontFaceDeclarations_BookFontUsesBareName_CollectionFontUsesParentFolder()
        {
            MakeBookFontFile("InBook-Regular.ttf");
            MakeBookFontFile("InBook-Bold.ttf");
            MakeCollectionFontFile("Shared.otf");

            var css = EmbeddedFonts.GetFontFaceDeclarations(_bookFolder);

            Assert.That(
                css,
                Does.Contain(
                    "@font-face {font-family:'InBook'; font-weight:400; font-style:normal; src:url('InBook-Regular.ttf') format('truetype');}"
                )
            );
            Assert.That(
                css,
                Does.Contain(
                    "@font-face {font-family:'InBook'; font-weight:700; font-style:normal; src:url('InBook-Bold.ttf') format('truetype');}"
                )
            );
            Assert.That(
                css,
                Does.Contain(
                    "@font-face {font-family:'Shared'; font-weight:400; font-style:normal; src:url('../fonts/Shared.otf') format('opentype');}"
                )
            );
            // No italic file, so no italic rule.
            Assert.That(css, Does.Not.Contain("font-style:italic"));
        }

        [Test]
        public void GetFontFaceDeclarations_QuoteInName_IsEscaped()
        {
            MakeBookFontFile("O'Brien.ttf");

            var css = EmbeddedFonts.GetFontFaceDeclarations(_bookFolder);

            Assert.That(
                css,
                Does.Contain(
                    @"@font-face {font-family:'O\'Brien'; font-weight:400; font-style:normal; src:url('O\'Brien.ttf') format('truetype');}"
                )
            );
        }

        [Test]
        public void GetFontFaceDeclarationsForBookRootFonts_OnlyTheNamedFamilies()
        {
            MakeBookFontFile("Wanted.ttf");
            MakeBookFontFile("Unwanted.ttf");

            var css = EmbeddedFonts.GetFontFaceDeclarationsForBookRootFonts(
                _bookFolder,
                new HashSet<string> { "Wanted" }
            );

            Assert.That(css, Does.Contain("src:url('Wanted.ttf')"));
            Assert.That(css, Does.Not.Contain("Unwanted"));
        }

        [Test]
        public void StoreFontInCollection_NamesFilesByTheConvention()
        {
            using (var installed = new TemporaryFolder("StoreFontInCollection"))
            {
                var group = new FontGroup
                {
                    Normal = MakeFontFile(installed.Path, "whatever-the-vendor-called-it.ttf"),
                    Bold = MakeFontFile(installed.Path, "vendorbd.ttf"),
                };
                // Sanity check: the collection has no fonts folder yet.
                Assert.That(Directory.Exists(_collectionFontsFolder), Is.False);

                EmbeddedFonts.StoreFontInCollection(_bookFolder, "Vendor Face", group);

                Assert.That(
                    Directory.GetFiles(_collectionFontsFolder).Select(Path.GetFileName),
                    Is.EquivalentTo(new[] { "Vendor Face.ttf", "Vendor Face-Bold.ttf" })
                );
                var groups = EmbeddedFonts.GetCollectionFontGroups(_bookFolder);
                Assert.That(groups.Keys, Is.EquivalentTo(new[] { "Vendor Face" }));
                Assert.That(groups["Vendor Face"].Bold, Is.Not.Null);
            }
        }

        [Test]
        public void MoveBookFontsToCollection_MovesUpAndLeavesNothingInTheBook()
        {
            MakeBookFontFile("Downloaded.ttf");
            MakeBookFontFile("Downloaded-Bold.ttf");
            // Sanity check: the book really does have the fonts before the move.
            Assert.That(EmbeddedFonts.GetEmbeddedFontGroups(_bookFolder).Count, Is.EqualTo(1));

            EmbeddedFonts.MoveBookFontsToCollection(_bookFolder);

            Assert.That(EmbeddedFonts.GetEmbeddedFontGroups(_bookFolder), Is.Empty);
            Assert.That(
                Directory.GetFiles(_collectionFontsFolder).Select(Path.GetFileName),
                Is.EquivalentTo(new[] { "Downloaded.ttf", "Downloaded-Bold.ttf" })
            );
        }

        [Test]
        public void MoveBookFontsToCollection_FamilyTheCollectionHas_IsJustDeleted()
        {
            var collectionCopy = MakeCollectionFontFile("Shared.ttf");
            var collectionContent = "the copy the collection already had";
            File.WriteAllText(collectionCopy, collectionContent);
            MakeBookFontFile("Shared.ttf");

            EmbeddedFonts.MoveBookFontsToCollection(_bookFolder);

            Assert.That(EmbeddedFonts.GetEmbeddedFontGroups(_bookFolder), Is.Empty);
            Assert.That(File.ReadAllText(collectionCopy), Is.EqualTo(collectionContent));
        }

        [Test]
        public void RemoveUnusedCollectionFonts_DeletesOnlyTheFamiliesNoBookUses()
        {
            MakeCollectionFontFile("Used.ttf");
            MakeCollectionFontFile("Used-Bold.ttf");
            MakeCollectionFontFile("Unused.ttf");

            EmbeddedFonts.RemoveUnusedCollectionFonts(
                _collection.Path,
                new HashSet<string> { "Used" }
            );

            Assert.That(
                Directory.GetFiles(_collectionFontsFolder).Select(Path.GetFileName),
                Is.EquivalentTo(new[] { "Used.ttf", "Used-Bold.ttf" })
            );
        }

        [Test]
        public void CopyStoredFontsIntoBook_CopiesUsedFamiliesAndReportsTheCollectionOnes()
        {
            MakeCollectionFontFile("Shared.ttf");
            MakeCollectionFontFile("SharedButUnused.ttf");
            MakeBookFontFile("InBook.ttf");
            using (var outgoing = new TemporaryFolder("CopyStoredFontsIntoBook"))
            {
                var copiedFromCollection = EmbeddedFonts.CopyStoredFontsIntoBook(
                    _bookFolder,
                    outgoing.Path,
                    new HashSet<string> { "Shared", "InBook" }
                );

                Assert.That(copiedFromCollection, Is.EquivalentTo(new[] { "Shared" }));
                Assert.That(
                    Directory.GetFiles(outgoing.Path).Select(Path.GetFileName),
                    Is.EquivalentTo(new[] { "Shared.ttf", "InBook.ttf" })
                );
            }
        }

        [Test]
        public void AddStoredFontsToOutgoingBook_ReplacesTheCollectionRulesWithBareNameRules()
        {
            MakeCollectionFontFile("Shared.ttf");
            using (var outgoing = new TemporaryFolder("AddStoredFontsToOutgoingBook"))
            {
                var cssPath = Path.Combine(outgoing.Path, "defaultLangStyles.css");
                var oldRule =
                    "@font-face {font-family:'Shared'; font-weight:400; font-style:normal; src:url('../fonts/Shared.ttf') format('truetype');}";
                File.WriteAllLines(
                    cssPath,
                    new[] { oldRule, "[lang='en'] { font-family: 'Shared'; }" }
                );

                EmbeddedFonts.AddStoredFontsToOutgoingBook(
                    _bookFolder,
                    outgoing.Path,
                    new HashSet<string> { "Shared" }
                );

                var css = File.ReadAllText(cssPath);
                Assert.That(css, Does.Contain("src:url('Shared.ttf')"));
                Assert.That(
                    css,
                    Does.Not.Contain("src:url('../fonts/Shared.ttf')"),
                    "the rule that pointed at the collection fonts folder must be gone"
                );
                Assert.That(
                    css,
                    Does.Contain("[lang='en'] { font-family: 'Shared'; }"),
                    "only the stale @font-face line is dropped"
                );
            }
        }

        [Test]
        public void RemoveUnusedBookFonts_DeletesFilesAndTheirRules()
        {
            MakeBookFontFile("Used.ttf");
            MakeBookFontFile("Unused.ttf");
            var cssPath = Path.Combine(_bookFolder, "defaultLangStyles.css");
            File.WriteAllLines(
                cssPath,
                new[]
                {
                    "@font-face {font-family:'Used'; font-weight:400; font-style:normal; src:url('Used.ttf') format('truetype');}",
                    "@font-face {font-family:'Unused'; font-weight:400; font-style:normal; src:url('Unused.ttf') format('truetype');}",
                    "[lang='en'] { font-family: 'Used'; }",
                }
            );

            EmbeddedFonts.RemoveUnusedBookFonts(_bookFolder, new HashSet<string> { "Used" });

            Assert.That(
                Directory.GetFiles(_bookFolder, "*.ttf").Select(Path.GetFileName),
                Is.EquivalentTo(new[] { "Used.ttf" })
            );
            var css = File.ReadAllText(cssPath);
            Assert.That(css, Does.Contain("src:url('Used.ttf')"));
            Assert.That(css, Does.Not.Contain("Unused.ttf"));
            Assert.That(css, Does.Contain("[lang='en']"));
        }

        [Test]
        public void ChooseFontsToStore_SkipsServedAndUnsuitableAndAlreadyStoredAndMissing()
        {
            var installedGroup = new FontGroup { Normal = MakeBookFontFile("Installed.ttf") };
            var okMetadata = MakeMetadataWithSuitability("Wanted", FontMetadata.kOK);
            var badMetadata = MakeMetadataWithSuitability("BadLicense", FontMetadata.kUnsuitable);

            var chosen = EmbeddedFonts
                .ChooseFontsToStore(
                    new[]
                    {
                        "Wanted",
                        "AlreadyStored",
                        "Served",
                        "BadLicense",
                        "NotInstalled",
                        "Unknown",
                    },
                    family => family == "AlreadyStored",
                    family => family == "Served",
                    family =>
                        family == "BadLicense" ? badMetadata
                        : family == "Unknown" ? null
                        : okMetadata,
                    family => family == "NotInstalled" ? null : installedGroup
                )
                .ToList();

            Assert.That(chosen.Select(kvp => kvp.Key), Is.EquivalentTo(new[] { "Wanted" }));
            Assert.That(chosen[0].Value, Is.SameAs(installedGroup));
        }

        private FontMetadata MakeMetadataWithSuitability(string family, string suitability)
        {
            var path = MakeFontFile(_bookFolder, family + "-metadata.ttf");
            var metadata = new FontMetadata(family, new FontGroup { Normal = path });
            metadata.SetSuitabilityForTest(suitability);
            return metadata;
        }

        [Test]
        public void MakeEmbeddedFontMetadata_RealFont_IsSuitableAndCarriesTheSource()
        {
            var installedGroup = InstalledTestFonts.FindFamilyWithGoodLicense(
                out var installedName
            );
            if (installedGroup == null)
                Assert.Ignore("No font with a known-good license is installed on this computer.");
            Directory.CreateDirectory(_collectionFontsFolder);
            File.Copy(installedGroup.Normal, Path.Combine(_collectionFontsFolder, "Foo.ttf"));

            var group = EmbeddedFonts.GetCollectionFontGroups(_bookFolder)["Foo"];
            var metadata = EmbeddedFonts.MakeEmbeddedFontMetadata(
                "Foo",
                group,
                FontMetadata.kSourceCollection
            );

            // The family name comes from the file name, not from inside the font file.
            Assert.That(metadata.name, Is.EqualTo("Foo"));
            Assert.That(metadata.source, Is.EqualTo(FontMetadata.kSourceCollection));
            Assert.That(metadata.determinedSuitability, Is.EqualTo(FontMetadata.kOK));
            Assert.That(metadata.fileExtension, Is.EqualTo(".ttf"));
        }

        [Test]
        public void MakeEmbeddedFontMetadata_SecondFontAddedToTheSameFolder_IsAlsoRead()
        {
            var installedGroup = InstalledTestFonts.FindFamilyWithGoodLicense(out _);
            if (installedGroup == null)
                Assert.Ignore("No font with a known-good license is installed on this computer.");
            StoredFontMetadataCache.ClearForTests();
            // A folder that no other test has used. The font reading code in WPF remembers which
            // files a folder held when it first read one of them, so a folder read earlier in this
            // process would hide the defect this test covers.
            using (
                var fresh = new TemporaryFolder(
                    "EmbeddedFontsTests-" + Guid.NewGuid().ToString("N")
                )
            )
            {
                var bookFolder = Path.Combine(fresh.Path, "My Book");
                Directory.CreateDirectory(bookFolder);
                var fontsFolder = Path.Combine(fresh.Path, "fonts");
                Directory.CreateDirectory(fontsFolder);
                File.Copy(installedGroup.Normal, Path.Combine(fontsFolder, "First.ttf"));

                var first = EmbeddedFonts.MakeEmbeddedFontMetadata(
                    "First",
                    EmbeddedFonts.GetCollectionFontGroups(bookFolder)["First"],
                    FontMetadata.kSourceCollection
                );
                // Sanity check: the first font read from a folder has always worked.
                Assert.That(
                    first.determinedSuitability,
                    Is.EqualTo(FontMetadata.kOK),
                    first.determinedSuitabilityNotes
                );

                // The collection fonts folder gains a file whenever a book starts using a font.
                File.Copy(installedGroup.Normal, Path.Combine(fontsFolder, "Second.ttf"));
                var second = EmbeddedFonts.MakeEmbeddedFontMetadata(
                    "Second",
                    EmbeddedFonts.GetCollectionFontGroups(bookFolder)["Second"],
                    FontMetadata.kSourceCollection
                );

                Assert.That(
                    second.determinedSuitability,
                    Is.EqualTo(FontMetadata.kOK),
                    second.determinedSuitabilityNotes
                );
            }
        }

        [Test]
        public void MakeEmbeddedFontMetadata_UnchangedFileIsReadOnce_ChangedFileIsReadAgain()
        {
            var installedGroup = InstalledTestFonts.FindFamilyWithGoodLicense(out _);
            if (installedGroup == null)
                Assert.Ignore("No font with a known-good license is installed on this computer.");
            StoredFontMetadataCache.ClearForTests();
            Directory.CreateDirectory(_collectionFontsFolder);
            var path = Path.Combine(_collectionFontsFolder, "Foo.ttf");
            File.Copy(installedGroup.Normal, path);
            var group = EmbeddedFonts.GetCollectionFontGroups(_bookFolder)["Foo"];

            var first = EmbeddedFonts.MakeEmbeddedFontMetadata(
                "Foo",
                group,
                FontMetadata.kSourceCollection
            );
            var again = EmbeddedFonts.MakeEmbeddedFontMetadata(
                "Foo",
                group,
                FontMetadata.kSourceCollection
            );
            Assert.That(again, Is.SameAs(first), "a file that has not changed is read once");
            Assert.That(StoredFontMetadataCache.CountForTests, Is.EqualTo(1));

            // A file of a different length is a different file.
            File.WriteAllBytes(path, File.ReadAllBytes(path).Concat(new byte[] { 0 }).ToArray());
            var afterChange = EmbeddedFonts.MakeEmbeddedFontMetadata(
                "Foo",
                group,
                FontMetadata.kSourceCollection
            );

            Assert.That(afterChange, Is.Not.SameAs(first), "a file that changed is read again");
            Assert.That(StoredFontMetadataCache.CountForTests, Is.EqualTo(2));
        }

        [Test]
        public void MakeEmbeddedFontMetadata_UnreadableFile_IsInvalid()
        {
            MakeBookFontFile("Foo.ttf"); // contains text, not a font

            var group = EmbeddedFonts.GetEmbeddedFontGroups(_bookFolder)["Foo"];
            var metadata = EmbeddedFonts.MakeEmbeddedFontMetadata(
                "Foo",
                group,
                FontMetadata.kSourceBook
            );

            Assert.That(metadata.source, Is.EqualTo(FontMetadata.kSourceBook));
            Assert.That(metadata.determinedSuitability, Is.EqualTo(FontMetadata.kInvalid));
        }
    }
}
