using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Bloom.Book;
using Bloom.Collection;
using L10NSharp;
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

		public static string GetBrowserFile(params string[] parts)
		{
			parts[0] = Path.Combine(BrowserRoot,parts[0]);
			return FileLocator.GetFileDistributedWithApplication(false, parts);
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
		/// Check whether this file was installed with Bloom (and likely to be read-only on Linux or for allUsers install).
		/// </summary>
		public static bool IsInstalledFileOrDirectory(string filepath)
		{
			var file = Assembly.GetExecutingAssembly().CodeBase.Replace("file://", string.Empty);
			if (SIL.PlatformUtilities.Platform.IsWindows)
				file = file.TrimStart('/');
			var folder = Path.GetDirectoryName(file);
			if (folder.EndsWith("/output/Debug"))
				folder = folder.Replace("/Debug", string.Empty);	// files now copied to output/browser for access
			return filepath.Contains(folder);
		}

		/// <summary>
		/// This can be used to find the best localized file when there is only one file with the given name, 
		/// and the file is part of the files distributed with Bloom (i.e., not something in a downloaded template).
		/// </summary>
		public static string GetBestLocalizableFileDistributedWithApplication(bool existenceOfEnglishVersionIsOptional, params string[] partsOfEnglishFilePath)
		{
			var englishPath = FileLocator.GetFileDistributedWithApplication(existenceOfEnglishVersionIsOptional, partsOfEnglishFilePath);
			if(!RobustFile.Exists(englishPath))
			{
				return englishPath; // just return whatever the original GetFileDistributedWithApplication gave. "", null, whatever it is.
			}
			return BloomFileLocator.GetBestLocalizedFile(englishPath);
		}


		/// <summary>
		/// If there is a file sitting next to the english one with the desired language, get that path.
		/// Otherwise, returns the English path.
		/// </summary>
		public static string GetBestLocalizedFile(string pathToEnglishFile)
		{
			var pathInDesiredLanguage = pathToEnglishFile.Replace("-en.", "-" + LocalizationManager.UILanguageId + ".");
			return RobustFile.Exists(pathInDesiredLanguage) ? pathInDesiredLanguage : pathToEnglishFile;
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
