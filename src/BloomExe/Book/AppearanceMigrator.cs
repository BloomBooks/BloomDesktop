using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using SIL.IO;
using SIL.Code;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace Bloom.Book
{
    /// <summary>
    /// This class handles cases where we know how to migrate a 5.6 or before custom CSS file to a 6.0 theme.
    /// </summary>
    public class AppearanceMigrator
    {
        private Dictionary<string, string> _checksumsToSubstitute = new();

        private static AppearanceMigrator _instance;

        /// <summary>
        /// We only ever need one of these, and often none at all.
        /// </summary>
        public static AppearanceMigrator Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new AppearanceMigrator();
                return _instance;
            }
        }

        public static string GetFolderContainingAppearanceMigrations()
        {
            return FileLocationUtilities.GetDirectoryDistributedWithApplication(
                Path.Combine(BloomFileLocator.BrowserRoot, "appearanceMigrations")
            );
        }

        public AppearanceMigrator()
        {
            foreach (
                var folderPath in Directory.EnumerateDirectories(
                    GetFolderContainingAppearanceMigrations()
                )
            )
            {
                var path = Path.Combine(folderPath, "appearance.json");
                if (!RobustFile.Exists(path))
                    continue; // or throw??
                var content = RobustFile.ReadAllText(path);
                var match = Regex.Match(content, " with checksum ([a-f0-9, ]*)");
                if (match.Success)
                {
                    var checksums = match.Groups[1].Value.Split(',');
                    foreach (var checksum in checksums)
                    {
                        _checksumsToSubstitute[checksum.Trim()] = path;
                    }
                }
            }
        }

        public string GetAppearanceThatSubstitutesForCustomCSS(string css)
        {
            var checksum = GetChecksum(css);
            Debug.WriteLine("checksum of " + checksum);
            if (_checksumsToSubstitute.TryGetValue(checksum, out string path))
            {
                return path;
            }
            return null;
        }

        public static string GetChecksum(string css)
        {
            // We want to ignore the boilerplate that is almost the same in all custom css files.
            // We also ignore any leading or trailing whitespace.
            // This is effectively the same as the code in bloom-utility-scripts that groups together
            // books with the same custom CSS.
            var boilerplate =
                "/\\* *Some books may need control over aspects of layout that cannot yet be adjusted(.|[\\r\\n])*?\\*/";
            var css2 = Regex.Replace(css, boilerplate, "").Trim();
            return Utils.MiscUtils.GetMd5HashOfString(css2);
        }
    }
}
