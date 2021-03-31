using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Gecko.WebIDL;
using SIL.IO;
using SIL.PlatformUtilities;

namespace Bloom.Utils
{
	/// <summary>
	/// Functions related to working with Dropbox.
	/// </summary>
	public class DropboxUtils
	{
		/// <summary>
		/// Is Dropbox up and running? This may not be 100% reliable...could the process be
		/// called something else when a localized version is running? Might one Dropbox process
		/// be running but not the one we really need? But it's the best we can find so far.
		/// Review: will this work on Linux?
		/// </summary>
		public static bool IsDropboxProcessRunning =>
			System.Diagnostics.Process.GetProcesses().Any(p => p.ProcessName.Contains("Dropbox"));

		/// <summary>
		/// Based on information in https://help.dropbox.com/installs-integrations/desktop/locate-dropbox-folder,
		/// though it isn't precisely accurate.
		/// </summary>
		public static bool IsPathInDropboxFolder(string path)
		{
			var jsonPaths = Platform.IsLinux
				? new[] {"~/.dropbox/info.json"}
				: new[] {"%APPDATA%\\Dropbox\\info.json", "%LOCALAPPDATA%\\Dropbox\\info.json"};
			var searchPath = Platform.IsLinux ? path : path.ToLowerInvariant();
			foreach (var jsonPath in jsonPaths)
			{
				var fixedPath = Platform.IsLinux ? jsonPath : Environment.ExpandEnvironmentVariables(jsonPath);
				if (RobustFile.Exists(fixedPath))
				{
					var json = RobustFile.ReadAllText(fixedPath, Encoding.UTF8);
					foreach (var match in new Regex("\\\"path\\\":\\s*\\\"(.*?)\\\"").Matches(json).Cast<Match>())
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

			return false;
		}

		public static bool CanAccessDropbox()
		{
			try
			{
				var ping = new System.Net.NetworkInformation.Ping();

				// Waits up to 5 seconds. Hopefully we usually get a faster response
				// if Dropbox IS acccessible and also if we're offline altogether.
				var result = ping.Send("dropbox.com");

				return result.Status == System.Net.NetworkInformation.IPStatus.Success;
			}
			catch (Exception ex)
			{
				// Some problems, like being completely offline, produce exceptions
				// rather than a nice failure.
				return false;
			}
		}
	}
}
