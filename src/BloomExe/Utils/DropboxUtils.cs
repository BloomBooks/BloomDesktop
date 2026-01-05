using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SIL.IO;
using SIL.PlatformUtilities;

namespace Bloom.Utils
{
    /// <summary>
    /// Functions related to working with Dropbox.
    /// </summary>
    public class DropboxUtils
    {
        private static readonly object _accessCheckLock = new object();
        private static bool? _lastAccessResult = null;
        private static DateTime _lastAccessCheck = DateTime.MinValue;
        private static readonly TimeSpan _cacheTimeout = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Location(s) for the Dropbox info.json file based on information in
        /// https://help.dropbox.com/installs-integrations/desktop/locate-dropbox-folder,
        /// though it isn't precisely accurate.
        /// </summary>
        private static string[] s_jsonPaths = Platform.IsLinux
            ? new[] { "%HOME%/.dropbox/info.json" }
            : new[] { "%APPDATA%\\Dropbox\\info.json", "%LOCALAPPDATA%\\Dropbox\\info.json" };

        /// <summary>
        /// It's a bit of a toss-up whether to use a Regex or a JSON parser to extract data from
        /// the info.json file. The documentation at the URL above has several mistakes. There
        /// are at least a couple of paths to navigate through the JSON to possible dropbox folder
        /// paths (personal.path and business.path) and one of these is not documented for Linux;
        /// could there be others they have not mentioned?  Then we'd also have to handle the file
        /// possibly not parsing. This Regex simply looks for any occurrence of "path": "wherever".
        /// </summary>
        private static string s_pathMatchPattern = "\"path\":\\s*\"(.*?)\"";

        /// <summary>
        /// Is Dropbox up and running? This may not be 100% reliable...could the process be
        /// called something else when a localized version is running? Might one Dropbox process
        /// be running but not the one we really need? But it's the best we can find so far.
        /// </summary>
        public static bool IsDropboxProcessRunning()
        {
            if (Platform.IsWindows)
            {
                // The primary Dropbox process is called Dropbox.exe on Windows, but shows up in C# as "Dropbox".
                // There should be several copies of this process running, but we'll just check for at least one.
                // The Dropbox Service process is called DbxSvc.exe, but shows up in C# as "DbxSvc".  It would
                // seem to be necessary for Dropbox to be running, so we'll check for it as well.
                // There is also a DropboxUpdate.exe process, but we don't care about it.
                return System.Diagnostics.Process.GetProcessesByName("Dropbox").Any()
                    && System.Diagnostics.Process.GetProcessesByName("DbxSvc").Any();
            }
            else if (Platform.IsFlatpak)
            {
                // Flatpak can't see external processes, so we'll have to check the log file.
                return IsDropboxLogCurrent();
            }
            else
            {
                return System
                    .Diagnostics.Process.GetProcesses()
                    .Any(p => p.ProcessName.Contains("dropbox"));
            }
        }

        /// <summary>
        /// Check whether an appropriate log file exists and has been written to in the last minute.
        /// This works on Linux in the flatpak environment where process checking won't work.  (It
        /// might even be as reliable as process checking on Linux in general.  See BL-12244.)
        /// </summary>
        public static bool IsDropboxLogCurrent()
        {
            var maxDelta = new TimeSpan(0, 1, 0);
            foreach (var jsonPath in s_jsonPaths)
            {
                var expandedPath = Environment.ExpandEnvironmentVariables(jsonPath);
                var logDir = Path.Combine(Path.GetDirectoryName(expandedPath), "logs", "1");
                if (Directory.Exists(logDir))
                {
                    foreach (var file in Directory.EnumerateFiles(logDir, "1-*.tmp"))
                    {
                        var writeTime = RobustFile.GetLastWriteTime(file);
                        if (DateTime.Now - writeTime < maxDelta)
                            return true;
                    }
                }
            }
            return false;
        }

        public static bool IsPathInDropboxFolder(string path)
        {
            try
            {
                var searchPath = Platform.IsLinux ? path : path.ToLowerInvariant();
                foreach (var jsonPath in s_jsonPaths)
                {
                    var fixedPath = Environment.ExpandEnvironmentVariables(jsonPath);
                    if (RobustFile.Exists(fixedPath))
                    {
                        var json = RobustFile.ReadAllText(fixedPath, Encoding.UTF8);
                        // It's a bit of a toss-up whether to use a Regex or a JSON parser here. The documentation at the
                        // URL above has several mistakes. There are at least a couple of paths to navigate through the
                        // JSON to possible dropbox folder paths (personal.path and business.path) and one of these is not
                        // documented for Linux; could there be others they have not mentioned? Then we'd also have to handle
                        // the file possibly not parsing. This Regex simply looks for any occurrence of "path": "wherever".
                        foreach (
                            var match in new Regex(s_pathMatchPattern).Matches(json).Cast<Match>()
                        )
                        {
                            var dropboxRoot = match.Groups[1].Value;
                            if (!Platform.IsLinux)
                            {
                                dropboxRoot = dropboxRoot.ToLowerInvariant().Replace(@"\\", @"\");
                            }

                            if (searchPath.StartsWith(dropboxRoot))
                                return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // If something goes wrong trying to figure out whether it's a Dropbox folder,
                // just assume it isn't. Nothing crucial currently depends on knowing this, just
                // some more helpful error reporting if there's a problem.
                NonFatalProblem.ReportSentryOnly(ex);
            }

            return false;
        }

        /// <summary>
        /// Can we access Dropbox.com? This method includes caching and thread safety
        /// to handle multiple concurrent calls efficiently. I'm not sure we ever call
        /// this off the UI thread, but at one point we could generate several calls to
        /// it in a row in Idle event handlers, and sometimes one of the multiple
        /// requests would fail (BL-15624), though I was not able to determine how or why.
        /// This check is mainly to let us give nicer error messages if something is
        /// about to fail because DropBox is not accessible, so it's not a complete
        /// disaster if our information is slightly stale. Even without the caching,
        /// DropBox status could possibly change between calling this and whatever we
        /// want to do that wants it to be up. So I think it's better (and less likely
        /// to give false negatives, which are much worse because our TC UI shuts down)
        /// to limit how often we check.
        /// </summary>
        public static bool CanAccessDropbox()
        {
            lock (_accessCheckLock)
            {
                // Return cached result if still valid
                if (_lastAccessResult.HasValue && DateTime.Now - _lastAccessCheck < _cacheTimeout)
                {
                    return _lastAccessResult.Value;
                }

                // Perform the actual check
                _lastAccessResult = CheckDropboxAccessInternal();
                _lastAccessCheck = DateTime.Now;
                return _lastAccessResult.Value;
            }
        }

        private static bool CheckDropboxAccessInternal()
        {
            try
            {
                var request = (System.Net.HttpWebRequest)
                    System.Net.WebRequest.Create("https://www.dropbox.com/");
                request.Method = "HEAD";
                request.Timeout = 5000; // 5 seconds
                request.AllowAutoRedirect = true;

                using (var response = (System.Net.HttpWebResponse)request.GetResponse())
                {
                    var code = (int)response.StatusCode;
                    return code >= 200 && code < 400;
                }
            }
            catch (Exception ex)
            {
                // Some problems, like being completely offline, produce exceptions
                // rather than a nice failure.
                Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(ex);
                return false;
            }
        }

        public static string GetDropboxFolderPath()
        {
            try
            {
                // Prefer a "business" dropbox to a "personal" dropbox if one exists.
                var pathPrefixes = new[] { "\"business\":\\s*{[^{}]*", "\"personal\":\\s*{[^{}]*" };
                foreach (var jsonPath in s_jsonPaths)
                {
                    var fixedPath = Environment.ExpandEnvironmentVariables(jsonPath);
                    if (RobustFile.Exists(fixedPath))
                    {
                        var json = RobustFile.ReadAllText(fixedPath, Encoding.UTF8);
                        foreach (var prefix in pathPrefixes)
                        {
                            var pathMatch = prefix + s_pathMatchPattern;
                            foreach (var match in new Regex(pathMatch).Matches(json).Cast<Match>())
                            {
                                var dropboxRoot = match.Groups[1].Value;
                                if (!Platform.IsLinux)
                                    dropboxRoot = dropboxRoot.Replace(@"\\", @"\");
                                if (System.IO.Directory.Exists(dropboxRoot))
                                    return dropboxRoot;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                NonFatalProblem.ReportSentryOnly(ex);
            }
            return null;
        }
    }
}
