using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Bloom.Book;
using Bloom.Collection;
using L10NSharp;
using XliffForHtml;
using SIL.IO;

namespace Bloom
{
	public class BloomFileLocator : FileLocator
	{
		private readonly CollectionSettings _collectionSettings;
		private readonly XMatterPackFinder _xMatterPackFinder;
		private readonly IEnumerable<string> _factorySearchPaths;
		private readonly List<string> _bookSpecificSearchPaths;
		private readonly IEnumerable<string> _userInstalledSearchPaths;
		private readonly IEnumerable<string> _afterXMatterSearchPaths;

		public static string BrowserRoot
		{
			get
			{
				return Directory.Exists(Path.Combine(FileLocator.DirectoryOfApplicationOrSolution,"output")) ? "output"+Path.DirectorySeparatorChar+"browser" : "browser";
			}
		}

		public static BloomFileLocator sTheMostRecentBloomFileLocator;

		public BloomFileLocator(CollectionSettings collectionSettings, XMatterPackFinder xMatterPackFinder, IEnumerable<string> factorySearchPaths, IEnumerable<string> userInstalledSearchPaths,
			IEnumerable<string> afterXMatterSearchPaths = null)
			: base(factorySearchPaths.Concat( userInstalledSearchPaths))//review: is this even used, since we override GetSearchPaths()?
		{
			if (afterXMatterSearchPaths == null)
			{
				afterXMatterSearchPaths = new string[] {};
			}
			_bookSpecificSearchPaths = new List<string>();
			_collectionSettings = collectionSettings;
			_xMatterPackFinder = xMatterPackFinder;
			_factorySearchPaths = factorySearchPaths;
			_userInstalledSearchPaths = userInstalledSearchPaths;
			_afterXMatterSearchPaths = afterXMatterSearchPaths;

			sTheMostRecentBloomFileLocator = this;
		}

		public override void AddPath(string path)
		{
			_bookSpecificSearchPaths.Add(path);
		}

		/// <summary>
		/// These are used (as of 26 aug 2016) only by LibPalaso's FileLocator.LocateFile(). Not used by GetFileDistributedWIthApplication().
		/// </summary>
		/// <returns></returns>
		protected override IEnumerable<string> GetSearchPaths()
		{
			yield return BloomFileLocator.BrowserRoot;

			//The versions of the files that come with the program should always win out.
			//NB: This should not include any sample books.
			foreach (var searchPath in _factorySearchPaths)
			{
				yield return searchPath;
			}

			//Note: the order here has major ramifications, as it's quite common to have mutliple copies of the same file around
			//in several of our locations.
			//For example, if do this:
			//    return base.GetSearchPaths().Concat(paths);
			//Then we will favor the paths known to the base class over those we just compiled in the lines above.
			//One particular bug that came out of that was when a custom xmatter (because of a previous bug) snuck into the
			//Sample "Vaccinations" book, then *that* copy of the xmatter was always used, becuase it was found first.

			// So, first we want to try the factory xmatter paths. These have precedence over factory templates.
			foreach (var xMatterInfo in _xMatterPackFinder.Factory)
			{
				//NB: if we knew what the xmatter pack they wanted, we could limit to that. for now, we just iterate over all of
				//them and rely (reasonably) on the names being unique

				//this is a bit weird... we include the parent, in case they're looking for the xmatter *folder*, and the folder
				//itself, in case they're looking for something inside it
				yield return xMatterInfo.PathToFolder;
				yield return Path.GetDirectoryName(xMatterInfo.PathToFolder);
			}

			// On the other hand the remaining factory stuff has precedence over non-factory XMatter.
			foreach (var searchPath in _afterXMatterSearchPaths)
			{
				yield return searchPath;
			}

			foreach (var xMatterInfo in _xMatterPackFinder.NonFactory)
			{
				//this is a bit weird... we include the parent, in case they're looking for the xmatter *folder*, and the folder
				//itself, in case they're looking for something inside it
				yield return xMatterInfo.PathToFolder;
				yield return Path.GetDirectoryName(xMatterInfo.PathToFolder);
			}

			//REVIEW: this one is just a big grab bag of all folders we find in their programdata, installed stuff. This could be insufficient.
			foreach (var searchPath in _userInstalledSearchPaths)
			{
				yield return searchPath;
			}

			//Book-specific paths (added by AddPath()) are last because we want people to get the latest stylesheet,
			//not just the version the had when they made the book.
			//This may seem counter-intuitive. One scenario, which has played out many times, is that the
			//book has been started, and the customer requests some change to the stylesheet, which we deliver just by having them
			//double-click a bloompack.
			//Another scenario is that a new version of Bloom comes out that expects/needs the newer stylesheet
			foreach (var searchPath in _bookSpecificSearchPaths)
			{
				yield return searchPath;
			}

			if (_collectionSettings.FolderPath != null) // typically only in tests, but makes more robust anyway
				yield return _collectionSettings.FolderPath;
		}

		public override IFileLocator CloneAndCustomize(IEnumerable<string> addedSearchPaths)
		{
			var locator= new BloomFileLocator(_collectionSettings, _xMatterPackFinder,_factorySearchPaths, _userInstalledSearchPaths, _afterXMatterSearchPaths);
			foreach (var path in _bookSpecificSearchPaths)
			{
				locator.AddPath(path);
			}
			foreach (var path in addedSearchPaths)
			{
				locator.AddPath(path);
			}
			return locator;
		}

		public static string GetBrowserFile(bool optional, params string[] parts)
		{
			parts[0] = Path.Combine(BrowserRoot,parts[0]);
			return FileLocator.GetFileDistributedWithApplication(optional, parts);
		}

		public static string GetBrowserDirectory(params string[] parts)
		{
			parts[0] = Path.Combine(BrowserRoot, parts[0]);
			return FileLocator.GetDirectoryDistributedWithApplication(false, parts);
		}
		public static string GetInstalledXMatterDirectory()
		{
			return BloomFileLocator.GetBrowserDirectory("templates","xMatter");
		}

		public static string FactoryTemplateBookDirectory
		{
			get { return BloomFileLocator.GetBrowserDirectory("templates", "template books"); }
		}

		public static string SampleShellsDirectory
		{
			get { return GetBrowserDirectory("templates", "Sample Shells"); }
		}

		/// <summary>
		/// contains both the template books and the sample shells
		/// </summary>
		public static string FactoryCollectionsDirectory {
			get
			{
				return GetBrowserDirectory("templates");
			}
		}


		public static string GetFactoryBookTemplateDirectory(string bookName)
		{
			return Path.Combine(FactoryTemplateBookDirectory, bookName);
		}

		/// <summary>
		/// Get the pathname of the directory containing the executing assembly (Bloom.exe).
		/// </summary>
		public static string GetCodeBaseFolder()
		{
			var file = Assembly.GetExecutingAssembly().CodeBase.Replace("file://", string.Empty);
			if (SIL.PlatformUtilities.Platform.IsWindows)
				file = file.TrimStart('/');
			return Path.GetDirectoryName(file);
		}

		/// <summary>
		/// Check whether this file was installed with Bloom (and likely to be read-only on Linux or for allUsers install).
		/// </summary>
		public static bool IsInstalledFileOrDirectory(string filepath)
		{
			var folder = GetCodeBaseFolder();
		    var slash = Path.DirectorySeparatorChar;
			if (folder.EndsWith($"{slash}output{slash}Debug"))
				folder = folder.Replace($"{slash}Debug", string.Empty);   // files now copied to output/browser for access

			return filepath.Contains(folder);
		}

		/// <summary>
		/// Map from human readable filenames to random filenames to discourage having someone
		/// open and inadvertantly lock a translated HTML file.  We save the names during each
		/// run of Bloom to minimize file storage and computation time while ensuring that each
		/// update to Bloom will always cause translated HTML files to be regenerated using the
		/// very latest xliff and English HTML files.
		/// </summary>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-5189.
		/// </remarks>
		private static Dictionary<string, string> _mapLocalizedFileToTempFilename = new Dictionary<string, string>();

		/// <summary>
		/// This can be used to find the best localized file when there is only one file with the given name,
		/// and the file is part of the files distributed with Bloom (i.e., not something in a downloaded template).
		/// </summary>
		public static string GetBestLocalizableFileDistributedWithApplication(bool existenceOfEnglishVersionIsOptional, params string[] partsOfEnglishFilePath)
		{
			// at this time, FileLocator does not have a way for the app to actually tell it where to find things distributed
			// with the application...
			var englishPath = FileLocator.GetFileDistributedWithApplication(true, partsOfEnglishFilePath);

			// ... so if it doesn't find it, we have to keep looking
			if (string.IsNullOrWhiteSpace(englishPath))
			{
				//this one will throw if we still can't find it and existenceOfEnglishVersionIsOptional is false
				englishPath = BloomFileLocator.GetBrowserFile(existenceOfEnglishVersionIsOptional, partsOfEnglishFilePath);
			}

			if (!RobustFile.Exists(englishPath))
			{
				return englishPath; // just return whatever the original GetFileDistributedWithApplication gave. "", null, whatever it is.
			}
			return BloomFileLocator.GetBestLocalizedFile(englishPath);
		}


		/// <summary>
		/// If there is an existing file in the desired language, return its path.
		/// Otherwise, try to create it if possible and return the path of the newly created file.
		/// Otherwise, returns the English path.
		/// </summary>
		public static string GetBestLocalizedFile(string pathToEnglishFile)
		{
			var keyPathInDesiredLanguage = GetLocalizedFilePathKey(pathToEnglishFile);
			if (keyPathInDesiredLanguage == pathToEnglishFile)
				return pathToEnglishFile;

			string pathInDesiredLanguage;
			if (_mapLocalizedFileToTempFilename.TryGetValue(keyPathInDesiredLanguage, out pathInDesiredLanguage) &&
				RobustFile.Exists(pathInDesiredLanguage))
			{
				return pathInDesiredLanguage;
			}
			if (pathToEnglishFile.ToLowerInvariant().EndsWith("-en.txt"))
			{
				// The xliff based translation process does not (yet?) handle plain .txt files.
				// Those have been translated and distributed with the program.
				pathInDesiredLanguage = pathToEnglishFile.Replace("-en.", "-" + LocalizationManager.UILanguageId + ".");
				return RobustFile.Exists(pathInDesiredLanguage) ? pathInDesiredLanguage : pathToEnglishFile;
			}
			else
			{
				Debug.Assert(pathToEnglishFile.ToLowerInvariant().EndsWith(".htm") || pathToEnglishFile.ToLowerInvariant().EndsWith(".html"));
				if (!pathToEnglishFile.ToLowerInvariant().EndsWith(".htm") && !pathToEnglishFile.ToLowerInvariant().EndsWith(".html"))
					return pathToEnglishFile;
				return CreateLocalizedHtmlFile(pathToEnglishFile, keyPathInDesiredLanguage);
			}
		}

		/// <summary>
		/// Localized files are created and stored in the Bloom/localizations folder of the temp folder.
		/// The filename keys are tagged with the target language code.  The template book ReadMe-xx files are
		/// stored in subfolders with the same name as the template folder to keep them unambiguous.  The
		/// names generated here are used as keys into a map.  The directory structure of the key is retained,
		/// but the filename proper is replaced by a random name for storage on the disk.
		/// </summary>
		/// <remarks>
		/// Storing the generated localized files in the temp directory area, and deleting them each time the
		/// program starts, ensures that new translations or changes to the English HTML are not lost due to
		/// an existing translated file.
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-5189.
		/// </remarks>
		private static string GetLocalizedFilePathKey(string pathToEnglishFile)
		{
			if (LocalizationManager.UILanguageId == "en" ||
				LocalizationManager.UILanguageId.StartsWith("en-"))
			{
				return pathToEnglishFile;	// don't create a copy of the original English file
			}
			var cacheDir = GetLocalizedFileCacheDirectory();
			var bareFilename = Path.GetFileNameWithoutExtension(pathToEnglishFile);
			if (bareFilename == "ReadMe-en")
			{
				// Every book template ReadMe file has the same name, so we use the directory structure
				// to disambiguate the generated files.
				var folder = Path.GetFileName(Path.GetDirectoryName(pathToEnglishFile));
				cacheDir = Path.Combine(cacheDir, folder);
			}
			return Path.Combine(cacheDir, Path.GetFileName(pathToEnglishFile).Replace("-en.", "-" + LocalizationManager.UILanguageId + "."));
		}

		/// <summary>
		/// Get the directory path where localized HTML files are created.
		/// </summary>
		public static string GetLocalizedFileCacheDirectory()
		{
			return Path.Combine(Path.GetTempPath(), "Bloom", "localizations");
		}

		/// <summary>
		/// The localized xliff files are stored under DistFiles/localization in two different ways.  The general
		/// location is in a subfolder with the language code as its name, and the .xlf file named without any
		/// embedded language code.  For the template books which all have ReadMe-xx files of the same name, the
		/// xliff file is stored in a subfolder with the same name as the template book's folder, and the language
		/// code is embedded in the file name as expected (ReadMe-en.xlf, ReadMe-fr.xlf, etc.).
		/// </summary>
		/// <remarks>
		/// The Windows installer omits the DistFiles level of the directory tree while the Linux package includes
		/// it.  See https://issues.bloomlibrary.org/youtrack/issue/BL-5190.
		/// </remarks>
		private static string GetLocalizedXliffPath(string pathToEnglishFile)
		{
			var baseDir = SIL.IO.FileLocator.DirectoryOfApplicationOrSolution;
			var xliffBareFile = Path.GetFileNameWithoutExtension(pathToEnglishFile);
			var folder = Path.GetFileName(Path.GetDirectoryName(pathToEnglishFile));
			var xliffDir = Path.Combine(baseDir, "DistFiles", "localization");	// Linux runtime, developers
			var path = GetLocalizedXliffPath(xliffDir, folder, xliffBareFile);
			if (RobustFile.Exists(path))
				return path;
			var xliffDir1 = Path.Combine(baseDir, "localization");	// Windows runtime
			return GetLocalizedXliffPath(xliffDir1, folder, xliffBareFile);
		}

		private static string GetLocalizedXliffPath(string xliffDir, string subDir, string xliffBareFile)
		{
			Debug.Assert(xliffBareFile.EndsWith("-en"));
			if (xliffBareFile.EndsWith("-en"))
				xliffBareFile = xliffBareFile.Substring(0, xliffBareFile.Length - 3);
			var langId = LocalizationManager.UILanguageId;
			var xliffPath = Path.Combine(xliffDir, langId, xliffBareFile + ".xlf");
			if (RobustFile.Exists(xliffPath))
				return xliffPath;
			xliffPath = Path.Combine(xliffDir, subDir, xliffBareFile + "-" + langId + ".xlf");
			if (RobustFile.Exists(xliffPath))
				return xliffPath;
			// We may have an xliff file identified by only language when langId includes country, for example "es" vs "es-ES".
			// If that happens, try finding the file with only the language code without the country code.
			if (langId.Contains('-'))
			{
				langId = langId.Substring(0, langId.IndexOf('-'));
				xliffPath = Path.Combine(xliffDir, langId, xliffBareFile + ".xlf");
				if (RobustFile.Exists(xliffPath))
					return xliffPath;
				xliffPath = Path.Combine(xliffDir, subDir, xliffBareFile + "-" + langId + ".xlf");
			}
			return xliffPath;
		}

		/// <summary>
		/// Use HtmlXliff to translate the English HTML file into another language using the translated xliff file
		/// corresponding to this HTML file.  If the translation occurs, a random filename is used in the given
		/// directory under the standard temp directory.
		/// </summary>
		private static string CreateLocalizedHtmlFile(string pathToEnglishFile, string keyPathInDesiredLanguage)
		{
			var xliffPath = GetLocalizedXliffPath(pathToEnglishFile);
			if (!RobustFile.Exists(xliffPath))
				return pathToEnglishFile;
			HtmlXliff injector = HtmlXliff.Load(pathToEnglishFile);
			var hdoc = injector.InjectTranslations(xliffPath, true);
			// Ensure the directory exists.
			var cacheDir = GetLocalizedFileCacheDirectory();
			Directory.CreateDirectory(cacheDir);	// just in case it hasn't yet been created
			string pathInDesiredLanguage = Path.Combine(cacheDir, Path.GetRandomFileName() + ".htm");
			hdoc.Save(pathInDesiredLanguage, Encoding.UTF8);
			_mapLocalizedFileToTempFilename[keyPathInDesiredLanguage] = pathInDesiredLanguage;
			return pathInDesiredLanguage;
		}

		/// <summary>
		/// Gets a file in the specified branding folder
		/// </summary>
		/// <param name="brandingNameOrFolderPath"> Normally, the branding is just a name, which we look up in the official branding folder
		//  but unit tests can instead provide a path to the folder.
		/// </param>
		/// <param name="fileName"></param>
		/// <returns></returns>
		public static string GetOptionalBrandingFile(string brandingNameOrFolderPath, string fileName)
		{
			if(Path.IsPathRooted(brandingNameOrFolderPath)) //if it looks like a path
			{
				var path = Path.Combine(brandingNameOrFolderPath, fileName);
				if(RobustFile.Exists(path))
					return path;
				return null;
			}
			return BloomFileLocator.GetFileDistributedWithApplication(true, "branding", brandingNameOrFolderPath, fileName);
		}
	}
}
