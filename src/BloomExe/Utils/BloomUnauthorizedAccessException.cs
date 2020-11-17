using SIL.IO;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;

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
		public BloomUnauthorizedAccessException(string path, UnauthorizedAccessException innerException)
			: base(GetMessage(path, innerException), innerException)
		{
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
