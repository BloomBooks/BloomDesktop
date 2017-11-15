using System;
using System.IO;
using L10NSharp;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Reporting;
using Bloom.Properties;

namespace Bloom.MiscUI
{
	/// <summary>
	/// This class tests for a Windows Defender setting that keeps Bloom from functioning.
	/// If the bogus setting is active, Bloom will pop up a message to the user and then exit.
	/// </summary>
	public static class DefenderFolderProtectionCheck
	{
		private const string TestFileName = "test.txt";

		/// <summary>
		/// Returns 'true' unless we find we can't run Bloom.
		/// In Oct of 2017, a Windows update to Defender on some machines set Protections on certain
		/// basic folders, like MyDocuments! This resulted in throwing an exception any time Bloom tried
		/// to write out CollectionSettings files!
		/// </summary>
		/// <returns></returns>
		public static bool CanContinue()
		{
			if (!Platform.IsWindows)
			{
				return true;
			}
			var testPath = GetMruProjectTestPath;
			try
			{
				RobustFile.WriteAllText(testPath, "test contents");
			}
			catch (Exception exc)
			{
				// Creating a previously non-existent file under these conditions just gives a WinIOError, "could not find file".
				ReportDefenderProblem(exc);
				return false;
			}
			finally
			{
				Cleanup(testPath);
			}
			return true;
		}

		// We probably care about MyDocuments if we're about to create a "Bloom" directory (new installation)
		// or a new Collection under "Bloom". But we always care about the actual bloom collection folder, which could be anywhere
		// on disk. Indeed, one work-around is to move your collection to some place that is not under Defender lock down.
		// This property should return the folder that either does or will contain the collection we will be opening.
		private static string GetMruProjectTestPath
		{
			get
			{
				var path = Settings.Default.MruProjects.Latest;
				if (!string.IsNullOrEmpty(path))
				{
					return Path.Combine(Path.GetDirectoryName(path), TestFileName) ;
				}

				return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), TestFileName);
			}
		}

		private static void ReportDefenderProblem(Exception exc)
		{
			var heading = LocalizationManager.GetString("DefenderFolderProtectionCheck.WindowsDefenderProblemHeading", "Check Windows Defender “Controlled Access”.");
			var mainMsg = LocalizationManager.GetString("DefenderFolderProtectionCheck.WindowsDefenderProblem",
				"A Windows update around October 2017 added a feature which prevents Bloom from being able to write its own files, if the collection folder is in a “Controlled Folder”. Your “Documents” folder is one such “Controlled Folder”, and by default, that is where Bloom collections live.");
			var msg = heading + Environment.NewLine + Environment.NewLine + mainMsg;
			ErrorReport.NotifyUserOfProblem(exc, msg);
		}

		private static void Cleanup(string testPath)
		{
			// try to clean up behind ourselves
			try
			{
				if (File.Exists(testPath))
				{
					File.Delete(testPath);
				}
			}
			catch (Exception)
			{
				// but don't try too hard
			}
		}
	}
}
