using System;
using System.IO;
using System.Text;

namespace Bloom
{
	public static class BloomFile
	{
		/// <summary>
		/// This is a drop-in replacement for File.ReadAllText that provides a more useful message 
		/// if an UnauthorizedAccessException occurs on Windows.
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
				if (!Palaso.PlatformUtilities.Platform.IsWindows) throw;

				var msg = new StringBuilder();
				msg.Append("Your computer would not let bloom access a file in this book.");
				msg.Append(
					" If this problem persists after restarting your computer, you may have a problem with permissions on these files.");
				msg.AppendLine(" Find a technical help person and have them try this:");
				msg.AppendLine("• Right-click on My Documents and select Properties");
				msg.AppendLine("• Click the Security tab");
				msg.AppendLine("• Click the Advanced button");
				msg.AppendLine("• Select your name and click the Change Permissions button");
				msg.AppendLine("• Select your name again");
				msg.AppendLine("• Tick \"Replace all child permissions with inheritable permissions from this object\"");
				msg.AppendLine("• Click Apply");

				var msgStr = L10NSharp.LocalizationManager.GetDynamicString("Bloom", "Errors.FilePermissions", msg.ToString());

				throw new UnauthorizedAccessException(msgStr);
			}
		}
	}
}
