using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bloom;
using Bloom.Book;
using Bloom.CLI;
using Bloom.SafeXml;
using Bloom.Spreadsheet;
using Bloom.web;
using BloomTemp;
using BloomTests.Spreadsheet;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.CLI
{
    /// <summary>
    /// Runs the command-line `spreadsheetImport` verb the way Program.Main does: the real
    /// SpreadsheetImportCommand.Handle, inside Program.RunConsoleCommandLoop, with the real
    /// SpreadsheetImporter (not TestSpreadsheetImporter) so that importing audio uses a real
    /// WebView2 to split the text into sentences and compute recording checksums.
    /// The in-app import of the same spreadsheet worked while this one failed (BL-16773).
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class SpreadsheetImportCommandTests
    {
        private const string kAudioId = "i9c7f4e02-4685-48fc-8653-71d88f218706";

        private TemporaryFolder _testFolder;
        private string _bookPath;
        private string _bookFolder;
        private string _spreadsheetPath;

        /// <summary>
        /// Make a book to import into, and a spreadsheet with one sentence and its recording.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _testFolder = new TemporaryFolder("SpreadsheetImportCommandTests");
            _bookFolder = Path.Combine(_testFolder.FolderPath, "Book");
            Directory.CreateDirectory(_bookFolder);
            _bookPath = Path.Combine(_bookFolder, "Book.htm");
            var xml = string.Format(
                SpreadsheetImageAndTextImportTests.templateDom,
                SpreadsheetImageAndTextImportTests.coverPage
                    + SpreadsheetImageAndTextImportTests.insideBackCoverPage
                    + SpreadsheetImageAndTextImportTests.backCoverPage
            );
            RobustFile.WriteAllText(_bookPath, xml.Trim());

            var spreadsheetFolder = Path.Combine(_testFolder.FolderPath, "Spreadsheet");
            var spreadsheetAudioFolder = Path.Combine(spreadsheetFolder, "audio");
            Directory.CreateDirectory(spreadsheetAudioFolder);
            var testAudioFolder = FileLocationUtilities.GetDirectoryDistributedWithApplication(
                "src/BloomTests/Spreadsheet/testAudio/"
            );
            RobustFile.Copy(
                Path.Combine(testAudioFolder, kAudioId + ".mp3"),
                Path.Combine(spreadsheetAudioFolder, kAudioId + ".mp3")
            );

            // One page with one sentence of French text and one recording of it, in sentence mode.
            var sheet = new InternalSpreadsheet();
            var columnForFr = sheet.AddColumnForLang("fr", "French");
            var columnForFrAudio = sheet.AddColumnForLangAudio("fr", "French");
            var contentRow = new ContentRow(sheet);
            contentRow.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow.SetCell(columnForFr, "<p>This is page one.</p>");
            contentRow.SetCell(columnForFrAudio, $"./audio/{kAudioId}.mp3");
            _spreadsheetPath = Path.Combine(spreadsheetFolder, "Book.xlsx");
            sheet.WriteToFile(_spreadsheetPath);
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
        /// A spreadsheet path that does not exist should get a plain message and a failure exit code,
        /// not a NullReferenceException.
        /// </summary>
        [Test]
        public void Handle_SpreadsheetDoesNotExist_ReportsItAndFails()
        {
            var missing = Path.Combine(_testFolder.FolderPath, "NotThere.xlsx");
            var (exitCode, output) = RunWithoutBrowser(_bookPath, missing);
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(
                output,
                Does.Contain($"Error: The spreadsheet '{missing}' does not exist.")
            );
            Assert.That(output, Does.Not.Contain("Exception"));
        }

        /// <summary>
        /// A book path that does not exist should get a plain message and a failure exit code.
        /// </summary>
        [Test]
        public void Handle_BookDoesNotExist_ReportsItAndFails()
        {
            var missing = Path.Combine(_testFolder.FolderPath, "NoBook", "NoBook.htm");
            var (exitCode, output) = RunWithoutBrowser(missing, _spreadsheetPath);
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Does.Contain($"Error: The book file '{missing}' does not exist."));
            Assert.That(output, Does.Not.Contain("Exception"));
        }

        // For the cases that fail before any browser work, so need no message loop.
        private static (int, string) RunWithoutBrowser(string bookPath, string inputPath)
        {
            var output = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(output);
            try
            {
                var task = SpreadsheetImportCommand.Handle(
                    new SpreadsheetImportParameters { BookPath = bookPath, InputPath = inputPath }
                );
                Assert.That(
                    task.IsCompleted,
                    Is.True,
                    "Sanity check: a bad path should be rejected before the command awaits anything."
                );
                return (task.Result, output.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        /// <summary>
        /// Importing a spreadsheet that carries audio from the command line should give the book the
        /// recording, just as importing it from inside Bloom does.
        /// </summary>
        [Test]
        public void Handle_SpreadsheetWithAudio_ImportsTheAudio()
        {
            Assert.That(
                Directory.Exists(Path.Combine(_bookFolder, "audio")),
                Is.False,
                "Sanity check: the book should start with no audio."
            );
            Assert.That(
                RobustFile.Exists(_spreadsheetPath),
                Is.True,
                "Sanity check: setup should have written the spreadsheet."
            );

            var (exitCode, consoleOutput) = RunInCommandLoop();
            Assert.That(
                exitCode,
                Is.EqualTo(0),
                "The import should succeed. Console output:\n" + consoleOutput
            );

            var sentences = GetFrenchSentences();
            Assert.That(
                sentences.Count,
                Is.EqualTo(1),
                "The one French sentence should have become one recorded sentence. Console output:\n"
                    + consoleOutput
            );
            var sentence = sentences[0];
            Assert.That(sentence.InnerText, Is.EqualTo("This is page one."));
            Assert.That(sentence.GetAttribute("id"), Is.EqualTo(kAudioId));
            Assert.That(
                sentence.GetAttribute("data-duration"),
                Is.Not.Empty,
                "The recording's duration should be recorded."
            );
            Assert.That(
                sentence.GetAttribute("recordingmd5"),
                Is.Not.Empty,
                "The checksum of the recorded text, which comes from the browser, should be recorded."
            );
            Assert.That(
                RobustFile.Exists(Path.Combine(_bookFolder, "audio", kAudioId + ".mp3")),
                Is.True,
                "The recording should have been copied into the book's audio folder."
            );
        }

        /// <summary>
        /// Like the in-app import, the command-line import replaces the book's recordings with the
        /// spreadsheet's, so a recording the spreadsheet does not mention is gone afterwards.
        /// </summary>
        [Test]
        public void Handle_BookHasOldRecordings_TheyAreRemoved()
        {
            var bookAudioFolder = Path.Combine(_bookFolder, "audio");
            Directory.CreateDirectory(bookAudioFolder);
            var oldRecording = Path.Combine(bookAudioFolder, "old-recording.mp3");
            RobustFile.WriteAllText(oldRecording, "not really an mp3");

            var (exitCode, consoleOutput) = RunInCommandLoop();
            Assert.That(
                exitCode,
                Is.EqualTo(0),
                "The import should succeed. Console output:\n" + consoleOutput
            );
            Assert.That(
                RobustFile.Exists(oldRecording),
                Is.False,
                "The recording the spreadsheet does not mention should have been removed."
            );
            Assert.That(
                RobustFile.Exists(Path.Combine(bookAudioFolder, kAudioId + ".mp3")),
                Is.True,
                "The spreadsheet's recording should be there."
            );
        }

        /// <summary>
        /// When a recording the spreadsheet names is missing, the rest of the book still imports and is
        /// saved, the problem is reported, and the exit code (2) says the import had errors.
        /// </summary>
        [Test]
        public void Handle_RecordingMissing_SavesTheTextAndExitsWith2()
        {
            var recording = Path.Combine(
                Path.GetDirectoryName(_spreadsheetPath),
                "audio",
                kAudioId + ".mp3"
            );
            RobustFile.Delete(recording);

            var (exitCode, consoleOutput) = RunInCommandLoop();
            Assert.That(
                exitCode,
                Is.EqualTo(2),
                "The import should finish but report errors. Console output:\n" + consoleOutput
            );
            Assert.That(consoleOutput, Does.Contain("Error: Did not import audio on page"));
            var sentences = GetFrenchSentences();
            Assert.That(
                sentences.Count,
                Is.EqualTo(1),
                "The text should still have been imported and saved. Console output:\n"
                    + consoleOutput
            );
            Assert.That(sentences[0].InnerText, Is.EqualTo("This is page one."));
            Assert.That(
                RobustFile.Exists(Path.Combine(_bookFolder, "audio", kAudioId + ".mp3")),
                Is.False
            );
        }

        /// <summary>
        /// If the import fails after the book's audio folder was cleared, the book's text is not saved,
        /// so its old recordings must be put back, and any the failed import added removed.
        /// </summary>
        [Test]
        public void ImportWithBackupAsync_ImportFails_PutsOldRecordingsBack()
        {
            var (oldRecording, nestedRecording, newRecording, output) = RunFailingImport(
                failAfterSaveStarted: false
            );
            Assert.That(RobustFile.ReadAllText(oldRecording), Is.EqualTo("old recording"));
            Assert.That(RobustFile.ReadAllText(nestedRecording), Is.EqualTo("nested recording"));
            Assert.That(
                RobustFile.Exists(newRecording),
                Is.False,
                "The recording the failed import added should be gone."
            );
            Assert.That(output, Does.Contain("the book's recordings were put back as they were"));
        }

        /// <summary>
        /// Once the import has started saving the book, the saved book may already point at the new
        /// recordings, so a later failure must leave them alone and say where the backup is.
        /// </summary>
        [Test]
        public void ImportWithBackupAsync_FailsWhileSaving_LeavesNewRecordingsAndReportsBackup()
        {
            var (oldRecording, _, newRecording, output) = RunFailingImport(
                failAfterSaveStarted: true
            );
            Assert.That(RobustFile.Exists(newRecording), Is.True);
            Assert.That(RobustFile.Exists(oldRecording), Is.False);
            Assert.That(
                output,
                Does.Contain("Error: The import failed while saving the book.")
                    .And.Contain("is in the backup at")
            );
        }

        // Give the book some recordings, then run ImportWithBackupAsync with an import that adds a
        // recording and throws, either before or after it says it has started saving the book.
        private (string, string, string, string) RunFailingImport(bool failAfterSaveStarted)
        {
            var bookAudioFolder = Path.Combine(_bookFolder, "audio");
            var nestedFolder = Path.Combine(bookAudioFolder, "nested");
            Directory.CreateDirectory(nestedFolder);
            var oldRecording = Path.Combine(bookAudioFolder, "old-recording.mp3");
            var nestedRecording = Path.Combine(nestedFolder, "nested.mp3");
            RobustFile.WriteAllText(oldRecording, "old recording");
            RobustFile.WriteAllText(nestedRecording, "nested recording");
            var newRecording = Path.Combine(bookAudioFolder, "new-recording.mp3");

            var output = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(output);
            try
            {
                var ex = Assert.ThrowsAsync<ApplicationException>(() =>
                    SpreadsheetImporter.ImportWithBackupAsync(
                        _bookFolder,
                        new ConsoleWebSocketProgress(),
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
                Console.SetOut(originalOut);
                var backup = Regex.Match(output.ToString(), @"Backup completed \(at (.*)\)");
                if (backup.Success)
                    RobustFile.Delete(backup.Groups[1].Value);
            }
            return (oldRecording, nestedRecording, newRecording, output.ToString());
        }

        // Run the verb on the test book and spreadsheet the way Program.Main does, with the real
        // browser, returning the exit code and what it wrote to the console.
        private (int, string) RunInCommandLoop()
        {
            var options = new SpreadsheetImportParameters
            {
                BookPath = _bookPath,
                InputPath = _spreadsheetPath,
            };
            var output = new StringWriter();
            var originalOut = Console.Out;
            int exitCode;
            Console.SetOut(output);
            try
            {
                exitCode = Program.RunConsoleCommandLoop(async () =>
                {
                    var import = SpreadsheetImportCommand.Handle(
                        options,
                        RetiredTestServers.Retire
                    );
                    // A broken import may never finish; don't let it hang the test run.
                    if (await Task.WhenAny(import, Task.Delay(60000)) != import)
                        return -1;
                    return await import;
                });
            }
            finally
            {
                Console.SetOut(originalOut);
            }
            var consoleOutput = output.ToString();
            Assert.That(
                exitCode,
                Is.Not.EqualTo(-1),
                "The import did not finish within a minute. Console output:\n" + consoleOutput
            );
            return (exitCode, consoleOutput);
        }

        // The recorded sentences in the saved book's French text.
        private List<SafeXmlElement> GetFrenchSentences()
        {
            var dom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(_bookPath, false));
            return dom.SafeSelectNodes(
                    "//div[contains(@class,'bloom-editable') and @lang='fr']//span[contains(@class,'audio-sentence')]"
                )
                .Cast<SafeXmlElement>()
                .ToList();
        }
    }
}
