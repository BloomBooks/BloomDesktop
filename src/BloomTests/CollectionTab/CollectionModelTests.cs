using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Book;
using Bloom.CollectionTab;
using Bloom.Utils;
using BloomTests.TestDoubles.CollectionTab;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;
using SIL.IO;
using SIL.Reporting;
using SIL.TestUtilities;

namespace BloomTests.CollectionTab
{
    [TestFixture]
    [SuppressMessage("ReSharper", "LocalizableElement")]
    class CollectionModelTests
    {
        const string kCollectionName = "FakeCollection";
        private TemporaryFolder _collection;
        private TemporaryFolder _folder;
        private FakeCollectionModel _testCollectionModel;

        [SetUp]
        public void Setup()
        {
            ErrorReport.IsOkToInteractWithUser = false;
            _folder = new TemporaryFolder("CollectionModelTests");

            // ENHANCE: Sometimes making the FakeCollection temporary folder causes an UnauthorizedAccessException.
            // Not exactly sure why or what to do about it. Possibly it could be related to file system operations
            // being async in nature???
            _collection = new TemporaryFolder(_folder, kCollectionName);
            MakeFakeCssFile();
            _testCollectionModel = new FakeCollectionModel(_collection);
        }

        private void MakeFakeCssFile()
        {
            var cssName = Path.Combine(_collection.Path, "FakeCss.css");
            File.WriteAllText(cssName, "Fake CSS file");
        }

        private string MakeBook()
        {
            var f = new TemporaryFolder(_collection, "unittest-" + Guid.NewGuid());
            File.WriteAllText(Path.Combine(f.Path, "one.htm"), "test");
            File.WriteAllText(Path.Combine(f.Path, "one.css"), "test");
            File.WriteAllText(Path.Combine(f.Path, "meta.json"), new BookMetaData().Json);
            return f.Path;
        }

        private void AddThumbsFile(string bookFolderPath)
        {
            File.WriteAllText(Path.Combine(bookFolderPath, "thumbs.db"), "test thumbs.db file");
        }

        private void AddPdfFile(string bookFolderPath)
        {
            File.WriteAllText(Path.Combine(bookFolderPath, "xfile1.pdf"), "test pdf file");
        }

        private void AddUnnecessaryHtmlFile(string srcBookPath)
        {
            string extraHtmDir = Path.Combine(srcBookPath, "unnecessaryExtraFiles");
            Directory.CreateDirectory(extraHtmDir);
            string htmContents = "<html><body><w:sdtPr></w:sdtPr></body></html>";
            File.WriteAllText(Path.Combine(extraHtmDir, "causesException.htm"), htmContents);
        }

        // Imitate CollectionModel.MakeBloomPack(), but bypasses the user interaction
        private void MakeTestBloomPack(string outputPath, bool forReaderTools)
        {
            var (dirName, dirPrefix) =
                _testCollectionModel.GetDirNameAndPrefixForCollectionBloomPack();
            _testCollectionModel.MakeBloomPackInternal(
                outputPath,
                dirName,
                dirPrefix,
                forReaderTools,
                isCollection: true
            );
        }

        // Imitate CollectionModel.MakeBloomPack(), but bypasses the user interaction
        private void MakeTestSingleBookBloomPack(
            string outputPath,
            string bookSrcPath,
            bool forReaderTools
        )
        {
            var (dirName, dirPrefix) =
                _testCollectionModel.GetDirNameAndPrefixForSingleBookBloomPack(bookSrcPath);
            _testCollectionModel.MakeBloomPackInternal(
                outputPath,
                dirName,
                dirPrefix,
                forReaderTools,
                isCollection: false
            );
        }

        // Don't do anything with the zip file except read in the filenames
        private static List<string> GetActualFilenamesFromZipfile(string bloomPackName)
        {
            var actualFiles = new List<string>();
            using (var zip = new ZipFile(bloomPackName))
            {
                actualFiles.AddRange(from ZipEntry entry in zip select entry.Name);
                zip.Close();
            }
            return actualFiles;
        }

        // ---- Import .bloomSource tests ----

        private static string MetaJson(string instanceId, string title)
        {
            return $"{{\"bookInstanceId\":\"{instanceId}\",\"title\":\"{title}\"}}";
        }

        // Build a .bloomSource file the same way Bloom's export does: a zip of a book folder's
        // contents (flat at the root) plus a .bloomCollection settings file at the root.
        private string MakeBloomSourceFile(string title, string instanceId, bool includeHtm = true)
        {
            var src = Path.Combine(_folder.Path, "srcbook-" + Guid.NewGuid());
            Directory.CreateDirectory(src);
            if (includeHtm)
                File.WriteAllText(
                    Path.Combine(src, title + ".htm"),
                    "<html><head></head><body></body></html>"
                );
            File.WriteAllText(Path.Combine(src, "book.css"), "/*css*/");
            File.WriteAllText(Path.Combine(src, "meta.json"), MetaJson(instanceId, title));

            var collectionFile = Path.Combine(_folder.Path, "SourceCollection.bloomCollection");
            if (!File.Exists(collectionFile))
                File.WriteAllText(collectionFile, "<Collection version=\"0.8\" />");

            var dest = Path.Combine(_folder.Path, title + "-" + Guid.NewGuid() + ".bloomSource");
            var ok = CollectionModel.SaveAsBloomSourceFile(
                src,
                dest,
                out var ex,
                new[] { collectionFile }
            );
            Assert.That(ok, Is.True, "Test setup failed to create .bloomSource: " + ex?.Message);
            return dest;
        }

        // Put a book directly in the editable collection folder so import can find it as a duplicate.
        private string MakeExistingBookInCollection(string title, string instanceId)
        {
            var folder = Path.Combine(_collection.Path, title);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, title + ".htm"), "<html></html>");
            File.WriteAllText(Path.Combine(folder, "meta.json"), MetaJson(instanceId, title));
            return folder;
        }

        private static CollectionModel.ImportDuplicateChoice NeverCalled(string title)
        {
            throw new Exception("resolveDuplicate should not have been called");
        }

        [Test]
        public void ImportBloomSource_ValidFile_PlacesBookInCollectionWithoutBloomCollection()
        {
            var src = MakeBloomSourceFile("Moon", Guid.NewGuid().ToString());

            var dest = _testCollectionModel.ImportBloomSourceFileToCollectionFolder(
                src,
                NeverCalled
            );

            Assert.That(dest, Is.Not.Null);
            Assert.That(Directory.Exists(dest), Is.True, "book folder should exist");
            Assert.That(
                Directory.GetParent(dest).FullName,
                Is.EqualTo(_collection.Path),
                "book should be placed directly in the editable collection"
            );
            Assert.That(File.Exists(Path.Combine(dest, "meta.json")), Is.True);
            // The main htm file is renamed to match the (unique) folder name.
            Assert.That(
                File.Exists(Path.Combine(dest, Path.GetFileName(dest) + ".htm")),
                Is.True,
                "book htm should be renamed to match the folder"
            );
            // The stray collection settings file must not end up in the book folder.
            Assert.That(
                Directory.GetFiles(dest, "*.bloomCollection"),
                Is.Empty,
                "the .bloomCollection file should have been removed"
            );
        }

        [Test]
        public void ImportBloomSource_DuplicateId_AddCopy_MakesIndependentCopyWithNewId()
        {
            var sharedId = Guid.NewGuid().ToString();
            MakeExistingBookInCollection("Moon", sharedId);
            var src = MakeBloomSourceFile("Moon", sharedId);

            var dest = _testCollectionModel.ImportBloomSourceFileToCollectionFolder(
                src,
                title => CollectionModel.ImportDuplicateChoice.AddCopy
            );

            Assert.That(dest, Is.Not.Null);
            var importedId = BookMetaData.FromFolder(dest).Id;
            Assert.That(
                importedId,
                Is.Not.EqualTo(sharedId),
                "an added copy should get a new instance id so it is independent"
            );
            // The added copy follows the same "<name> - Copy-<id>" folder convention as the
            // Duplicate Book command.
            Assert.That(
                Path.GetFileName(dest),
                Does.Contain(" - Copy-"),
                "an added copy should use the Duplicate-style folder name"
            );
            // The original book is still there, unchanged.
            Assert.That(Directory.Exists(Path.Combine(_collection.Path, "Moon")), Is.True);
        }

        [Test]
        public void ImportBloomSource_DuplicateId_Replace_RecyclesExistingBook()
        {
            var sharedId = Guid.NewGuid().ToString();
            var existingFolder = MakeExistingBookInCollection("Moon", sharedId);
            var src = MakeBloomSourceFile("Moon", sharedId);

            var dest = _testCollectionModel.ImportBloomSourceFileToCollectionFolder(
                src,
                title => CollectionModel.ImportDuplicateChoice.Replace
            );

            Assert.That(dest, Is.Not.Null);
            Assert.That(
                Directory.Exists(existingFolder),
                Is.False,
                "the existing book should have been recycled"
            );
            Assert.That(BookMetaData.FromFolder(dest).Id, Is.EqualTo(sharedId));
        }

        [Test]
        public void ImportBloomSource_DuplicateId_Cancel_ImportsNothing()
        {
            var sharedId = Guid.NewGuid().ToString();
            MakeExistingBookInCollection("Moon", sharedId);
            var src = MakeBloomSourceFile("Moon", sharedId);
            var foldersBefore = Directory.GetDirectories(_collection.Path).Length;

            var dest = _testCollectionModel.ImportBloomSourceFileToCollectionFolder(
                src,
                title => CollectionModel.ImportDuplicateChoice.Cancel
            );

            Assert.That(dest, Is.Null, "cancel should return no imported book");
            Assert.That(
                Directory.GetDirectories(_collection.Path).Length,
                Is.EqualTo(foldersBefore),
                "cancel should not add or remove any book"
            );
        }

        [Test]
        public void ImportBloomSource_SameTitleDifferentId_NotTreatedAsDuplicate()
        {
            // Duplicate detection is by bookInstanceId, NOT by title/folder name. A book with the
            // same name but a different id is a different book, so it must import silently (the
            // resolveDuplicate callback must never be invoked) rather than prompting the user.
            MakeExistingBookInCollection("Moon", Guid.NewGuid().ToString());
            var importedId = Guid.NewGuid().ToString();
            var src = MakeBloomSourceFile("Moon", importedId);

            var dest = _testCollectionModel.ImportBloomSourceFileToCollectionFolder(
                src,
                NeverCalled // if this is called, the code wrongly treated a same-name book as a duplicate
            );

            Assert.That(dest, Is.Not.Null);
            Assert.That(
                BookMetaData.FromFolder(dest).Id,
                Is.EqualTo(importedId),
                "a non-duplicate import should keep its own id"
            );
            // The same-named existing book is still there, and the import landed in its own folder.
            Assert.That(Directory.Exists(Path.Combine(_collection.Path, "Moon")), Is.True);
            Assert.That(
                dest,
                Is.Not.EqualTo(Path.Combine(_collection.Path, "Moon")),
                "the import should land in a distinct folder from the same-named existing book"
            );
        }

        [Test]
        public void ImportBloomSource_SameIdDifferentTitle_IsTreatedAsDuplicate()
        {
            // The flip side: a matching bookInstanceId means it is the same book even when the
            // titles differ, so we must resolve the duplicate rather than silently create a second
            // book that shares an id.
            var sharedId = Guid.NewGuid().ToString();
            var existingFolder = MakeExistingBookInCollection("Sun", sharedId);
            var src = MakeBloomSourceFile("Moon", sharedId);
            var resolveCalled = false;

            var dest = _testCollectionModel.ImportBloomSourceFileToCollectionFolder(
                src,
                title =>
                {
                    resolveCalled = true;
                    return CollectionModel.ImportDuplicateChoice.Replace;
                }
            );

            Assert.That(
                resolveCalled,
                Is.True,
                "a book with a matching id but a different title should still be detected as a duplicate"
            );
            Assert.That(dest, Is.Not.Null);
            Assert.That(
                Directory.Exists(existingFolder),
                Is.False,
                "Replace should have recycled the existing (same-id) book"
            );
        }

        [Test]
        public void ImportBloomSource_DuplicateId_AddCopy_LeavesNoTwoBooksSharingAnId()
        {
            // The whole point of giving the added copy a new id: we must never end up with two
            // books that share a bookInstanceId. That id also controls re-uploading to the Bloom
            // library, so a collision would cause real bugs.
            var sharedId = Guid.NewGuid().ToString();
            var originalFolder = MakeExistingBookInCollection("Moon", sharedId);
            var src = MakeBloomSourceFile("Moon", sharedId);
            // Sanity check: before importing, the original really has the shared id.
            Assert.That(BookMetaData.FromFolder(originalFolder).Id, Is.EqualTo(sharedId));

            var dest = _testCollectionModel.ImportBloomSourceFileToCollectionFolder(
                src,
                title => CollectionModel.ImportDuplicateChoice.AddCopy
            );

            var originalId = BookMetaData.FromFolder(originalFolder).Id;
            var importedId = BookMetaData.FromFolder(dest).Id;
            Assert.That(
                originalId,
                Is.EqualTo(sharedId),
                "the original book should be left untouched with its id"
            );
            Assert.That(
                importedId,
                Is.Not.EqualTo(originalId),
                "the added copy must have a different id from the book it duplicates"
            );
        }

        [Test]
        public void AnyBloomSourceIsAlreadyInCollection_MatchingId_ReturnsTrue()
        {
            // This is the pre-flight check that decides whether to show the duplicate dialog.
            var sharedId = Guid.NewGuid().ToString();
            MakeExistingBookInCollection("Moon", sharedId);
            var src = MakeBloomSourceFile("Moon", sharedId);

            Assert.That(
                _testCollectionModel.AnyBloomSourceIsAlreadyInCollection(new[] { src }),
                Is.True
            );
        }

        [Test]
        public void AnyBloomSourceIsAlreadyInCollection_SameTitleDifferentId_ReturnsFalse()
        {
            // The dialog is triggered by a matching bookInstanceId, not a matching name.
            MakeExistingBookInCollection("Moon", Guid.NewGuid().ToString());
            var src = MakeBloomSourceFile("Moon", Guid.NewGuid().ToString());

            Assert.That(
                _testCollectionModel.AnyBloomSourceIsAlreadyInCollection(new[] { src }),
                Is.False,
                "a same-named book with a different id is not a duplicate"
            );
        }

        [Test]
        public void AnyBloomSourceIsAlreadyInCollection_DifferentIdAndTitle_ReturnsFalse()
        {
            MakeExistingBookInCollection("Sun", Guid.NewGuid().ToString());
            var src = MakeBloomSourceFile("Moon", Guid.NewGuid().ToString());

            Assert.That(
                _testCollectionModel.AnyBloomSourceIsAlreadyInCollection(new[] { src }),
                Is.False
            );
        }

        [Test]
        public void AnyBloomSourceIsAlreadyInCollection_NoFilesChosen_ReturnsFalse()
        {
            Assert.That(_testCollectionModel.AnyBloomSourceIsAlreadyInCollection(null), Is.False);
            Assert.That(
                _testCollectionModel.AnyBloomSourceIsAlreadyInCollection(new string[0]),
                Is.False
            );
        }

        [Test]
        public void ImportBloomSource_CorruptFile_Throws()
        {
            var bad = Path.Combine(_folder.Path, "corrupt.bloomSource");
            File.WriteAllText(bad, "this is not a zip file");

            Assert.Throws<CollectionModel.BloomSourceImportException>(() =>
                _testCollectionModel.ImportBloomSourceFileToCollectionFolder(bad, NeverCalled)
            );
        }

        [Test]
        public void ImportBloomSource_NoBookInside_Throws()
        {
            var src = MakeBloomSourceFile("Moon", Guid.NewGuid().ToString(), includeHtm: false);

            Assert.Throws<CollectionModel.BloomSourceImportException>(() =>
                _testCollectionModel.ImportBloomSourceFileToCollectionFolder(src, NeverCalled)
            );
        }

        [Test]
        public void ImportBloomSource_BloomPackShapedZip_Throws()
        {
            // A Bloom Pack wraps everything in a single collection folder (no files at the root).
            var collDir = Path.Combine(_folder.Path, "MyCollection");
            var bookDir = Path.Combine(collDir, "SomeBook");
            Directory.CreateDirectory(bookDir);
            File.WriteAllText(Path.Combine(bookDir, "SomeBook.htm"), "<html></html>");
            File.WriteAllText(Path.Combine(bookDir, "meta.json"), MetaJson("id-1", "SomeBook"));
            var packPath = Path.Combine(_folder.Path, "pack.bloomSource");
            var zip = new BloomZipFile(packPath);
            zip.AddDirectory(collDir); // includes "MyCollection" as the single root folder
            zip.Save();

            Assert.Throws<CollectionModel.BloomSourceImportException>(() =>
                _testCollectionModel.ImportBloomSourceFileToCollectionFolder(packPath, NeverCalled)
            );
        }

        [Test]
        public void ImportBloomSource_ZipSlipEntry_Throws_AndWritesNothingOutside()
        {
            var slipPath = Path.Combine(_folder.Path, "slip.bloomSource");
            using (var fs = File.Create(slipPath))
            using (var zos = new ZipOutputStream(fs))
            {
                var bytes = Encoding.UTF8.GetBytes("x");
                zos.PutNextEntry(new ZipEntry("../evil.txt"));
                zos.Write(bytes, 0, bytes.Length);
                zos.CloseEntry();
                zos.PutNextEntry(new ZipEntry("Book.htm"));
                zos.Write(bytes, 0, bytes.Length);
                zos.CloseEntry();
                zos.IsStreamOwner = true;
            }

            Assert.Throws<CollectionModel.BloomSourceImportException>(() =>
                _testCollectionModel.ImportBloomSourceFileToCollectionFolder(slipPath, NeverCalled)
            );
            // The traversal target must never be written.
            Assert.That(
                File.Exists(Path.Combine(_folder.Path, "evil.txt")),
                Is.False,
                "zip-slip entry must not be extracted outside the target folder"
            );
        }

        [Test]
        public void MakeBloomPack_DoesntIncludePdfFile()
        {
            var srcBookPath = MakeBook();
            AddPdfFile(srcBookPath);
            const string excludedFile = "xfile1.pdf";
            var bloomPackName = Path.Combine(_folder.Path, "testPack.BloomPack");

            // Imitate CollectionModel.MakeBloomPack() without the user interaction
            MakeTestBloomPack(bloomPackName, false);

            // Don't do anything with the zip file except read in the filenames
            var actualFiles = GetActualFilenamesFromZipfile(bloomPackName);

            // +2 for collection-level CustomCollectionSettings and FakeCss.css file, -1 for pdf file, so the count is +1
            Assert.That(actualFiles.Count, Is.EqualTo(Directory.GetFiles(srcBookPath).Count() + 1));

            foreach (var filePath in actualFiles)
            {
                Assert.IsFalse(Equals(Path.GetFileName(filePath), excludedFile));
            }
        }

        [Test]
        public void MakeBloomPack_DoesntIncludeThumbsFile()
        {
            var srcBookPath = MakeBook();
            AddThumbsFile(srcBookPath);
            const string excludedFile = "thumbs.db";
            var bloomPackName = Path.Combine(_folder.Path, "testPack.BloomPack");

            // Imitate CollectionModel.MakeBloomPack() without the user interaction
            MakeTestBloomPack(bloomPackName, false);

            // Don't do anything with the zip file except read in the filenames
            var actualFiles = GetActualFilenamesFromZipfile(bloomPackName);

            // +2 for collection-level CustomCollectionSettings and FakeCss.css file, -1 for thumbs file, so the count is +1
            Assert.That(actualFiles.Count, Is.EqualTo(Directory.GetFiles(srcBookPath).Count() + 1));

            foreach (var filePath in actualFiles)
            {
                Assert.IsFalse(Equals(Path.GetFileName(filePath), excludedFile));
            }
        }

        [Test]
        public void MakeBloomPack_DoesntIncludeCorrupt_Map_OrBakFiles()
        {
            var srcBookPath = MakeBook();
            const string excludedFile1 = BookStorage.PrefixForCorruptHtmFiles + ".htm";
            const string excludedFile2 = BookStorage.PrefixForCorruptHtmFiles + "2.htm";
            const string excludedFile3 = "Basic Book.css.map";
            string excludedBackup = Path.GetFileName(srcBookPath) + ".bak";
            RobustFile.WriteAllText(Path.Combine(srcBookPath, excludedFile1), "rubbish");
            RobustFile.WriteAllText(Path.Combine(srcBookPath, excludedFile2), "rubbish");
            RobustFile.WriteAllText(Path.Combine(srcBookPath, excludedFile3), "rubbish");
            RobustFile.WriteAllText(Path.Combine(srcBookPath, excludedBackup), "rubbish");
            var bloomPackName = Path.Combine(_folder.Path, "testPack.BloomPack");

            // Imitate CollectionModel.MakeBloomPack() without the user interaction
            MakeTestBloomPack(bloomPackName, false);

            // Don't do anything with the zip file except read in the filenames
            var actualFiles = GetActualFilenamesFromZipfile(bloomPackName);

            // +2 for collection-level CustomCollectionSettings and FakeCss.css file, -4 for corrupt, .map and .bak files, so the count is -2
            Assert.That(actualFiles.Count, Is.EqualTo(Directory.GetFiles(srcBookPath).Length - 2));

            foreach (var filePath in actualFiles)
            {
                Assert.IsFalse(Equals(Path.GetFileName(filePath), excludedFile1));
                Assert.IsFalse(Equals(Path.GetFileName(filePath), excludedFile2));
                Assert.IsFalse(Equals(Path.GetFileName(filePath), excludedFile3));
                Assert.IsFalse(Equals(Path.GetFileName(filePath), excludedBackup));
            }
        }

        [Test]
        public void MakeReaderTemplateBloomPack_IncludesExpectedFilesAndNotUnexpectedFolder()
        {
            string[] requiredCollectionLevelFiles =
            {
                "customCollectionStyles.css",
                "ReaderToolsSettings-en.json",
                "ReaderToolsWords-en.json",
            };
            foreach (var requiredFile in requiredCollectionLevelFiles)
                File.WriteAllText(
                    Path.Combine(
                        _testCollectionModel.TheOneEditableCollection.PathToDirectory,
                        requiredFile
                    ),
                    "rubbish"
                );

            Directory.CreateDirectory(
                Path.Combine(
                    _testCollectionModel.TheOneEditableCollection.PathToDirectory,
                    "Sample Texts"
                )
            );
            File.WriteAllText(
                Path.Combine(
                    _testCollectionModel.TheOneEditableCollection.PathToDirectory,
                    "Sample Texts",
                    "something.txt"
                ),
                "rubbish"
            );

            var srcBookPath = MakeBook(); // BloomPack must have at least one book

            var bloomPackName = Path.Combine(_folder.Path, "testReaderPack.BloomPack");

            // make the BloomPack
            MakeTestBloomPack(bloomPackName, forReaderTools: true);

            using (var zip = new ZipFile(bloomPackName))
            {
                foreach (var requiredFile in requiredCollectionLevelFiles)
                {
                    Assert.That(
                        zip.FindEntry($"{kCollectionName}/{requiredFile}", false) != -1,
                        () =>
                        {
                            return $"{requiredFile} is missing from the BloomPack";
                        }
                    );
                }

                Assert.That(
                    zip.FindEntry($"{kCollectionName}/Sample Texts", false) == -1,
                    () =>
                    {
                        return "Sample Texts folder should not be in the BloomPack";
                    }
                );
                Assert.That(
                    zip.FindEntry($"{kCollectionName}/Sample Texts/something.txt", false) == -1,
                    () =>
                    {
                        return "Sample Texts files should not be in the BloomPack";
                    }
                );
            }
        }

        [Test]
        public void MakeReaderTemplateBloomPack_AddsLockFormattingMetaTagToBookDom()
        {
            var srcBookPath = MakeBook();

            // the html file needs to have the same name as its directory
            var testFileName = Path.GetFileName(srcBookPath) + ".htm";
            var readerName = Path.Combine(srcBookPath, testFileName);

            var bloomPackName = Path.Combine(_folder.Path, "testReaderPack.BloomPack");

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html>");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset=\"UTF-8\"></meta>");
            sb.AppendLine(
                "    <meta name=\"Generator\" content=\"Bloom Version 3.3.0 (apparent build date: 28-Jul-2015)\"></meta>"
            );
            sb.AppendLine("    <meta name=\"BloomFormatVersion\" content=\"2.0\"></meta>");
            sb.AppendLine(
                "    <meta name=\"pageTemplateSource\" content=\"Leveled Reader\"></meta>"
            );
            sb.AppendLine("    <title>Leveled Reader</title>");
            sb.AppendLine(
                "    <link rel=\"stylesheet\" href=\"basePage.css\" type=\"text/css\"></link>"
            );
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            File.WriteAllText(readerName, sb.ToString());

            // make the BloomPack
            MakeTestBloomPack(bloomPackName, forReaderTools: true);

            // get the reader file from the BloomPack
            var actualFiles = GetActualFilenamesFromZipfile(bloomPackName);
            var zipEntryName = actualFiles.FirstOrDefault(file => file.EndsWith(testFileName));
            Assert.That(zipEntryName, Is.Not.Null.And.Not.Empty);

            string outputText;
            using (var zip = new ZipFile(bloomPackName))
            {
                var ze = zip.GetEntry(zipEntryName);
                var buffer = new byte[4096];

                using (var instream = zip.GetInputStream(ze))
                using (var writer = new MemoryStream())
                {
                    ICSharpCode.SharpZipLib.Core.StreamUtils.Copy(instream, writer, buffer);
                    writer.Position = 0;
                    using (var reader = new StreamReader(writer))
                    {
                        outputText = reader.ReadToEnd();
                    }
                }
            }

            // check for the lockFormatting meta tag
            Assert.That(outputText, Is.Not.Null.And.Not.Empty);
            Assert.IsTrue(outputText.Contains("<meta name=\"lockFormatting\" content=\"true\">"));
        }

        [Test]
        public void MakeCollectionBloomPack_DoesntParseExtraHtmlFiles()
        {
            var srcBookPath = MakeBook();
            AddUnnecessaryHtmlFile(srcBookPath);
            var bloomPackName = Path.Combine(_folder.Path, "testPack.BloomPack");

            // System Under Test
            // Imitate CollectionModel.MakeBloomPack() without the user interaction
            MakeTestBloomPack(bloomPackName, false);

            // Verification
            // Just make sure it doesn't throw an exception.
            return;
        }

        [Test]
        public void MakeSingleBookBloomPack_DoesntParseExtraHtmlFiles()
        {
            var srcBookPath = MakeBook();
            AddUnnecessaryHtmlFile(srcBookPath);
            var bloomPackName = Path.Combine(_folder.Path, "testPack.BloomPack");

            // System Under Test
            // Imitate CollectionModel.MakeBloomPack() without the user interaction
            MakeTestSingleBookBloomPack(bloomPackName, srcBookPath, false);

            // Verification
            // Just make sure it doesn't throw an exception.
            return;
        }
    }
}
