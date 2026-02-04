using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using SIL.Reporting;

namespace Bloom.Utils
{
    internal class PathTooLongException : System.IO.PathTooLongException
    {
        public string Path;
        public string AdditionalInfo;

        public PathTooLongException(string path, string additionaInfo = "")
        {
            Path = path;
            AdditionalInfo = additionaInfo;
        }

        private string AdditionalInfoMessage =>
            string.IsNullOrWhiteSpace(AdditionalInfo) ? "" : $" Additional Info: {AdditionalInfo}";

        public override string Message =>
            $"{base.Message} Path was '{Path}.{AdditionalInfoMessage}";
    }

    internal static class LongPathAware
    {
        const int kmaxPath = 255; // not 256 becuase that includes the C++ null terminator

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern uint GetLongPathName(
            [MarshalAs(UnmanagedType.LPTStr)] string lpszShortPath,
            [MarshalAs(UnmanagedType.LPTStr)] StringBuilder lpszLongPath,
            [MarshalAs(UnmanagedType.U4)] int cchBuffer
        );

        /// <summary>
        /// Converts a short path (8.3) to a long path. We can be given these kinds of paths by
        /// Windows File Explorer when the user double-clicks on a .bloomCollection that has
        /// a really long path.
        /// </summary>
        /// <param name="path">A path that may or may not be an 8.3-style path.</param>
        /// <returns>The long path.  Null or empty if the input is null or empty.</returns>
        internal static string GetLongPath(string path)
        {
            if (String.IsNullOrEmpty(path))
            {
                return path;
            }
            if (SIL.PlatformUtilities.Platform.IsLinux)
            {
                return path;
            }
            if (!GetIsPossiblyShortenedPath(path))
            {
                return path;
            }

            StringBuilder builder = new StringBuilder(2000); // randomly chosen max
            var result = Convert.ToInt32(GetLongPathName(path, builder, builder.Capacity));
            if (result == 0)
            {
                // The original code threw a FileNotFoundException here.
                // But there are cases where we call this and don't expect the file to exist yet,
                // such as validating the destination path of a copy is not too long.
                // If the given path doesn't exist, it certainly isn't a short path which needs
                // to be converted to a long path, and the best we can do here is simply return
                // the path we were given. And in the case of a file we are about to create,
                // returning the given path is always the right thing to do.
                // See BL-12986.
                return path;
            }
            if (result >= builder.Capacity)
            {
                throw new ApplicationException("GetLongPath() exceeded capacity.");
            }

            return builder.ToString(0, result);
        }

        /// <summary>
        /// This doesn't do any fancy regex, so it could give false positives. Which is fine, becuase it's just a time saver.
        /// </summary>
        /// <param name="path"></param>
        /// <returns>True if it is worth asking the OS to expand the path in case it is an 8.3-style one.</returns>
        internal static bool GetIsPossiblyShortenedPath(string path)
        {
            if (path == null)
                return false;
            if (SIL.PlatformUtilities.Platform.IsLinux)
                return false;
            return path.Contains("~");
        }

        internal static bool GetExceedsMaxPath(string path)
        {
            if (path == null)
                return false;
            if (GetIsPossiblyShortenedPath(path))
            {
                return GetLongPath(path).Length > kmaxPath;
            }
            else
                return path.Length > kmaxPath;
        }

        internal static void ThrowIfExceedsMaxPath(string path)
        {
            if (GetExceedsMaxPath(path))
            {
                throw new PathTooLongException(path);
            }
        }

        internal static bool FileExistsThrowIfTooLong(string path)
        {
            ThrowIfExceedsMaxPath(path);
            return SIL.IO.RobustFile.Exists(path);
        }

        private static string GetGenericPathTooLongMessage()
        {
            return L10NSharp.LocalizationManager.GetString(
                "Errors.PathTooLong2",
                "A file Bloom was working with had a path that was too long. You may need to give it a shorter name, or give your collection a shorter name, or move your collection closer to the root of your hard drive."
            );
        }

        public static bool ShouldConvertToPathTooLongException(Exception exception, out string path)
        {
            path = null;
            if (exception == null || exception is System.IO.PathTooLongException)
                return false;
            // There may be other exceptions that we should convert, but this is what we've seen. (BL-15304)
            if (exception is System.IO.DirectoryNotFoundException)
            {
                path = Regex.Match(exception.Message, "^.*'(.*)'\\.$").Groups[1].Value;
                if (path?.Length >= 260)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The idea here is to show a helpful notice, with the option to click "REPORT" if they need help.
        /// </summary>
        /// <param name="e">either a raw PathTooLongException or our own enhanced subclass</param>
        internal static void ReportLongPath(System.IO.PathTooLongException e)
        {
            // If we have our own subclass, it will know the path of the offending file and maybe more helpful info.
            if (e is Bloom.Utils.PathTooLongException)
            {
                var x = (Bloom.Utils.PathTooLongException)e;
                var path = x.Path;
                try
                {
                    // It might be a short (8.3) path, so try to get the long version for the report.
                    path = GetLongPath(path);
                }
                catch
                {
                    // if we can't get the long path, just use what we had
                }
                ErrorReport.NotifyUserOfProblem(
                    x,
                    $"{GetGenericPathTooLongMessage()} <br> <span style='font-size:7pt'>Path was '{path}'. {x.AdditionalInfo}</span>"
                );
            }
            else
            {
                ErrorReport.NotifyUserOfProblem(e, $"{GetGenericPathTooLongMessage()}");
            }
        }

        internal static void ReportLongPath(string path)
        {
            ErrorReport.NotifyUserOfProblem(
                $"{GetGenericPathTooLongMessage()} <br> <span style='font-size:7pt'>Path was '{path}'.</span>"
            );
        }

        /* This function is NOT IN USE because this was for BloomServer but I ran screaming at the complexity in there and
         * so it will have to wait for another day.
         * internal static IEnumerable<string> EnumerateDirectoryFilesThrowIfTooLong(string dir, string fileName, SearchOption searchOption)
            {
                // It would take some extra effort to catch a path that became too long only in a sub directoryy
                // and we're not doing that yet. But we can at least test the top directory...
                Utils.LongPathAware.ThrowIfExceedsMaxPath(Path.Combine(dir, fileName));
                try
                {
                    return Directory.EnumerateFiles(dir, fileName, searchOption //  here filename is the searchPattern
    }
            catch (Exception ex)
            {
                // ...and then if it still fails, we can guess that the problem was length
                if (dir.Length + fileName.Length > 220)
                {
                    throw new Utils.PathTooLongException(fileName, "It is not certain that this was too long. We were doing Directory.EnumerateFiles in this directory and its sub directories: " + dir);
                }
                throw ex;
            }
        }*/
    }
}
