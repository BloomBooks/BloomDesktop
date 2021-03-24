using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Bloom.Book;
using CommandLine;
using SIL.IO;
using Directory = System.IO.Directory;

namespace Bloom.CLI
{
	[Flags]
	enum GetUsedFontsExitCode
	{
		Success = 0,
		BookPathDirectoryNotFound = 1,
		ReportIOError = 2,
		UnhandledException = 4
	}

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
			try
			{
				if (!Directory.Exists(options.BookPath))
				{
					if (options.BookPath.Contains(".htm"))
					{
						Debug.WriteLine("Supply only the directory, not the path to the file.");
						Console.Error.WriteLine("Supply only the directory, not the path to the file.");
					}
					else
					{
						Debug.WriteLine("Could not find " + options.BookPath);
						Console.Error.WriteLine("Could not find " + options.BookPath);
					}
					return (int)GetUsedFontsExitCode.BookPathDirectoryNotFound;
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

				var fonts = GetFontsUsed(options.BookPath).ToList();
				fonts.Sort();

				Directory.CreateDirectory(Path.GetDirectoryName(options.ReportPath));

				try
				{
					using (var report = new StreamWriter(options.ReportPath))
					{
						foreach (var font in fonts)
						{
							report.WriteLine(font);
						}
					}
				}
				catch (IOException e)
				{
					string message = "Exception: " + e.ToString();
					Debug.WriteLine(message);
					Console.Error.WriteLine(message);
					return (int)GetUsedFontsExitCode.ReportIOError;
				}

				Console.WriteLine("Finished gathering font data.");
				Debug.WriteLine("Finished gathering font data.");
				return (int)GetUsedFontsExitCode.Success;
			}
			catch (Exception e)
			{
				string message = "Exception: " + e.ToString();
				Debug.WriteLine(message);
				Console.Error.WriteLine(message);
				return (int)GetUsedFontsExitCode.UnhandledException;
			}
		}

		/// <summary>
		/// Examine the stylesheets and collect the font families they mention.
		/// Note that the process used by ePub and bloomPub publication to
		/// determine fonts is more complicated, using the DOM in an actual browser.
		/// </summary>
		/// <returns>Enumerable of font names</returns>
		internal static IEnumerable<string> GetFontsUsed(string bookPath)
		{
			string bookHtmContent = null;
			string defaultLangStylesContent = null;

			var result = new HashSet<string>();
			// Css for styles are contained in the actual html
			foreach (var filePath in Directory.EnumerateFiles(bookPath, "*.*").Where(f => f.EndsWith(".css") || f.EndsWith(".htm") || f.EndsWith(".html")))
			{
				var fileContents = RobustFile.ReadAllText(filePath, Encoding.UTF8);

				if (filePath.EndsWith(".htm"))
					bookHtmContent = fileContents;
				else if (filePath.EndsWith("defaultLangStyles.css"))
				{
					defaultLangStylesContent = fileContents;
					// Delay processing defaultLangStyles to the end when we know we have the htm content.
					continue;
				}

				HtmlDom.FindFontsUsedInCss(fileContents, result, false);
			}

			ProcessDefaultLangStyles(bookHtmContent, defaultLangStylesContent, result);

			return result;
		}

		/// <summary>
		/// Special processing is needed for defaultLangStyles.css.
		/// This file is designed to hold information about each language seen by this book and its ancestors.
		/// But that means we may have font information for a language not present in this version of the book.
		/// We don't want to include those fonts.
		/// </summary>
		private static void ProcessDefaultLangStyles(string bookHtmContent, string defaultLangStylesContent, HashSet<string> result)
		{
			if (bookHtmContent == null || defaultLangStylesContent == null)
				return;

			var htmlDom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtml(bookHtmContent, false));
			var languagesWithContent = htmlDom.GetLanguagesWithContent(true).ToArray();

			// Find something like this
			//	[lang='en']
			//	{
			//		font-family: 'Andika New Basic';
			//		direction: ltr;
			//	}
			Regex languageCssRegex = new Regex(@"\[\s*lang\s*=\s*['""](.*?)['""]\s*\]\s*{.*?}", RegexOptions.Singleline | RegexOptions.Compiled);

			// Remove it for languages which are not in the book.
			foreach (Match match in languageCssRegex.Matches(defaultLangStylesContent))
			{
				var langTag = match.Groups[1].Value;
				if (languagesWithContent.Contains(langTag))
					continue;
				var wholeRuleForLang = match.Groups[0].Value;
				defaultLangStylesContent = defaultLangStylesContent.Replace(wholeRuleForLang, "");
			}

			HtmlDom.FindFontsUsedInCss(defaultLangStylesContent, result, false);
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
