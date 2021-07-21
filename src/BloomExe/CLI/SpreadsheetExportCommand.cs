using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Book;
using Bloom.Spreadsheet;
using CommandLine;

namespace Bloom.CLI
{
	/// <summary>
	/// Exports a book to an xslx spreadsheet
	/// usage:
	///		spreadsheetExport [--params {path}] -o {path to put xlsx} {path to book file}
	/// </summary>
	class SpreadsheetExportCommand
	{
		public static int Handle(SpreadsheetExportParameters options)
		{
			try
			{
				var dom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(options.BookPath, false));
				var exporter = new SpreadsheetExporter();
				if (!string.IsNullOrEmpty(options.ParamsPath))
					exporter.Params = SpreadsheetExportParams.FromFile(options.ParamsPath);
				string imagesFolderPath = Path.GetDirectoryName(options.BookPath);
				var _sheet = exporter.Export(dom, imagesFolderPath);
				_sheet.WriteToFile(options.OutputPath);
				return 0; // all went well
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.Message);
				Console.WriteLine(ex.Message);
				Console.WriteLine(ex.StackTrace);
				return 1;
			}
		}
	}


	// Used with https://github.com/gsscoder/commandline, which we get via nuget.
	// (using the beta of commandline 2.0, as of Bloom 3.8)

	[Verb("spreadsheetExport", HelpText = "Export a book to an Excel spreadsheet (xslx). See https://docs.google.com/document/d/1Fg95M3Q-8bUHMAGrsvDnnuKKXKQGGBTaVa7EaizqWwg/edit?usp=sharing")]
	public class SpreadsheetExportParameters
	{
		[Value(0, MetaName = "path", HelpText = "Path to the book (htm file) to export.", Required = true)]
		public string BookPath { get; set; }

		[Option('o', "output", HelpText = "The (xlsx) file where the output will be written", Required = true)]
		public string OutputPath { get; set; }

		[Option('p', "params", HelpText = "Path to a file containing parameters for the export", Required = false)]
		public string ParamsPath { get; set; }
	}
}
