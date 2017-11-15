using Bloom.Properties;
using SIL.PlatformUtilities;
using System;
using System.IO;
using L10NSharp;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.MiscUI
{
	/// <summary>
	/// This is a class for testing various user System things to make sure Bloom can run safely.
	/// </summary>
	public static class BloomSystemCheck
	{
		private const string TestContents = "test";
		private const string TestFileName = "test.txt";

		/// <summary>
		/// Returns 'true' unless we find some reason to not run Bloom.
		/// For example, in Oct of 2017, a Windows update to Defender on some machines set Protections on certain
		/// basic folders, like MyDocuments! This resulted in throwing an exception any time Bloom tried
		/// to write out CollectionSettings files!
		/// </summary>
		/// <returns></returns>
		public static bool CanContinue()
		{
			if (!Platform.IsWindows)
			{
				return true; // so far this is only needed on Windows
			}
			var testPath = GetMyDocsTestPath;
			try
			{
				RobustFile.WriteAllText(testPath, TestContents);
			}
			catch (Exception exc)
			{
				// Creating a previously non-existent file under these conditions just gives a WinIOError, "could not find file".
				ReportDefenderProblem(exc);
				return false;				
			}
			Cleanup(testPath);
			return true;
		}

		private static string GetMyDocsTestPath
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
			var heading = LocalizationManager.GetString("BloomSystemCheck.WindowsDefenderProblemHeading", "Check Windows Defender “Controlled Access”.");
			var mainMsg = LocalizationManager.GetString("BloomSystemCheck.WindowsDefenderProblem",
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
