using System;
using System.Collections.Generic;
using System.IO;
using L10NSharp;
using SIL.CommandLineProcessing;
using SIL.IO;
using SIL.Progress;

namespace Bloom.Edit
{
	/// <summary>
	/// This class, borrowed almost unchanged from HearThis, compresses .wav files to .mp3.
	/// Requires the installation of LAME.
	/// </summary>
	public class LameEncoder
	{
		private static string _pathToLAME;

		public void Encode(string sourcePath, string destPathWithoutExtension, IProgress progress)
		{
			LocateAndRememberLAMEPath();

			try
			{
				if (RobustFile.Exists(destPathWithoutExtension + ".mp3"))
					RobustFile.Delete(destPathWithoutExtension + ".mp3");
			}
			catch (Exception)
			{
				var shortMsg = LocalizationManager.GetString("LameEncoder.DeleteFailedShort", "Cannot replace mp3 file. Check antivirus");
				var longMsg = LocalizationManager.GetString("LameEncoder.DeleteFailedLong", "Bloom could not replace an mp3 file. If this continues, check your antivirus.");
				NonFatalProblem.Report(ModalIf.None, PassiveIf.All, shortMsg, longMsg);
				return;
			}

			progress.WriteMessage(LocalizationManager.GetString("LameEncoder.Progress", " Converting to mp3", "Appears in progress indicator"));

			//-a downmix to mono
			string arguments = string.Format("-a \"{0}\" \"{1}.mp3\"", sourcePath, destPathWithoutExtension);
			//ClipRepository.RunCommandLine(progress, _pathToLAME, arguments);
			ExecutionResult result = CommandLineRunner.Run(_pathToLAME, arguments, null, 60, progress);
			result.RaiseExceptionIfFailed("");
		}

		public string FormatName
		{
			get { return "mp3"; }
		}

		public static bool IsAvailable()
		{
			if (string.IsNullOrEmpty(LocateAndRememberLAMEPath()))
			{
				return false;
			}
			return true;
		}

		/// <summary>
		/// Find the path to LAME)
		/// </summary>
		/// <returns></returns>
		private static string LocateAndRememberLAMEPath()
		{
			if (null != _pathToLAME) // string.empty means we looked for LAME previously and didn't find it)
				return _pathToLAME;
			_pathToLAME = LocateLAME();
			return _pathToLAME;
		}

		/// <summary>
		/// </summary>
		/// <returns>the path, if found, else null</returns>
		static private string LocateLAME()
		{
#if __MonoCS__
			if (RobustFile.Exists("/usr/bin/lame"))
				return "/usr/bin/lame";
#else
			return FileLocationUtilities.GetFileDistributedWithApplication("lame.exe");
#endif
			return string.Empty;
		}
	}
}
