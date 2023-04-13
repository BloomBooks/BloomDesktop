using System;
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
		/// <summary>
		/// Location(s) for the Dropbox info.json file based on information in
		/// https://help.dropbox.com/installs-integrations/desktop/locate-dropbox-folder,
		/// though it isn't precisely accurate.
		/// </summary>
		private static string[] s_jsonPaths = Platform.IsLinux ?
			new[] {"%HOME%/.dropbox/info.json"} :
			new[] {"%APPDATA%\\Dropbox\\info.json", "%LOCALAPPDATA%\\Dropbox\\info.json"};

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
		public static bool IsDropboxProcessRunning =>
			System.Diagnostics.Process.GetProcesses().Any(p => Platform.IsLinux ? p.ProcessName == "dropbox" : p.ProcessName.Contains("Dropbox"));

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
						foreach (var match in new Regex(s_pathMatchPattern).Matches(json).Cast<Match>())
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
