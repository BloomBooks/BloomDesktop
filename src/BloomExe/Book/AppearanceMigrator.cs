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
    /// This class handles cases where we know how to migrate a 5.6 or before custom CSS file to a 5.7 theme.
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
                if (!File.Exists(path))
                    continue; // or throw??
                var content = RobustFile.ReadAllText(path);
                var match = Regex.Match(content, " with checksum ([a-f0-9, ]*)");
                if (match.Success)
                {
                    var checksums = match.Groups[1].Value.Split(',');
                    foreach (var checksum in checksums)
                    {
                        _checksumsToSubstitute[checksum] = path;
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
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(css));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
