using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Mono.Unix;
using NAudio.Wave;
using SIL.Code;
using SIL.CommandLineProcessing;
using SIL.IO;
using SIL.PlatformUtilities;

namespace Bloom.Utils
{
	/// <summary>
	/// Collection of static utility methods that don't fit comfortably anywhere else.
	/// </summary>
	public static class MiscUtils
	{

		/// <summary>
		/// Action will be invoked on the calling, typically UI thread, after approximately
		/// the requested number of milliseconds, or when events are next handled.
		/// </summary>
		public static void SetTimeout(Action action, int timeout)
		{
			var timer = new Timer();
			timer.Interval = timeout;
			timer.Tick += delegate (object sender, EventArgs args)
			{
				timer.Stop();
				action();
			};
			timer.Start();
		}
		public static string CollectFilePermissionInformation(string filePath)
		{
			var bldr = new StringBuilder();
			try
			{
				if (Platform.IsWindows)
				{
					var currentUser = WindowsIdentity.GetCurrent();
					bldr.AppendLine($"current user is {currentUser.Name}");
					var principal = new WindowsPrincipal(currentUser);
					bool isInRoleWithAccess = false;
					bool accessDenied = false;
					bool accessAllowed = false;
					FileSystemRights accessRights = FileSystemRights.Write;
					var acl = File.GetAccessControl(filePath);
					var rules = acl.GetAccessRules(true, true, typeof(NTAccount));
					var sid = acl.GetOwner(typeof(SecurityIdentifier));
					var acct = sid.Translate(typeof(NTAccount)) as NTAccount;
					if (acct != null)
						bldr.AppendLine($"owner of \"{filePath}\" is {acct.Value}");
					var fileAttributes = RobustFile.GetAttributes(filePath);
					bldr.AppendLine($"{filePath} current ReadOnly attribute of {filePath} is {(fileAttributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly}");
					foreach (AuthorizationRule rule in rules)
					{
						var fsAccessRule = rule as FileSystemAccessRule;
						if (fsAccessRule == null)
							continue;
						if ((fsAccessRule.FileSystemRights & accessRights) > 0)
						{
							var ntAccount = rule.IdentityReference as NTAccount;
							if (ntAccount == null)
								continue;
							if (principal.IsInRole(ntAccount.Value))
							{
								if (fsAccessRule.AccessControlType == AccessControlType.Deny)
								{
									bldr.AppendLine($"current user is denied write access to {filePath} by {ntAccount.Value}{(rule.IsInherited ? " (inherited)":"")}");
									accessDenied = true;
								}
								if (fsAccessRule.AccessControlType == AccessControlType.Allow)
								{
									bldr.AppendLine($"current user is allowed write access to {filePath} by {ntAccount.Value}{(rule.IsInherited ? " (inherited)":"")}");
									accessAllowed = true;
								}
								isInRoleWithAccess = true;
							}
						}
					}
					if (isInRoleWithAccess)
					{
						if (!accessAllowed)
							bldr.AppendLine($"current user is not explicitly allowed write access to {filePath}");
						if (!accessDenied)
							bldr.AppendLine($"current user is not explicitly denied write access to {filePath}");
					}
					else
					{
						bldr.AppendLine($"current user is not explicitly given access to {filePath}");
					}
				}
				else
				{
					var folder = Path.GetDirectoryName(filePath);
					var fileInfo = new UnixFileInfo(filePath);
					var dirInfo = new UnixDirectoryInfo(folder);
					var userInfo = UnixUserInfo.GetRealUser();
					bldr.AppendLine($"current user is {userInfo.UserName}");
					bldr.AppendLine($"owner of \"{filePath}\" is {fileInfo.OwnerUser.UserName}");
					bldr.AppendLine($"permissions of \"{filePath}\" = {fileInfo.FileAccessPermissions.ToString()}");
					bldr.AppendLine($"owner of \"{folder}\" is {dirInfo.OwnerUser.UserName}");
					bldr.AppendLine($"permissions of \"{folder}\" = {dirInfo.FileAccessPermissions.ToString()}");
				}
			}
			catch (Exception e)
			{
				bldr.AppendLine($"Caught exception {e} while trying to collect information about {filePath}");
			}
			return bldr.ToString();
		}

		public static string InstalledAntivirusPrograms()
		{
			var collector = new List<string>();
			var error = InstalledAntivirusProgramsMof(collector);
			if (error != null)
				return error;
			return string.Join(Environment.NewLine, collector);
		}

		public static string InstalledAntivirusProgramsMof(List<string> collector)
		{
			if (Platform.IsWindows)
			{
				string wmipathstr = @"\\" + Environment.MachineName + @"\root\SecurityCenter2";
				try
				{
					var searcher =
						new ManagementObjectSearcher(wmipathstr, "SELECT * FROM AntivirusProduct");
					var instances = searcher.Get();
					foreach (var instance in instances)
					{
						collector.Add(instance.GetText(TextFormat.Mof));
					}
				}
				catch (Exception error)
				{
					return error.Message;
				}
			}
			return null; // no error
		}

		public static string InstalledAntivirusProgramNames()
		{
			var collector = new List<string>();
			var error = InstalledAntivirusProgramsMof(collector);
			if (error != null)
				return error;
			var result = new List<string>();
			foreach (var mof in collector)
			{
				var match = new Regex("displayName = \"(.*?)\"").Match(mof);
				if (match.Success)
				{
					result.Add(match.Groups[1].Value);
				}
			}

			return string.Join(", ", result);
		}

		/// <summary>
		/// Get what information we can about why a given file could not be written to the filesystem.
		/// This includes the identity of the current user, the identity of the file owner, the file
		/// permission information, and (grasping at straws) the system's anti-virus programs, if any.
		/// This information may be helpful for local technical support people even if we can't help
		/// users at a distance.
		/// </summary>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-9533.
		/// </remarks>
		public static string GetExtendedFileCopyErrorInformation(string path, string firstLine=null)
		{
			var bldr = new StringBuilder();
			if (!String.IsNullOrEmpty(firstLine))
				bldr.AppendLine(firstLine);
			if (RobustFile.Exists(path))
			{
				bldr.AppendLine($"You may find help for this problem at https://community.software.sil.org/t/when-bloom-is-prevented-from-changing-png-image-files/4445.");
				bldr.AppendLine($"The following specific information may also be helpful.");
				bldr.Append(CollectFilePermissionInformation(path));
			}
			else
			{
				bldr.AppendLine($"The file ({path}) does not exist!?");
				bldr.AppendLine($"The following specific information may be helpful.");
			}
			bldr.Append(InstalledAntivirusPrograms());
			return bldr.ToString();
		}

		/// <summary>
		/// Escapes the argument to "cmd /k"
		/// </summary>
		/// <remarks>Right now, this method is only designed to handle double quotes within {argument}.
		/// It is unknown whether there any other special characters that need special handling.</remarks>
		/// <param name="argument">The string to pass as the argument to the /k switch for "cmd".
		/// The argument (which is the command to run) should be properly quoted so as to work if you were to run it manually in a terminal.</param>
		/// <returns>A string in the proper format to pass directly to /k</returns>
		internal static string EscapeForCmd(string argument)
		{
			if (string.IsNullOrEmpty(argument))
				return argument;

			// Escaping special characters for Windows Batch seems to be a big ad-hoc, unintuitive mess.
			// (Lots of things in Batch are a mess...)
			// There are many different ways to encode special characters, and different scenarios require certain types of escaping.
			// For dealing with double quotes in an argument passed to "cmd /k", all you need to do is wrap the entire thing in double quotes,
			// leaving the double quotes in the string untouched.
			// Even though in C# this would be invalid syntax and a compiler error, this is the desired and required syntax for cmd.
			// Strange but true.
			// https://ss64.com/nt/syntax-esc.html

			// NOTE: Currently, we always wrap argument in double quotes, regardless of if it's strictly needed or not.
			// If desired, you could omit the double quotes if they're not strictly necessary.
			return $"\"{argument}\"";
		}

		/// <summary>
		/// Suppresses an "unused variable" warning for the exception variable in a catch block (by giving it this reference)
		/// </summary>
		/// <remarks>This can be useful because it allows you to keep the exception variable, even if the catch block doesn't organically reference it.
		/// You may want this because the exception variable in the catch provides an easy way to view the exception in the debugger.
		/// (Of course, you can also add a watch for $exception instead).
		/// </remarks>
		public static void SuppressUnusedExceptionVarWarning(Exception e)
		{
			// Paranoia
			if (e == null)
				return;

			// For now, we just write it to Debug
			Debug.WriteLine(e.ToString());

			// If desired, we could write it to the log file, or report to Sentry, etc...
		}

		public static string ColorToHtmlCode(Color color)
		{
			// thanks to http://stackoverflow.com/questions/982028/convert-net-color-objects-to-hex-codes-and-back
			return string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
		}

		/// <summary>
		/// Find the custom build of ffmpeg that Bloom uses. Unlike most of our dependencies, as of
		/// March 2022 (Bloom 5.3), the binary of this ffmpeg (for Windows) is checked in under our lib
		/// directory. It is built from a fork of the ffmpeg code at https://github.com/BloomBooks/ffmpeg.
		/// A quite complex custom build process is used to create an exe that is two orders of magnitude
		/// smaller than the default build with most features enabled. It has just the features we actually
		/// use. The process is documented in BuildingFFMpeg.md (in this repo, currently at DistFiles/ffmpeg,
		/// though I think that's the wrong place for it; we don't need to ship this to Bloom users).
		/// </summary>
		/// <returns></returns>
		public static string FindFfmpegProgram()
		{
			var ffmpeg = "/usr/bin/ffmpeg";     // standard Linux location
			if (SIL.PlatformUtilities.Platform.IsWindows)
				ffmpeg = Path.Combine(BloomFileLocator.GetCodeBaseFolder(), "ffmpeg.exe");
			return RobustFile.Exists(ffmpeg) ? ffmpeg : string.Empty;
		}

		/// <summary>
		/// Check whether the .bloomCollection file pointed to by path is either inside an uneditable source collection
		/// or is inside a ZIP file that needs to be unzipped before using.
		/// </summary>
		/// <returns><c>true</c> if the path points to an invalid collection to edit, <c>false</c> otherwise.</returns>
		public static bool ReportIfInvalidCollectionToEdit(string path)
		{
			if (IsInvalidCollectionToEdit(path))
			{
				var msg = L10NSharp.LocalizationManager.GetString("OpenCreateCloneControl.InSourceCollectionMessage",
					"This collection is part of your 'Sources for new books' which you can see in the bottom left of the Collections tab. It cannot be opened for editing.");
				MessageBox.Show(msg);
				return true;
			}
			if (IsInsideZipFile(path))
			{
				var msg = L10NSharp.LocalizationManager.GetString("OpenCreateCloneControl.InZipFileMessage",
					"It looks like you are trying to open a Bloom Collection from inside of a ZIP file. You need to first unzip the ZIP file, and then open the collection.");
				MessageBox.Show(msg);
				return true;
			}
			return false;
		}

		private static bool IsInsideZipFile(string path)
		{
			// Windows Explorer and 7-Zip both do a minimal extraction to the user's temp folder
			// when the user double-clicks on a .bloomCollection file inside a zip archive.
			// Something similar happens for archive programs on Linux.  Only Windows Explorer
			// creates the file on disk as read-only, so that's not a general check.  But all
			// of these create only the one (.bloomCollection) file in the temporary folder.
			var tempDir = Path.GetTempPath();
			var folder = Path.GetDirectoryName(path);
			if (SIL.PlatformUtilities.Platform.IsWindows)
			{
				if (folder.StartsWith(tempDir, StringComparison.InvariantCulture) &&
						(folder.Contains(@".zip\") ||   // Windows Explorer
						folder.Contains(@"\7z")))       // 7-Zip
				{
					return IsSingleFileInFolder(folder);
				}
			}
			else
			{
				var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
				if (folder.StartsWith(Path.Combine(tempDir, ".fr-"), StringComparison.InvariantCulture) ||
					folder.StartsWith(Path.Combine(tempDir, "xa-"), StringComparison.InvariantCulture) ||
					folder.StartsWith(Path.Combine(homeDir, ".cache/.fr-"), StringComparison.InvariantCulture))
				{
					return IsSingleFileInFolder(folder);
				}
			}
			return false;
		}

		/// <summary>
		/// Check if only one file (presumably the .bloomCollection file) is available in the folder.
		/// </summary>
		private static bool IsSingleFileInFolder(string folder)
		{
			var fileCount = Directory.EnumerateFiles(folder).Count();
			var dirCount = Directory.EnumerateDirectories(folder).Count();
			return (fileCount == 1 && dirCount == 0);
		}

		/// <summary>
		/// Check whether the path is inside either the installed collection folder or inside the folder
		/// containing factory template books.  If either condition is true, the collection cannot be edited.
		/// </summary>
		/// <returns><c>true</c> if the collection pointed to by path is not valid to edit, <c>false</c> otherwise.</returns>
		public static bool IsInvalidCollectionToEdit(string path)
		{
			return path.StartsWith(ProjectContext.GetInstalledCollectionsDirectory())
				|| path.StartsWith(BloomFileLocator.FactoryTemplateBookDirectory);
		}

		/// <summary>
		/// Reads all text (like File.ReadAllText) from a file. Works even if that file may
		/// be written to one or more times.
		/// e.g. reading the progress output file of ffmpeg while ffmpeg is running.
		/// </summary>
		/// <param name="path">path of the file to read</param>
		/// <returns>the contents of the file as a string</returns>
		public static string ReadAllTextFromFileWhichMightGetWrittenTo(string path)
		{
			return RetryUtility.Retry(()=> ReadAllTextFromFileWhichMightGetWrittenToInternal(path));
		}

		private static string ReadAllTextFromFileWhichMightGetWrittenToInternal(string path)
		{
			using (FileStream logFileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			using (StreamReader logFileReader = new StreamReader(logFileStream))
			{
				StringBuilder sb = new StringBuilder();

				char[] buffer = new char[4096];
				while (!logFileReader.EndOfStream)
				{
					logFileReader.ReadBlock(buffer, 0, buffer.Length);
					sb.Append(buffer);
				}

				return sb.ToString();
			}
		}

		/// <summary>
		/// Wrap a "Save File" dialog to prevent saving files inside the book's collection folder.
		/// </summary>
		/// <returns>The output file path, or null if canceled.</returns>
		public static string GetOutputFilePathOutsideCollectionFolder(string initialPath, string filter)
		{
			string initialFolder = Path.GetDirectoryName(initialPath);
			string initialFilename = Path.GetFileName(initialPath);
			string defaultExtension = Path.GetExtension(initialPath);
			var destFileName = String.Empty;
			var repeat = false;
			do
			{
				using (var dlg = new DialogAdapters.SaveFileDialogAdapter())
				{
					dlg.AddExtension = true;
					dlg.DefaultExt = defaultExtension;
					dlg.FileName = initialFilename;
					dlg.Filter = filter;
					dlg.RestoreDirectory = false;
					dlg.OverwritePrompt = true;
					dlg.InitialDirectory = initialFolder;
					if (DialogResult.Cancel == dlg.ShowDialog())
						return null;
					destFileName = dlg.FileName;
				}
				string collectionFolder;
				repeat = IsFolderInsideBloomCollection(Path.GetDirectoryName(destFileName), out collectionFolder);
				if (repeat)
				{
					WarnUserOfInvalidFolderChoice(collectionFolder, destFileName);
					// Change the initialFolder to just above the collection folder, or to the documents folder
					// if that ends up empty.
					initialFolder = Path.GetDirectoryName(collectionFolder);
					if (String.IsNullOrEmpty(initialFolder))
						initialFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
				}
			} while (repeat);
			return destFileName;
		}

		/// <summary>
		/// Wrap a "Choose Folder" dialog to prevent saving the selection being inside a book's collection folder.
		/// </summary>
		/// <returns>The output folder path, or an empty string if canceled.</returns>
		public static string GetOutputFolderOutsideCollectionFolder(string initialPath, string description, bool isForOutput)
		{
			var resultPath = string.Empty;
			bool repeat = false;
			do
			{
				resultPath = MiscUI.BloomFolderChooser.ChooseFolder(initialPath, description);
				if (string.IsNullOrEmpty(resultPath))
					return null;
				string collectionFolder = string.Empty;
				repeat = isForOutput && IsFolderInsideBloomCollection(resultPath, out collectionFolder);
				if (repeat)
				{
					WarnUserOfInvalidFolderChoice(collectionFolder, resultPath);
					// Change the initialFolder to just above the collection folder, or to the documents folder
					// if that ends up empty.
					initialPath = Path.GetDirectoryName(collectionFolder);
					if (string.IsNullOrEmpty(initialPath))
						initialPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
				}
			} while (repeat);
			return resultPath;
		}

		/// <summary>
		/// Check whether the given folder is a collection folder or inside a collection folder at
		/// any depth.  If so, return true and set collectionFolder for use in a warning message.
		/// If not, return false and set collectionFolder to null.
		/// </summary>
		/// <remarks>
		/// Note that C:\, C:\Users, C:\Users\Name, C:\Users\Name\Documents, and C:\Users\Name\Downloads
		/// are generally assumed never to be collection folders.  (Substitute /home for C:\Users on Linux.)
		/// </remarks>
		public static bool IsFolderInsideBloomCollection(string folder, out string collectionFolder)
		{
			collectionFolder = null;
			if (String.IsNullOrEmpty(folder) || !Directory.Exists(folder))
				return false;
			var personalFolder = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
			var userProfileFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			// The download folder could have a .bloomCollection file, but it's inconceivable that
			// it would actually be a collection folder.  See BL-11444.
			var downloadFolder = Path.Combine(userProfileFolder, "Downloads");	// why isn't this in the enumeration?
			if (folder == personalFolder || folder == userProfileFolder || folder == downloadFolder)
				return false;	// There should be no need to go further.
			if (Directory.EnumerateFiles(folder, "*.bloomCollection").Any())
			{
				collectionFolder = folder;
				return true;
			}
			return IsFolderInsideBloomCollection(Path.GetDirectoryName(folder), out collectionFolder);
		}

		public static void WarnUserOfInvalidFolderChoice(string collectionFolder, string chosenDestination)
		{
			var msgFmt = L10NSharp.LocalizationManager.GetString("MiscUtils.CannotSaveToCollectionFolder",
				"Bloom cannot save files inside the collection folder ({0}).  Please choose another location.");
			var msg = String.Format(msgFmt, collectionFolder);
			var buttons = new[]
				{
					new MiscUI.MessageBoxButton { Default = true, Text = L10NSharp.LocalizationManager.GetString("Common.OK","OK"), Id = "ok" }
				};
			MiscUI.BloomMessageBox.Show(Form.ActiveForm, $"<p>{msg}</p><p>{chosenDestination}</p>", buttons, MessageBoxIcon.Warning);
		}

		public static void DoOnceOnIdle(Action actionToDoOnIdle)
		{
			void HandleAction(object sender, EventArgs eventArgs)
			{
				Application.Idle -= HandleAction;
				actionToDoOnIdle();
			}

			Application.Idle += HandleAction;
		}

		public static TimeSpan GetMp3TimeSpan(string path, bool throwOnInvalidData = false)
		{
			try
			{
#if __MonoCS__
				// ffmpeg can provide the length of the audio, but you have to strip it out of the command line output
				// See https://stackoverflow.com/a/33115316/7442826 or https://stackoverflow.com/a/53648234/7442826
				// The output (which is sent to stderr, not stdout) looks something like this:
				// "size=N/A time=00:03:36.13 bitrate=N/A speed= 432x    \rsize=N/A time=00:07:13.16 bitrate=N/A speed= 433x    \rsize=N/A time=00:08:42.97 bitrate=N/A speed= 434x"
				// When seen on the console screen interactively, it looks like a single line that is updated frequently.
				// A short file may have only one carriage-return separated section of output, while a very long file may
				// have more sections than this.
				var args = String.Format("-v quiet -stats -i \"{0}\" -f null -", path);
				var result = CommandLineRunner.Run("/usr/bin/ffmpeg", args, "", 20 * 10, new SIL.Progress.NullProgress());
				var output = result.ExitCode == 0 ? result.StandardError : null;
				string timeString = null;
				if (!string.IsNullOrEmpty(output))
				{
					var idxTime = output.LastIndexOf("time=");
					if (idxTime > 0)
						timeString = output.Substring(idxTime + 5, 11);
				}
				return TimeSpan.Parse(timeString, CultureInfo.InvariantCulture);
#else
				using (var reader = new Mp3FileReader(path))
					return reader.TotalTime;
#endif
			}
			catch (Exception ex)
			{
				NonFatalProblem.Report(ModalIf.All, PassiveIf.All,
					"Bloom could not accurately determine the length of the audio file and will only make a very rough estimate.");
				if (throwOnInvalidData && ex is InvalidDataException)
					throw;
				// Crude estimate. In one sample, a 61K mp3 is 7s long.
				// So, multiply by 7 and divide by 61K to get seconds.
				// Then, to make a TimeSpan we need ticks, which are 0.1 microseconds,
				// hence the 10000000.
				return new TimeSpan(new FileInfo(path).Length * 7 * 10000000 / 61000);
			}
		}
	}
}
