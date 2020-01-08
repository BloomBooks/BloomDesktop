using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Bloom.Publish;
using Bloom.Publish.Epub;
using CommandLine;
using System.Collections.Generic;
using System.Text;

namespace Bloom.CLI
{
	/// <summary>
	/// This generates a list of the fonts used in a book.
	/// Currently it is pretty crude, just detecting what fonts are mentioned
	/// in one of the book's stylesheets. Some of these might not actually be used
	/// (e.g., a fall-back font in one of the sheets shipped with Bloom, or
	/// a font in a style that the user defined but didn't end up using,
	/// or a font that is used only for a specific language we aren't currently
	/// publishing).
	/// </summary>
	class GetUsedFontsCommand
	{
		public static int Handle(GetUsedFontsParameters options)
		{
			if(!Directory.Exists(options.BookPath))
			{
				if(options.BookPath.Contains(".htm"))
				{
					Debug.WriteLine("Supply only the directory, not the path to the file.");
					Console.Error.WriteLine("Supply only the directory, not the path to the file.");
				}
				else
				{
					Debug.WriteLine("Could not find " + options.BookPath);
					Console.Error.WriteLine("Could not find " + options.BookPath);
				}
				return 1;
			}
			Console.WriteLine("Gathering font data.");

			// Some of this might be useful if we end up needing to instantiate the book to figure out what
			// is REALLY needed (as opposed to just mentioned in a style sheet).
			//var collectionFolder = Path.GetDirectoryName(options.BookPath);
			//var projectSettingsPath = Directory.GetFiles(collectionFolder, "*.bloomCollection").FirstOrDefault();
			//var collectionSettings = new CollectionSettings(projectSettingsPath);

			//XMatterPackFinder xmatterFinder = new XMatterPackFinder(new[] {BloomFileLocator.GetInstalledXMatterDirectory()});
			//var locator = new BloomFileLocator(collectionSettings, xmatterFinder, ProjectContext.GetFactoryFileLocations(),
			//	ProjectContext.GetFoundFileLocations(), ProjectContext.GetAfterXMatterFileLocations());

			//var bookInfo = new BookInfo(options.BookPath, true);
			//var book = new Book.Book(bookInfo, new BookStorage(options.BookPath, locator, new BookRenamedEvent(), collectionSettings),
			//	null, collectionSettings, null, null, new BookRefreshEvent());

			//book.BringBookUpToDate(new NullProgress());

			var fonts = GetFontsUsed(options.BookPath, false).ToList();
			fonts.Sort();

			Directory.CreateDirectory(Path.GetDirectoryName(options.ReportPath));

			using (var report = new StreamWriter(options.ReportPath))
			{
				foreach (var font in fonts)
				{
					report.WriteLine(font);
				}
			}
			Console.WriteLine("Finished gathering font data.");
			Debug.WriteLine("Finished gathering font data.");
			return 0;
		}

		/// <summary>
		/// First step of embedding fonts: determine what are used in the document.
		/// Eventually we may load each page into a DOM and use JavaScript to ask each
		/// bit of text what actual font and face it is using.
		/// For now we examine the stylesheets and collect the font families they mention.
		/// </summary>
		public static IEnumerable<string> GetFontsUsed (string bookPath, bool includeFallbackFonts)
		{
			var result = new HashSet<string> ();
			// Css for styles are contained in the actual html
			foreach (var ss in Directory.EnumerateFiles (bookPath, "*.*").Where (f => f.EndsWith (".css") || f.EndsWith (".htm") || f.EndsWith (".html"))) {
				var root = SIL.IO.RobustFile.ReadAllText (ss, Encoding.UTF8);
				Bloom.Book.HtmlDom.FindFontsUsedInCss (root, result, includeFallbackFonts);
			}
			return result;
		}
	}
}

// Used with https://github.com/gsscoder/commandline, which we get via nuget.
// (using the beta of commandline 2.0, as of Bloom 3.8)

[Verb("getfonts", HelpText = "Get the fonts used in a Bloom book. Used by automated converters and app makers.")]
public class GetUsedFontsParameters
{

	[Option("bookpath", HelpText = "path to the book", Required = true)]
	public string BookPath { get; set; }

	// These or similar may be needed eventually for a more accurate list.
	//[Option("VernacularIsoCode", HelpText = "iso code of primary language", Required = true)]
	//public string VernacularIsoCode { get; set; }

	//[Option("NationalLanguage1IsoCode", HelpText = "iso code of secondary language", Default="", Required = false)]
	//	public string NationalLanguage1IsoCode { get; set; }

	//[Option("NationalLanguage2IsoCode", HelpText = "iso code of tertiary language", Default = "", Required = false)]
	//	public string NationalLanguage2IsoCode { get; set; }

	[Option("reportpath", HelpText = "Path to the file to receive the report", Required = true)]
	public string ReportPath { get; set; }
}
