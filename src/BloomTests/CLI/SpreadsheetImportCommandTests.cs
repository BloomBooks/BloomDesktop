using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bloom;
using Bloom.Book;
using Bloom.CLI;
using Bloom.SafeXml;
using Bloom.Spreadsheet;
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
            Assert.That(
                exitCode,
                Is.EqualTo(0),
                "The import should succeed. Console output:\n" + consoleOutput
            );

            var dom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(_bookPath, false));
            var sentences = dom.SafeSelectNodes(
                    "//div[contains(@class,'bloom-editable') and @lang='fr']//span[contains(@class,'audio-sentence')]"
                )
                .Cast<SafeXmlElement>()
                .ToList();
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
    }
}
