// The command-line `spreadsheetImport` verb is commented out (BL-16773). It dates from before
// Bloom had a way to import a spreadsheet in its UI, and no one is known to use it, so it is not
// worth maintaining or testing. Importing from Bloom's UI (SpreadsheetImporter.ImportWithProgressAsync)
// is unaffected.
//
// The code below is kept in case we want the verb again. It is the version that also imports
// audio: it starts a local server for the helper browser audio import needs, checks its
// arguments, prints progress, and runs through SpreadsheetImporter.ImportWithBackupAsync like the
// in-app import. To bring it back, uncomment this file, the three places in Program.cs that mention
// spreadsheetImport/SpreadsheetImport, the verb in BloomFreezeDoctor.Core's BloomChannel.IsHeadlessRun,
// and its tests in src/BloomTests/CLI/SpreadsheetImportCommandTests.cs.

/*
using System;
using System.IO;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.Book;
using Bloom.ImageProcessing;
using Bloom.Spreadsheet;
using Bloom.web;
using CommandLine;
using SIL.IO;

namespace Bloom.CLI
{
    /// <summary>
    /// Imports a book from an xslx spreadsheet
    /// usage:
    ///		spreadsheetImport [--params {path}] -i {path to get xslx} {path to book file}
    /// </summary>
    class SpreadsheetImportCommand
    {
        /// <summary>
        /// Run the spreadsheetImport verb, writing progress and problems to the console.
        /// </summary>
        /// <returns>the process exit code: 0 for success; 1 when the import could not be done;
        /// 2 when the import finished and the book was saved, but errors were reported (for example,
        /// a recording that could not be imported)</returns>
        public static Task<int> Handle(SpreadsheetImportParameters options)
        {
            return Handle(options, server => server.Dispose());
        }

        /// <summary>
        /// As above, but the caller decides what happens to the local server the import starts once
        /// the import is done with it. Tests must not dispose a server on the spot (see
        /// RetiredTestServers in BloomTests), so they pass something else.
        /// </summary>
        internal static async Task<int> Handle(
            SpreadsheetImportParameters options,
            Action<BloomServer> releaseServer
        )
        {
            try
            {
                var progress = new ConsoleWebSocketProgress();
                if (!RobustFile.Exists(options.BookPath))
                {
                    progress.MessageWithoutLocalizing(
                        $"The book file '{options.BookPath}' does not exist.",
                        ProgressKind.Error
                    );
                    return 1;
                }
                if (!RobustFile.Exists(options.InputPath))
                {
                    progress.MessageWithoutLocalizing(
                        $"The spreadsheet '{options.InputPath}' does not exist.",
                        ProgressKind.Error
                    );
                    return 1;
                }
                string folderPath = Directory.GetParent(options.BookPath).FullName;
                // ReadFromFile and Validate report any problem to the progress themselves.
                var sheet = InternalSpreadsheet.ReadFromFile(options.InputPath, progress);
                if (sheet == null)
                    return 1;
                var dom = new HtmlDom(
                    XmlHtmlConverter.GetXmlDomFromHtmlFile(options.BookPath, false)
                );
                var importer = new SpreadsheetImporter(
                    null,
                    dom,
                    Path.GetDirectoryName(options.InputPath),
                    folderPath
                );
                if (!string.IsNullOrEmpty(options.ParamsPath))
                    importer.Params = SpreadsheetImportParams.FromFile(options.ParamsPath);
                if (!importer.Validate(sheet, progress))
                    return 1;
                // Known problem, not fixed while the verb is retired: if the spreadsheet sits beside
                // the book and its recordings are in the book's own audio folder, ImportWithBackupAsync
                // deletes them before the import reads them. They are only reported as missing, so
                // the book is saved without them (it is still in the backup).
                var hasStartedSaving = false;
                await SpreadsheetImporter.ImportWithBackupAsync(
                    folderPath,
                    progress,
                    async () =>
                    {
                        // Importing audio uses a browser (to split text into sentences and to
                        // checksum it), and that browser loads its page from our local http server.
                        // In the app that server is always running; a console verb has to start one
                        // itself. Without it the page URL points at port 0 and the page never loads
                        // (BL-16773).
                        var server = new BloomServer(
                            new RuntimeImageProcessor(new BookRenamedEvent()),
                            new BookSelection()
                        );
                        try
                        {
                            server.EnsureListening();
                            // Warnings and errors go to the console through the progress as they happen.
                            await importer.ImportAsync(sheet, progress);
                        }
                        finally
                        {
                            releaseServer(server);
                        }
                        // Review: A lot of other stuff happens in Book.Save() and BookStorage.SaveHtml().
                        // I doubt we need any of it for current purposes, but later we might.
                        hasStartedSaving = true;
                        XmlHtmlConverter.SaveDOMAsHtml5(dom.RawDom, options.BookPath);
                    },
                    () => hasStartedSaving
                );
                if (progress.HaveErrorsBeenReported)
                {
                    // Errors here are things like a missing recording: the rest of the book imported
                    // and has been saved, but something the spreadsheet asked for is not in it.
                    Console.WriteLine("done, with errors");
                    return 2;
                }
                Console.WriteLine("done");
                return 0; // all went well
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return 1;
            }
        }
    }

    /// <summary>
    /// Writes progress messages to the console, for command-line verbs that have no progress dialog.
    /// Problems are marked, so they stand out from ordinary progress.
    /// (This belongs in web/WebSocketProgress.cs if it is brought back.)
    /// </summary>
    public class ConsoleWebSocketProgress : WebSocketProgress
    {
        /// <summary>
        /// True once an Error or Fatal message has been written (warnings don't count), so a
        /// command can exit with a code that says something went wrong.
        /// </summary>
        public bool HaveErrorsBeenReported { get; private set; }

        /// <summary>
        /// Write the message to the console, prefixed by its kind unless it is ordinary progress.
        /// </summary>
        public override void MessageWithoutLocalizing(
            string message,
            ProgressKind kind = ProgressKind.Progress
        )
        {
            base.MessageWithoutLocalizing(message, kind);
            switch (kind)
            {
                case ProgressKind.Fatal:
                case ProgressKind.Error:
                    HaveErrorsBeenReported = true;
                    Console.WriteLine("Error: " + message);
                    break;
                case ProgressKind.Warning:
                    Console.WriteLine("Warning: " + message);
                    break;
                default:
                    Console.WriteLine(message);
                    break;
            }
        }

        /// <summary>
        /// A prefix only changes which localization ids are looked up. Like NullWebSocketProgress,
        /// this ignores it, so messages that would have used it come out in English.
        /// </summary>
        public override WebSocketProgress WithL10NPrefix(string localizationIdPrefix)
        {
            return this;
        }
    }

    // Used with https://github.com/gsscoder/commandline, which we get via nuget.
    // (using the beta of commandline 2.0, as of Bloom 3.8)

    [Verb(
        "spreadsheetImport",
        HelpText = "Import a book from an Excel spreadsheet (xslx). See https://docs.google.com/document/d/1Fg95M3Q-8bUHMAGrsvDnnuKKXKQGGBTaVa7EaizqWwg/edit?usp=sharing"
    )]
    public class SpreadsheetImportParameters
    {
        [Value(
            0,
            MetaName = "path",
            HelpText = "Path to the book (htm file) to import into.",
            Required = true
        )]
        public string BookPath { get; set; }

        [Option('i', "input", HelpText = "The xlsx file to be imported", Required = true)]
        public string InputPath { get; set; }

        [Option(
            'p',
            "params",
            HelpText = "Path to a file containing parameters for the import",
            Required = false
        )]
        public string ParamsPath { get; set; }
    }
}
*/
