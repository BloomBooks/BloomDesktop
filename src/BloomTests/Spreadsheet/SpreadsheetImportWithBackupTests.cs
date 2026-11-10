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
    /// clears the book's recordings before importing, and must put them back if the import fails
    /// before it has started saving the book.
    /// </summary>
    [TestFixture]
    public class SpreadsheetImportWithBackupTests
    {
        private TemporaryFolder _testFolder;
        private string _bookFolder;

        /// <summary>
        /// Make an empty book folder to import into.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _testFolder = new TemporaryFolder("SpreadsheetImportWithBackupTests");
            _bookFolder = Path.Combine(_testFolder.FolderPath, "Book");
            Directory.CreateDirectory(_bookFolder);
            RobustFile.WriteAllText(
                Path.Combine(_bookFolder, "Book.htm"),
                "<html><body></body></html>"
            );
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
        /// If the import fails after the book's audio folder was cleared, the book's text is not saved,
        /// so its old recordings must be put back, and any the failed import added removed.
        /// </summary>
        [Test]
        public void ImportWithBackupAsync_ImportFails_PutsOldRecordingsBack()
        {
            var (oldRecording, nestedRecording, newRecording, progress) = RunFailingImport(
                failAfterSaveStarted: false
            );
            Assert.That(RobustFile.ReadAllText(oldRecording), Is.EqualTo("old recording"));
            Assert.That(RobustFile.ReadAllText(nestedRecording), Is.EqualTo("nested recording"));
            Assert.That(
                RobustFile.Exists(newRecording),
                Is.False,
                "The recording the failed import added should be gone."
            );
            Assert.That(
                progress.ProgressMessages,
                Does.Contain(
                    "The import failed, so the book's recordings were put back as they were."
                )
            );
            Assert.That(progress.Errors, Is.Empty);
        }

        /// <summary>
        /// Once the import has started saving the book, the saved book may already point at the new
        /// recordings, so a later failure must leave them alone and say where the backup is.
        /// </summary>
        [Test]
        public void ImportWithBackupAsync_FailsWhileSaving_LeavesNewRecordingsAndReportsBackup()
        {
            var (oldRecording, _, newRecording, progress) = RunFailingImport(
                failAfterSaveStarted: true
            );
            Assert.That(RobustFile.Exists(newRecording), Is.True);
            Assert.That(RobustFile.Exists(oldRecording), Is.False);
            Assert.That(progress.Errors.Count, Is.EqualTo(1));
            Assert.That(
                progress.Errors[0],
                Does.StartWith("The import failed while saving the book.")
                    .And.Contain("is in the backup at")
            );
        }

        // Give the book some recordings, then run ImportWithBackupAsync with an import that adds a
        // recording and throws, either before or after it says it has started saving the book.
        private (string, string, string, ProgressSpy) RunFailingImport(bool failAfterSaveStarted)
        {
            var bookAudioFolder = Path.Combine(_bookFolder, "audio");
            var nestedFolder = Path.Combine(bookAudioFolder, "nested");
            Directory.CreateDirectory(nestedFolder);
            var oldRecording = Path.Combine(bookAudioFolder, "old-recording.mp3");
            var nestedRecording = Path.Combine(nestedFolder, "nested.mp3");
            RobustFile.WriteAllText(oldRecording, "old recording");
            RobustFile.WriteAllText(nestedRecording, "nested recording");
            var newRecording = Path.Combine(bookAudioFolder, "new-recording.mp3");

            var progress = new ProgressSpy();
            try
            {
                var ex = Assert.ThrowsAsync<ApplicationException>(() =>
                    SpreadsheetImporter.ImportWithBackupAsync(
                        _bookFolder,
                        progress,
                        () =>
                        {
                            Assert.That(
                                RobustFile.Exists(oldRecording),
                                Is.False,
                                "Sanity check: the old recordings should be cleared before the import runs."
                            );
                            Directory.CreateDirectory(bookAudioFolder);
                            RobustFile.WriteAllText(newRecording, "new recording");
                            throw new ApplicationException("import failed");
                        },
                        () => failAfterSaveStarted
                    )
                );
                Assert.That(ex.Message, Is.EqualTo("import failed"));
            }
            finally
            {
                var backup = progress
                    .ProgressMessages.Select(m => Regex.Match(m, @"Backup completed \(at (.*)\)"))
                    .FirstOrDefault(m => m.Success);
                if (backup != null)
                    RobustFile.Delete(backup.Groups[1].Value);
            }
            return (oldRecording, nestedRecording, newRecording, progress);
        }
    }
}
