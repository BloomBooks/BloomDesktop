using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using SIL.IO;

namespace Bloom.Utils
{
    /// <summary>
    /// This class is basically the same as System.UnauthorizedAccessException,
    /// except it appends the file permissions to the exception message.
    /// </summary>
    [System.Serializable]
    public class BloomUnauthorizedAccessException : UnauthorizedAccessException
    {
        /// <summary>
        /// Creates a new BloomUnauthorizedAccessException wrapping System.UnauthorizedAccessException
        /// </summary>
        /// <param name="path">The path that was being accessed when the exception was thrown</param>
        /// <param name="innerException">The exception that was thrown, which will become the innerException of this exception.</param>
        public BloomUnauthorizedAccessException(
            string path,
            UnauthorizedAccessException innerException
        )
            : base(GetMessage(path, innerException), innerException)
        {
            CheckForHiddenFolder(path);
        }

        // If this is not null, it is the name of a hidden folder that might be responsible
        // for the problem (may be the folder originally passed as path, or the parent of the file
        // passed as path).
        public string HiddenFolder = null;

        private void CheckForHiddenFolder(string path)
        {
            try
            {
                // A possible cause of UnauthorizedAccessException even though the permissions look good is
                // that the containing folder is hidden. If we can determine that to be the case,
                // note the problem directory. (Conceivably a parent directory could be hidden; but
                // in my testing, that did not produce unauthorized access errors unless the immediate
                // parent directory was also hidden. So I don't think we need look higher up.
                string directoryPath = "";
                // if path is a path to a directory, we want a Directory Info for that directory.
                // if a path to a file, we want a Directory info for its containing directory.
                // (I haven't actually tested the case where path already points to a hidden directory.
                // Don't know of a scenario where that would be the case.)
                directoryPath = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if ((new DirectoryInfo(directoryPath).Attributes & FileAttributes.Hidden) != 0)
                {
                    HiddenFolder = directoryPath;
                }
            }
            catch (Exception)
            {
                // We might trigger other exceptions while trying to determine this, if it is a
                // different permission error. Just ignore them. This block is only about
                // making the error message a bit more helpful.
            }
        }

        /// <summary>
        /// Takes the message from the innerException and appends additional debugging info
        /// like the permissions of the file
        /// </summary>
        protected static string GetMessage(string path, UnauthorizedAccessException innerException)
        {
            try
            {
                string additionalMessage = GetPermissionString(path);

                string originalMessage = "";
                if (innerException != null)
                {
                    originalMessage = innerException.Message + "\n";
                }

                string combinedMessage = originalMessage + additionalMessage;

                return combinedMessage;
            }
            catch (Exception e)
            {
                // Some emergency fallback code to prevent this code from throwing an exception
                Debug.Fail("Unexpected exception: " + e.ToString());

                return innerException?.Message ?? $"Access to the path '{path}' is denied.";
            }
        }

        /// <summary>
        /// When all we have is an UnauthorizedAccessException, we can typically determine the path from the message.
        /// If not, we will just return the original exception.
        /// </summary>
        /// <param name="originalException"></param>
        /// <returns></returns>
        public static UnauthorizedAccessException CreateFromException(
            UnauthorizedAccessException originalException
        )
        {
            // Try to identify the blocked resource. Unfortunately UnauthorizeAccessException
            // does not provide a specific field that gives the path, but the message follows
            // a pretty typical pattern that contains it.
            var splitAtQuotes = originalException.Message.Split("'");
            var hidden = false;
            string directoryPath = "";
            if (splitAtQuotes.Length == 3)
            {
                // The bit inside the quotes is typically the path.
                // I don't know whether another delimiter might be used in some locales,
                // but this is only about improving the chances of a helpful message, so at
                // worst, we just keep the original exception.
                var path = splitAtQuotes[1];
                // This is a crude way to test whether we got something that looks like a file or
                // folder path. I think UnauthorizedAccessException will always give a rooted path
                // to the problem file or folder.
                try
                {
                    if (Path.IsPathRooted(path))
                        return new BloomUnauthorizedAccessException(path, originalException);
                }
                catch (Exception)
                {
                    // We might trigger other exceptions while trying to determine this, if it is a
                    // different permission error. Just ignore them. Getting a BloomUnauthorizedAccessException
                    // is only about making error messages a bit more helpful.
                }
            }
            return originalException;
        }

        /// <summary>
        /// Returns the file permission string in SDDL form.
        /// For help deciphering this string, check websites such as
        /// * https://docs.microsoft.com/en-us/windows/win32/secauthz/security-descriptor-string-format
        /// * https://docs.microsoft.com/en-us/windows/win32/secauthz/ace-strings
        /// * https://itconnect.uw.edu/wares/msinf/other-help/understanding-sddl-syntax/
        /// </summary>
        /// <param name="path">The string path that we were trying to access at the time the exception was thrown</param>
        /// <returns>A SDDL string (just the access control sections of it) represnting the access control rules of that file</returns>
        protected static string GetPermissionString(string path)
        {
            if (RobustFile.Exists(path))
            {
                string permissions = GetPermissionString(new FileInfo(path));
                return $"Permissions SDDL for file '{path}' is '{permissions}'.";
            }
            else
            {
                // Check its containing directory instead.
                // Process in a loop in case we get a path that contains folders which have not been created yet.
                string dirName = Path.GetDirectoryName(path);
                while (dirName != null && !Directory.Exists(dirName))
                {
                    dirName = Path.GetDirectoryName(dirName);
                }

                if (dirName != null)
                {
                    string permissions = GetPermissionString(new DirectoryInfo(dirName));
                    return $"Permissions SDDL for directory '{dirName}' is '{permissions}'.";
                }
                else
                {
                    return "";
                }
            }
        }

        protected static string GetPermissionString(FileInfo fileInfo)
        {
            var fileSecurity = fileInfo.GetAccessControl();
            var sddl = fileSecurity?.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            return sddl;
        }

        protected static string GetPermissionString(DirectoryInfo dirInfo)
        {
            var dirSecurity = dirInfo.GetAccessControl();
            var sddl = dirSecurity?.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            return sddl;
        }
    }
}
