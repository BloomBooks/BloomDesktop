using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Spreadsheet;
using CommandLine;

namespace Bloom.CLI
{
    /// <summary>
    /// Exports a book to an xslx spreadsheet
    /// usage:
    ///		spreadsheetExport [--params {path}] -o {path to put book folder} {path to book file}
    /// The actual output folder will be the specified output folder combined with the book name.
    /// </summary>
    class SpreadsheetExportCommand
    {
        public static Task<int> Handle(SpreadsheetExportParameters options)
        {
            try
            {
                var collectionSettings = GetCollectionSettings(options.BookPath);
                var exporter = new SpreadsheetExporter(null, collectionSettings);
                if (!string.IsNullOrEmpty(options.ParamsPath))
                {
                    exporter.Params = SpreadsheetExportParams.FromFile(options.ParamsPath);
                }
                var dom = new HtmlDom(
                    XmlHtmlConverter.GetXmlDomFromHtmlFile(options.BookPath, false)
                );
                string imagesFolderPath = Path.GetDirectoryName(options.BookPath);
                // It's too dangerous to use the output path they gave us, since we're going to wipe out any existing
                // content of the directory we pass to ExportToFolder. If they give us a parent folder by mistake, that
                // could be something huge, like "my documents". So assume it IS a parent folder, and make one within it.
                string outputFolderPath = Path.Combine(
                    options.OutputPath,
                    Path.GetFileNameWithoutExtension(imagesFolderPath)
                );
                var _sheet = exporter.ExportToFolder(
                    dom,
                    imagesFolderPath,
                    outputFolderPath,
                    out string unused,
                    null,
                    options.Overwrite ? OverwriteOptions.Overwrite : OverwriteOptions.Quit
                );
                return Task.FromResult(0); // all went well
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return Task.FromResult(1);
            }
        }

        private static CollectionSettings GetCollectionSettings(string bookPath)
        {
            string bookFolder = Path.GetDirectoryName(bookPath);
            string collectionFolder = Directory.GetParent(bookFolder).FullName;
            var collectionSettingsFile = CollectionSettings.GetSettingsFilePath(collectionFolder);
            return new CollectionSettings(collectionSettingsFile);
        }
    }

    // Used with https://github.com/gsscoder/commandline, which we get via nuget.
    // (using the beta of commandline 2.0, as of Bloom 3.8)

    [Verb(
        "spreadsheetExport",
        HelpText = "Export a book to an Excel spreadsheet (xslx). See https://docs.google.com/document/d/1Fg95M3Q-8bUHMAGrsvDnnuKKXKQGGBTaVa7EaizqWwg/edit?usp=sharing"
    )]
    public class SpreadsheetExportParameters
    {
        [Value(
            0,
            MetaName = "path",
            HelpText = "Path to the book (htm file) to export.",
            Required = true
        )]
        public string BookPath { get; set; }

        [Option(
            'o',
            "output",
            HelpText = "The folder where the output folder will be created",
            Required = true
        )]
        public string OutputPath { get; set; }

        [Option(
            'p',
            "params",
            HelpText = "Path to a file containing parameters for the export",
            Required = false
        )]
        public string ParamsPath { get; set; }

        [Option(
            'y',
            "overwrite",
            HelpText = "True to overwrite an existing output folder",
            Required = false
        )]
        public bool Overwrite { get; set; }
    }
}
