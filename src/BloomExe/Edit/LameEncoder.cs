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
	/// Requires the (currently separate) installation of LAME.
	/// </summary>
	public class LameEncoder
	{
		private static string _pathToLAME;

		public void Encode(string sourcePath, string destPathWithoutExtension, IProgress progress)
		{
			LocateAndRememberLAMEPath();

			if (RobustFile.Exists(destPathWithoutExtension + ".mp3"))
				RobustFile.Delete(destPathWithoutExtension + ".mp3");

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
#if !MONO
			//nb: this is sensitive to whether we are compiled against win32 or not,
			//not just the host OS, as you might guess.
			var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);


			var progFileDirs = new List<string>
				{
					pf.Replace(" (x86)", ""),			//native (win32 or 64, depending)
					pf.Replace(" (x86)", "")+" (x86)"	//win32
				};


			foreach (var path in progFileDirs)
			{
				var exePath = (Path.Combine(path, "LAME for Audacity/lame.exe"));
				if (RobustFile.Exists(exePath))
					return exePath;
			}
			return string.Empty;
#endif
		}
	}
}