using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.Collection;
using Bloom.ToPalaso;
using L10NSharp;
using SIL.Xml;


namespace Bloom.Book
{
    // LicenseChecker implements a strategy for keeping users from breaking the rules about books for which we
    // are only licensed to publish certain languages. A spreadsheet is published at
    // https://docs.google.com/spreadsheets/d/1HL8gO2BEqQ38gX-VkyVaAI-nPfhUxMlMQz7z4047TFc/edit#gid=0
    // and available in json at "https://content-licenses.bloomlibrary.org" which maps book IDs
    // and groups of book IDs to permitted languages. If a book contains a key (stored in
    // <meta name="bloom-licensed-content-id" content="key"></meta>), we can only publish
    // languages that we can confirm are OK from the spreadsheet.
    // For offline checking we cache the keys whenever we read it.
    public class LicenseChecker
    {
        private static string _offlineFolderPath = ProjectContext.GetBloomAppDataFolder(); // normally stays here except in unit tests
        private static bool _allowInternetAccess = true;
        public IEnumerable<string> GetProblemLanguages(string[] inputLangs, string key, out bool didCheck)
        {
            string permissionsJson;
            if (_allowInternetAccess)
            {
                try
                {
                    permissionsJson = new WebClient().DownloadString("https://content-licenses.bloomlibrary.org");
                    if (!string.IsNullOrEmpty(_offlineFolderPath))
                    {
                        try
                        {
                            Directory.CreateDirectory(_offlineFolderPath);
                            LicenseChecker.WriteObfuscatedFile(getCacheFile(), permissionsJson);
                        }
                        catch (IOException e)
                        {
                            // just ignore if we can't cache
                        }
                    }
                }
                catch (WebException w)
                {
                    if (!TryGetOfflineCache(out permissionsJson))
                    {
                        didCheck = false;
                        return inputLangs;
                    }
                }
            }
            else
            {
                if (!TryGetOfflineCache(out permissionsJson))
                {
                    didCheck = false;
                    return inputLangs;
                }
            }

            var permissions = DynamicJson.Parse(permissionsJson);
            var values = permissions.values;
            var allowed = new HashSet<string>();
            foreach (var val in values)
            {
                string contentId = val[0];
                string languageCode = val[1];
                if (MatchingKey(contentId.Trim(), key))
                {
                    allowed.Add(languageCode.Trim());
                }
            }

            didCheck = true;
            return inputLangs.Where(c => !allowed.Contains(c));
        }

        private bool TryGetOfflineCache(out string permissionsJson)
        {
            permissionsJson = null;
            if (String.IsNullOrEmpty(_offlineFolderPath))
            {
                return false;
            }

            var path = getCacheFile();
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                permissionsJson = ReadObfuscatedFile(path);
            }
            catch (IOException ex)
            {
                return false;
            }

            return true;
        }

        // A key like "kingstone.superbible.ruth" mathces a rule like ["kingstone.superbible.ruth", "ru"]
        // but also one like ["kingstone.superbible.*", "ru"].
        private bool MatchingKey(string contentId, string key)
        {
            if (contentId == key)
                return true;
            if (!contentId.EndsWith("*"))
                return false;
            return key.StartsWith(contentId.Substring(0, contentId.Length - 1));
        }

        public static void SetOfflineFolder(string folderPath)
        {
            _offlineFolderPath = folderPath;
        }

        // Currently always allowed except in testing.
        public static void SetAllowInternetAccess(bool allow)
        {
            _allowInternetAccess = allow;
        }

        internal string getCacheFile()
        {
            return _offlineFolderPath + "/license.cache";
        }

        internal static void WriteObfuscatedFile(string path, string content)
        {
            File.WriteAllText(path, System.Convert.ToBase64String(Encoding.UTF8.GetBytes(content)));
        }

        internal static string ReadObfuscatedFile(string path)
        {
            return Encoding.UTF8.GetString(System.Convert.FromBase64String(File.ReadAllText(path)));
        }

        /// <summary>
        /// Checks whether it is OK to publish the specified book in the specified languages.
        /// Returns null if the book does not have meta name="bloom-licensed-content-id",
        /// or if we can download (or already have cached) the spreadsheet and can confirm
        /// that these languages are OK. Otherwise, it returns an error message:
        /// - "To publish this book, Bloom must check which languages the copyright owner has permitted, and Bloom cannot reach the server that has this information" or
        /// - "The copyright owner of this book has not licensed it for publishing in {forbidden languages}. Please publish it only in permitted languages."
        /// </summary>
        public string CheckBook(Book book, string[] languages)
        {
            return CheckBook(book.OurHtmlDom, languages, book);
        }

        // This overload is much more convenient for testing.
        public string CheckBook(HtmlDom dom, string[] languages, Book book = null)
        {
            var meta = dom.SelectSingleNode("//meta[@name='bloom-licensed-content-id' and @content]");
            if (meta == null)
                return null;
            bool didCheck;
            var problems = GetProblemLanguages(languages, meta.GetStringAttribute("content"), out didCheck);
            if (problems.Count() == 0)
                return null;
            if (!didCheck)
            {
                return LocalizationManager.GetString("PublishTab.Android.CantGetLicenseInfo",
                    "To publish this book, Bloom must check which languages the copyright owner has permitted, but Bloom is having trouble reaching the server that has this information.");
            }
            var template = LocalizationManager.GetString("PublishTab.Android.UnlicensedLanguages",
                    "The copyright holder of this book has not licensed it for publishing in {0}. You can deselect languages using the checkboxes on the right of this screen. Please contact the copyright holder to learn more about licensing.",
                    "{0} will be a language name or a list of them");
            // In real life we always have a book and can get nicer names. I put the fallback in for testing.
            var langs = string.Join(CultureInfo.CurrentCulture.TextInfo.ListSeparator + " ",
                problems.Select(x => book == null
                        ? WritingSystem.LookupIsoCode.GetLocalizedLanguageName(x, "")
                        : book.PrettyPrintLanguage(x)));
            return string.Format(template, langs);
        }
    }
}
