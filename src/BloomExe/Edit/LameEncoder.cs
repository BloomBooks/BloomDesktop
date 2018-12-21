using System;
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
		private static string s_pathToLame;

		/// <summary>
		/// Encode the given file as an .mp3 file in the same directory.
		/// </summary>
		/// <returns>Path to the new file</returns>
		public string Encode(string sourcePath)
		{
			string destinationPath = Path.ChangeExtension(sourcePath, AudioRecording.kPublishableExtension);

			try
			{
				if (RobustFile.Exists(destinationPath))
					RobustFile.Delete(destinationPath);
			}
			catch (Exception)
			{
				var shortMsg = LocalizationManager.GetString("LameEncoder.DeleteFailedShort", "Cannot replace mp3 file. Check antivirus");
				var longMsg = LocalizationManager.GetString("LameEncoder.DeleteFailedLong", "Bloom could not replace an mp3 file. If this continues, check your antivirus.");
				NonFatalProblem.Report(ModalIf.None, PassiveIf.All, shortMsg, longMsg);
				return null;
			}

			//-a downmix to mono
			string arguments = $"-a \"{sourcePath}\" \"{destinationPath}\"";
			ExecutionResult result = CommandLineRunner.Run(GetLamePath(), arguments, null, 60, new NullProgress());
			result.RaiseExceptionIfFailed("");
			return destinationPath;
		}

		private static string GetLamePath()
		{
			if (s_pathToLame != null)
				return s_pathToLame;
#if __MonoCS__
			return s_pathToLame = "/usr/bin/lame";
#else
			return s_pathToLame = FileLocationUtilities.GetFileDistributedWithApplication("lame.exe");
#endif
		}
	}
}
