using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Bloom.Book;
using Bloom.Spreadsheet;
using CommandLine;

namespace Bloom.CLI
{
    /// <summary>
    /// Imports a book from an xslx spreadsheet
    /// usage:
    ///		spreadsheetImport [--params {path}] -i {path to get xslx} {path to book file}
    /// </summary>
    class SpreadsheetImportCommand
    {
        public static async Task<int> Handle(SpreadsheetImportParameters options)
        {
            try
            {
                string folderPath = Directory.GetParent(options.BookPath).FullName;
                BookStorage.SaveCopyBeforeImportOverwrite(folderPath);
                var sheet = InternalSpreadsheet.ReadFromFile(options.InputPath);
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
                var messages = await importer.ImportAsync(sheet);
                foreach (var message in messages)
                {
                    Debug.WriteLine(message);
                    Console.WriteLine(message);
                }
                // Review: A lot of other stuff happens in Book.Save() and BookStorage.SaveHtml().
                // I doubt we need any of it for current purposes, but later we might.
                XmlHtmlConverter.SaveDOMAsHtml5(dom.RawDom, options.BookPath);
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
