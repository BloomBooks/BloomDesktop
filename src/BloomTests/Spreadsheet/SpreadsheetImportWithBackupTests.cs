using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bloom.Spreadsheet;
using BloomTemp;
using BloomTests.TeamCollection;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Spreadsheet
{
    /// <summary>
    /// Tests SpreadsheetImporter.ImportWithBackupAsync, which the in-app import runs through: it
    /// clears the book's recordings before importing, and must put the whole book folder back as it
    /// was if the import fails before it has started saving the book.
    /// </summary>
    [TestFixture]
    public class SpreadsheetImportWithBackupTests
    {
        private const string kOriginalHtml = "<html><body>original</body></html>";

        private TemporaryFolder _testFolder;
        private string _bookFolder;
        private string _bookPath;

        /// <summary>
        /// Make a book folder to import into.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _testFolder = new TemporaryFolder("SpreadsheetImportWithBackupTests");
            _bookFolder = Path.Combine(_testFolder.FolderPath, "Book");
            Directory.CreateDirectory(_bookFolder);
            _bookPath = Path.Combine(_bookFolder, "Book.htm");
            RobustFile.WriteAllText(_bookPath, kOriginalHtml);
        }

        /// <summary>
        /// Remove the test's files.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            _testFolder?.Dispose();
        }

        /// <summary>
        /// If the import fails before it starts saving the book, the whole book folder goes back to
        /// how it was: its old recordings return, and whatever the failed import added or changed is
        /// undone.
        /// </summary>
        [Test]
        public void ImportWithBackupAsync_ImportFails_PutsBookBackAsItWas()
        {
            var result = RunFailingImport(failAfterSaveStarted: false);
            Assert.That(RobustFile.ReadAllText(result.OldRecording), Is.EqualTo("old recording"));
            Assert.That(
                RobustFile.ReadAllText(result.NestedRecording),
                Is.EqualTo("nested recording")
            );
            Assert.That(
                RobustFile.Exists(result.NewRecording),
                Is.False,
                "The recording the failed import added should be gone."
            );
            Assert.That(
                RobustFile.Exists(result.NewPicture),
                Is.False,
                "The picture the failed import added should be gone."
            );
            Assert.That(
                RobustFile.ReadAllText(_bookPath),
                Is.EqualTo(kOriginalHtml),
                "The book's file, which the failed import changed, should be as it was."
            );
            Assert.That(
                result.Progress.ProgressMessages,
                Does.Contain("The import failed, so the book was put back as it was.")
            );
            Assert.That(result.Progress.Errors, Is.Empty);
        }

        /// <summary>
        /// The backup leaves out hidden files and the AI Image Editor's folder, so putting the book
        /// back must leave them alone rather than delete what it cannot restore.
        /// </summary>
        [Test]
        public void ImportWithBackupAsync_ImportFails_KeepsWhatTheBackupLeavesOut()
        {
            var aiHistory = Path.Combine(_bookFolder, ".ai-image-editor", "history", "edit1.png");
            Directory.CreateDirectory(Path.GetDirectoryName(aiHistory));
            RobustFile.WriteAllText(aiHistory, "an earlier AI edit");
            var hiddenFile = Path.Combine(_bookFolder, "hidden.txt");
            RobustFile.WriteAllText(hiddenFile, "hidden");
            File.SetAttributes(hiddenFile, FileAttributes.Hidden);

            var result = RunFailingImport(failAfterSaveStarted: false);

            Assert.That(
                RobustFile.ReadAllText(result.OldRecording),
                Is.EqualTo("old recording"),
                "Sanity check: the book should have been put back."
            );
            Assert.That(RobustFile.ReadAllText(aiHistory), Is.EqualTo("an earlier AI edit"));
            Assert.That(RobustFile.ReadAllText(hiddenFile), Is.EqualTo("hidden"));
        }

        /// <summary>
        /// A read-only thumbnail is a custom one Bloom must not regenerate. The failed import didn't
        /// touch it, so putting the book back leaves it alone, still read-only.
        /// </summary>
        [Test]
        public void ImportWithBackupAsync_ImportFails_LeavesUntouchedReadOnlyFileReadOnly()
        {
            var thumbnail = Path.Combine(_bookFolder, "thumbnail.png");
            RobustFile.WriteAllText(thumbnail, "custom thumbnail");
            File.SetAttributes(thumbnail, FileAttributes.ReadOnly);
            try
            {
                var result = RunFailingImport(failAfterSaveStarted: false);

                Assert.That(result.Progress.Errors, Is.Empty);
                Assert.That(
                    RobustFile.ReadAllText(result.OldRecording),
                    Is.EqualTo("old recording"),
                    "Sanity check: the book should have been put back."
                );
                Assert.That(RobustFile.ReadAllText(thumbnail), Is.EqualTo("custom thumbnail"));
                Assert.That(
                    File.GetAttributes(thumbnail).HasFlag(FileAttributes.ReadOnly),
                    Is.True,
                    "The custom thumbnail should still be read-only."
                );
            }
            finally
            {
                File.SetAttributes(thumbnail, FileAttributes.Normal); // so TearDown can delete it
            }
        }

        /// <summary>
        /// If the failed import had put a read-only file over one of the book's own, the book's own
        /// comes back, as it was: writable.
        /// </summary>
        [Test]
        public void ImportWithBackupAsync_ImportReplacedFileWithReadOnlyOne_RestoresOriginalWritable()
        {
            var picture = Path.Combine(_bookFolder, "picture.png");
            RobustFile.WriteAllText(picture, "original picture");
            var progress = new ProgressSpy();
            try
            {
                Assert.ThrowsAsync<ApplicationException>(() =>
                    SpreadsheetImporter.ImportWithBackupAsync(
                        _bookFolder,
                        progress,
                        () =>
                        {
                            RobustFile.WriteAllText(picture, "imported picture");
                            File.SetAttributes(picture, FileAttributes.ReadOnly);
                            throw new ApplicationException("import failed");
                        },
                        () => false
                    )
                );
                Assert.That(RobustFile.ReadAllText(picture), Is.EqualTo("original picture"));
                Assert.That(
                    File.GetAttributes(picture).HasFlag(FileAttributes.ReadOnly),
                    Is.False,
                    "The original picture was writable, so it should be again."
                );
            }
            finally
            {
                File.SetAttributes(picture, FileAttributes.Normal);
                DeleteBackup(progress);
            }
        }

        /// <summary>
        /// A book whose main file is read-only was protected on purpose: the import is refused before
        /// anything, even the recordings, is touched.
        /// </summary>
        [Test]
        public void ImportWithBackupAsync_BookFileReadOnly_RefusesAndChangesNothing()
        {
            var recording = Path.Combine(_bookFolder, "audio", "old-recording.mp3");
            Directory.CreateDirectory(Path.GetDirectoryName(recording));
            RobustFile.WriteAllText(recording, "old recording");
            File.SetAttributes(_bookPath, FileAttributes.ReadOnly);
            var progress = new ProgressSpy();
            var importRan = false;
            try
            {
                var imported = SpreadsheetImporter
                    .ImportWithBackupAsync(
                        _bookFolder,
                        progress,
                        () =>
                        {
                            importRan = true;
                            return System.Threading.Tasks.Task.CompletedTask;
                        },
                        () => false
                    )
                    .GetAwaiter()
                    .GetResult();

                Assert.That(imported, Is.False);
                Assert.That(importRan, Is.False);
                Assert.That(RobustFile.ReadAllText(recording), Is.EqualTo("old recording"));
                Assert.That(progress.Errors.Count, Is.EqualTo(1));
                Assert.That(progress.Errors[0], Does.Contain("is read-only"));
                Assert.That(
                    progress.ProgressMessages,
                    Has.None.StartsWith("Backup completed"),
                    "No backup should have been made."
                );
            }
            finally
            {
                File.SetAttributes(_bookPath, FileAttributes.Normal); // so TearDown can delete it
            }
        }

        /// <summary>
        /// Once the import has started saving the book, the save may have renamed its folder, so a
        /// later failure must leave the folder alone and say where the backup is.
        /// </summary>
        [Test]
        public void ImportWithBackupAsync_FailsWhileSaving_LeavesBookAloneAndReportsBackup()
        {
            var result = RunFailingImport(failAfterSaveStarted: true);
            Assert.That(RobustFile.Exists(result.NewRecording), Is.True);
            Assert.That(RobustFile.Exists(result.NewPicture), Is.True);
            Assert.That(RobustFile.Exists(result.OldRecording), Is.False);
            Assert.That(result.Progress.Errors.Count, Is.EqualTo(1));
            Assert.That(
                result.Progress.Errors[0],
                Does.StartWith("The import failed while saving the book.")
                    .And.Contain("is in the backup at")
            );
        }

        // What RunFailingImport made and reported.
        private class FailingImportResult
        {
            public string OldRecording;
            public string NestedRecording;
            public string NewRecording;
            public string NewPicture;
            public ProgressSpy Progress;
        }

        // Give the book some recordings, then run ImportWithBackupAsync with an import that adds a
        // recording and a picture, changes the book's file, and throws, either before or after it says
        // it has started saving the book.
        private FailingImportResult RunFailingImport(bool failAfterSaveStarted)
        {
            var bookAudioFolder = Path.Combine(_bookFolder, "audio");
            var nestedFolder = Path.Combine(bookAudioFolder, "nested");
            Directory.CreateDirectory(nestedFolder);
            var result = new FailingImportResult
            {
                OldRecording = Path.Combine(bookAudioFolder, "old-recording.mp3"),
                NestedRecording = Path.Combine(nestedFolder, "nested.mp3"),
                NewRecording = Path.Combine(bookAudioFolder, "new-recording.mp3"),
                NewPicture = Path.Combine(_bookFolder, "new-picture.png"),
                Progress = new ProgressSpy(),
            };
            RobustFile.WriteAllText(result.OldRecording, "old recording");
            RobustFile.WriteAllText(result.NestedRecording, "nested recording");

            try
            {
                var ex = Assert.ThrowsAsync<ApplicationException>(() =>
                    SpreadsheetImporter.ImportWithBackupAsync(
                        _bookFolder,
                        result.Progress,
                        () =>
                        {
                            Assert.That(
                                RobustFile.Exists(result.OldRecording),
                                Is.False,
                                "Sanity check: the old recordings should be cleared before the import runs."
                            );
                            Directory.CreateDirectory(bookAudioFolder);
                            RobustFile.WriteAllText(result.NewRecording, "new recording");
                            RobustFile.WriteAllText(result.NewPicture, "new picture");
                            RobustFile.WriteAllText(_bookPath, "<html><body>changed</body></html>");
                            throw new ApplicationException("import failed");
                        },
                        () => failAfterSaveStarted
                    )
                );
                Assert.That(ex.Message, Is.EqualTo("import failed"));
            }
            finally
            {
                DeleteBackup(result.Progress);
            }
            return result;
        }

        // Delete the backup ImportWithBackupAsync reported making, if any.
        private static void DeleteBackup(ProgressSpy progress)
        {
            var backup = progress
                .ProgressMessages.Select(m => Regex.Match(m, @"Backup completed \(at (.*)\)"))
                .FirstOrDefault(m => m.Success);
            if (backup != null)
                RobustFile.Delete(backup.Groups[1].Value);
        }
    }
}
