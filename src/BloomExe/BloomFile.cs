using System;
using System.IO;
using System.Text;

namespace Bloom
{
	public static class BloomFile
	{
		/// <summary>
		/// This is a drop-in replacement for File.ReadAllText that provides a more useful message 
		/// if an UnauthorizedAccessException in My Documents occurs on Windows.
		/// </summary>
		/// <param name="path">The file to open for reading</param>
		/// <param name="encoding">The encoding applied to the contents of the file</param>
		/// <returns></returns>
		public static string ReadAllText(string path, Encoding encoding = null)
		{
			try
			{
				return encoding == null ? File.ReadAllText(path) : File.ReadAllText(path, encoding);
			}
			catch (UnauthorizedAccessException)
			{
				// only applies to Windows
				if (!Palaso.PlatformUtilities.Platform.IsWindows) throw;

				// on Windows, only applies to My Documents
				var fullPath = Path.GetFullPath(path);
				var myDocuments = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
				if (!fullPath.StartsWith(myDocuments)) throw;

				throw new UnauthorizedAccessException(GetMyDocumentsUnauthorizedMessage());
			}
		}

		/// <summary>
		/// This is a drop-in replacement for File.WriteAllText that provides a more useful message 
		/// if an UnauthorizedAccessException in My Documents occurs on Windows.
		/// </summary>
		/// <param name="path">The file to open for reading</param>
		/// <param name="contents">The string to write to the file</param>
		/// <param name="encoding">The encoding applied to the contents of the file</param>
		public static void WriteAllText(string path, string contents, Encoding encoding = null)
		{
			try
			{
				if (encoding == null)
				{
					File.WriteAllText(path, contents);
				}
				else
				{
					File.WriteAllText(path, contents, encoding);
				}
			}
			catch (UnauthorizedAccessException)
			{
				// only applies to Windows
				if (!Palaso.PlatformUtilities.Platform.IsWindows) throw;

				// on Windows, only applies to My Documents
				var fullPath = Path.GetFullPath(path);
				var myDocuments = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
				if (!fullPath.StartsWith(myDocuments)) throw;

				throw new UnauthorizedAccessException(GetMyDocumentsUnauthorizedMessage());
			}
		}

		private static string GetMyDocumentsUnauthorizedMessage()
		{
			var msg = new StringBuilder();
			msg.Append("Your computer would not let bloom access a file in this book.");
			msg.Append(" If this problem persists after restarting your computer, you may have a problem with permissions on these files.");
			msg.AppendLine(" Find a technical help person and have them try this:");
			msg.AppendLine("• Right-click on My Documents and select Properties");
			msg.AppendLine("• Click the Security tab");
			msg.AppendLine("• Click the Advanced button");
			msg.AppendLine("• Select your name and click the Change Permissions button");
			msg.AppendLine("• Select your name again");
			msg.AppendLine("• Tick \"Replace all child permissions with inheritable permissions from this object\"");
			msg.AppendLine("• Click Apply");

			return L10NSharp.LocalizationManager.GetDynamicString("Bloom", "Errors.FilePermissions", msg.ToString());
		}
	}
}
