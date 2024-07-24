using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using L10NSharp;
using SIL.Code;
using SIL.IO;
using SIL.Reporting;

namespace Bloom
{
    /// <summary>
    /// This class is a more complex version of LibPalaso's FileLocator class. It handles finding files in collections the user
    /// has installed, which we (sometimes) want to do. We decided to just copy some of the implementation of that class,
    /// rather than continuing to complicate it with hooks to allow Bloom to customize it.
    /// </summary>
    public class BloomFileLocator : IChangeableFileLocator
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
                return Directory.Exists(
                    Path.Combine(FileLocationUtilities.DirectoryOfApplicationOrSolution, "output")
                )
                    ? "output" + Path.DirectorySeparatorChar + "browser"
                    : "browser";
            }
        }

        public static BloomFileLocator sTheMostRecentBloomFileLocator;

        public BloomFileLocator(
            CollectionSettings collectionSettings,
            XMatterPackFinder xMatterPackFinder,
            IEnumerable<string> factorySearchPaths,
            IEnumerable<string> userInstalledSearchPaths,
            IEnumerable<string> afterXMatterSearchPaths = null
        )
        {
            if (afterXMatterSearchPaths == null)
            {
                afterXMatterSearchPaths = new string[] { };
            }
            _bookSpecificSearchPaths = new List<string>();
            _collectionSettings = collectionSettings;
            _xMatterPackFinder = xMatterPackFinder;
            _factorySearchPaths = factorySearchPaths;
            _userInstalledSearchPaths = userInstalledSearchPaths;
            _afterXMatterSearchPaths = afterXMatterSearchPaths;

            sTheMostRecentBloomFileLocator = this;
        }

        public void AddPath(string path)
        {
            _bookSpecificSearchPaths.Add(path);
            ClearLocateDirectoryCache();
        }

        public void RemovePath(string path)
        {
            _bookSpecificSearchPaths.Remove(path);
            ClearLocateDirectoryCache();
        }

        public string LocateFile(string fileName)
        {
            foreach (var path in GetSearchPaths(fileName))
            {
                var fullPath = Path.Combine(path, fileName);
                if (RobustFile.Exists(fullPath))
                    return fullPath;
            }
            return string.Empty;
        }

        /// <summary>
        /// These are used (as of 26 aug 2016) only by LibPalaso's FileLocationUtilities.LocateFile(). Not used by GetFileDistributedWIthApplication().
        /// </summary>
        /// <returns></returns>
        protected IEnumerable<string> GetSearchPaths(string fileName = null)
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

            foreach (var xMatterInfo in _xMatterPackFinder.CustomInstalled)
            {
                //this is a bit weird... we include the parent, in case they're looking for the xmatter *folder*, and the folder
                //itself, in case they're looking for something inside it
                yield return xMatterInfo.PathToFolder;
                yield return Path.GetDirectoryName(xMatterInfo.PathToFolder);
            }

            //REVIEW: this one is just a big grab bag of all folders we find in their programdata, installed stuff. This could be insufficient.
            if (ShouldSearchInstalledCollectionsForFile(fileName))
            {
                foreach (var searchPath in _userInstalledSearchPaths)
                {
                    yield return searchPath;
                }
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

        bool ShouldSearchInstalledCollectionsForFile(string fileName)
        {
            if (fileName == null)
                return true; // default if we weren't given the filename
            // We definitely don't want to get random versions of these files from some arbitrary installed collection.
            // Enhance: we're thinking of switching to some sort of "whitelist" approach, where only particular
            // groups of files (e.g., *-template.css) would be looked for in these locations. There is significant
            // danger of finding something irrelevant, and also, of hard-to-reproduce bugs that don't happen because
            // the developer has different installed collections than the reporter.
            return !BookStorage.CssFilesThatAreDynamicallyUpdated.Contains(fileName);
        }

        public IFileLocator CloneAndCustomize(IEnumerable<string> addedSearchPaths)
        {
            var locator = new BloomFileLocator(
                _collectionSettings,
                _xMatterPackFinder,
                _factorySearchPaths,
                _userInstalledSearchPaths,
                _afterXMatterSearchPaths
            );
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
            parts[0] = Path.Combine(BrowserRoot, parts[0]);
            return FileLocationUtilities.GetFileDistributedWithApplication(optional, parts);
        }

        public static string GetBrowserDirectory(params string[] parts)
        {
            parts[0] = Path.Combine(BrowserRoot, parts[0]);
            return FileLocationUtilities.GetDirectoryDistributedWithApplication(false, parts);
        }

        public static string GetOptionalBrowserDirectory(params string[] parts)
        {
            parts[0] = Path.Combine(BrowserRoot, parts[0]);
            return FileLocationUtilities.GetDirectoryDistributedWithApplication(true, parts);
        }

        public static string GetFactoryXMatterDirectory()
        {
            return BloomFileLocator.GetBrowserDirectory("templates", "xMatter");
        }

        public static string GetProjectSpecificInstalledXMatterDirectory()
        {
            return BloomFileLocator.GetBrowserDirectory("templates", "xMatter", "project-specific");
        }

        public static string GetCustomXMatterDirectory()
        {
            return BloomFileLocator.GetBrowserDirectory("templates", "customXMatter");
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
        public static string FactoryCollectionsDirectory
        {
            get { return GetBrowserDirectory("templates"); }
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

        public static string GetFolderContainingAppearanceThemeFiles()
        {
            return FileLocationUtilities.GetDirectoryDistributedWithApplication(
                Path.Combine(BloomFileLocator.BrowserRoot, "appearanceThemes")
            );
        }

        /// <summary>
        /// Check whether this file was installed with Bloom (and likely to be read-only on Linux or for allUsers install).
        /// </summary>
        public static bool IsInstalledFileOrDirectory(string filepath)
        {
            var folder = GetCodeBaseFolder();
            var slash = Path.DirectorySeparatorChar;
            if (folder.EndsWith($"{slash}output{slash}Debug"))
                folder = folder.Replace($"{slash}Debug", string.Empty); // files now copied to output/browser for access

            return filepath.Contains(folder);
        }

        /// <summary>
        /// This can be used to find the best localized file when there is only one file with the given name,
        /// and the file is part of the files distributed with Bloom (i.e., not something in a downloaded template).
        /// </summary>
        public static string GetBestLocalizableFileDistributedWithApplication(
            bool existenceOfEnglishVersionIsOptional,
            params string[] partsOfEnglishFilePath
        )
        {
            // at this time, FileLocator does not have a way for the app to actually tell it where to find things distributed
            // with the application...
            var englishPath = FileLocationUtilities.GetFileDistributedWithApplication(
                true,
                partsOfEnglishFilePath
            );

            // ... so if it doesn't find it, we have to keep looking
            if (string.IsNullOrWhiteSpace(englishPath))
            {
                //this one will throw if we still can't find it and existenceOfEnglishVersionIsOptional is false
                englishPath = BloomFileLocator.GetBrowserFile(
                    existenceOfEnglishVersionIsOptional,
                    partsOfEnglishFilePath
                );
            }

            if (!RobustFile.Exists(englishPath))
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
            var langId = LocalizationManager.UILanguageId;
            var pathInDesiredLanguage = pathToEnglishFile.Replace("-en.", "-" + langId + ".");
            if (RobustFile.Exists(pathInDesiredLanguage))
                return pathInDesiredLanguage;
            if (langId.Contains('-'))
            {
                // Ignore any country (or script) code to see if we can find a match to the generic language.
                langId = langId.Substring(0, langId.IndexOf('-'));
                pathInDesiredLanguage = pathToEnglishFile.Replace("-en.", "-" + langId + ".");
                if (RobustFile.Exists(pathInDesiredLanguage))
                    return pathInDesiredLanguage;
            }
            return pathToEnglishFile; // can't find a localized version, fall back to English
        }

        /// <summary>
        /// Gets a file in the specified branding folder
        /// </summary>
        /// <param name="brandingNameOrFolderPath"> Normally, the branding is just a name, which we look up in the official branding folder
        //  but unit tests can instead provide a path to the folder.
        /// </param>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public static string GetOptionalBrandingFile(
            string brandingNameOrFolderPath,
            string fileName
        )
        {
            if (Path.IsPathRooted(brandingNameOrFolderPath)) //if it looks like a path
            {
                var path = Path.Combine(brandingNameOrFolderPath, fileName);
                if (RobustFile.Exists(path))
                    return path;
                return null;
            }
            if (Path.IsPathRooted(fileName) && RobustFile.Exists(fileName)) // also just for unit tests
                return fileName;
            return BloomFileLocator.GetBrowserFile(
                true,
                "branding",
                brandingNameOrFolderPath,
                fileName
            );
        }

        public static string GetBrandingFolder(string fullBrandingName)
        {
            BrandingSettings.ParseBrandingKey(
                fullBrandingName,
                out var brandingFolderName,
                out var flavor,
                out var subUnitName
            );
            return BloomFileLocator.GetOptionalBrowserDirectory("branding", brandingFolderName);
        }

        public string GetBrandingFile(Boolean optional, string fileName)
        {
            return BloomFileLocator.GetBrowserFile(
                optional,
                "branding",
                _collectionSettings.GetBrandingFolderName(),
                fileName
            );
        }

        //-----------------------------------------------------
        // Copied mostly unchanged from libpalaso/FileLocationUtilities. Bloom may not actually need all of these.
        //----------------------------------------------------

        Dictionary<string, string> _mapDirectoryNameToPath = new Dictionary<string, string>();

        private void ClearLocateDirectoryCache()
        {
            _mapDirectoryNameToPath.Clear();
        }

        public string LocateDirectory(string directoryName)
        {
            if (_mapDirectoryNameToPath.TryGetValue(directoryName, out string result))
                return result;
            // Because GetSearchPaths is not being passed a file name, it won't search
            // in the user-installed directories, and the other places is searches shouldn't
            // change without restarting Bloom or calling things that clear the cache.
            foreach (var path in GetSearchPaths())
            {
                var fullPath = Path.Combine(path, directoryName);
                if (Directory.Exists(fullPath))
                {
                    _mapDirectoryNameToPath[directoryName] = fullPath;
                    return fullPath;
                }
            }
            _mapDirectoryNameToPath[directoryName] = string.Empty;
            return string.Empty;
        }

        public string LocateDirectory(string directoryName, string descriptionForErrorMessage)
        {
            var path = LocateDirectory(directoryName);
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                ErrorReport.NotifyUserOfProblem(
                    "{0} could not find the {1}.  It expected to find it in one of these locations: {2}",
                    UsageReporter.AppNameToUseInDialogs,
                    descriptionForErrorMessage,
                    string.Join(", ", GetSearchPaths())
                );
            }
            return path;
        }

        public string LocateDirectoryWithThrow(string directoryName)
        {
            var path = LocateDirectory(directoryName);
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                throw new ApplicationException(
                    String.Format(
                        "Could not find {0}.  It expected to find it in one of these locations: {1}",
                        directoryName,
                        string.Join(Environment.NewLine, GetSearchPaths())
                    )
                );
            }
            return path;
        }

        public string LocateFile(string fileName, string descriptionForErrorMessage)
        {
            var path = LocateFile(fileName);
            if (string.IsNullOrEmpty(path) || !RobustFile.Exists(path))
            {
                ErrorReport.NotifyUserOfProblem(
                    "{0} could not find the {1}.  It expected to find it in one of these locations: {2}",
                    UsageReporter.AppNameToUseInDialogs,
                    descriptionForErrorMessage,
                    string.Join(", ", GetSearchPaths(fileName))
                );
            }
            return path;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns>null if not found</returns>
        public string LocateOptionalFile(string fileName)
        {
            var path = LocateFile(fileName);
            if (string.IsNullOrEmpty(path) || !RobustFile.Exists(path))
            {
                return null;
            }
            return path;
        }

        /// <summary>
        /// Throws ApplicationException if not found.
        /// </summary>
        public string LocateFileWithThrow(string fileName)
        {
            var path = LocateFile(fileName);
            if (string.IsNullOrEmpty(path) || !RobustFile.Exists(path))
            {
                throw new ApplicationException(
                    "Could not find "
                        + fileName
                        + ". It expected to find it in one of these locations: "
                        + Environment.NewLine
                        + string.Join(Environment.NewLine, GetSearchPaths(fileName))
                );
            }
            return path;
        }
    }
}
