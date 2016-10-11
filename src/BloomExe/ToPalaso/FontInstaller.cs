using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using L10NSharp;
using SIL.IO;

namespace Bloom.ToPalaso
{
	/// <summary>
	/// Helper class for installing fonts.
	/// To use this: the sourceFolder passed to InstallFont must be one that GetDirectoryDistributedWithApplication can find.
	/// It must contain the fonts you want to be sure are installed.
	/// It MUST also contain the installer program, "Install Bloom Literacy Fonts.exe", a renamed version of FontReg.exe (see below).
	/// The user will typically see a UAC dialog asking whether it is OK to run this program (if the fonts are not already
	/// installed).
	/// </summary>
	public static class FontInstaller
	{
		public static bool InstallFont(string sourceFolder, bool needsRestart = true)
		{
			// This is not needed on Linux - fonts should be installed by adding a package
			// dependency, in this case fonts-sil-andika, or by installing that particular
			// package.
			if (!SIL.PlatformUtilities.Platform.IsWindows)
				return false;

			var sourcePath = FileLocator.GetDirectoryDistributedWithApplication(sourceFolder);
			if (AllFontsExist(sourcePath))
				return false; // already installed (Enhance: maybe one day we want to check version?)

			var info = new ProcessStartInfo
			{
				// Renamed to make the UAC dialog less mysterious.
				// Originally it is FontReg.exe (http://code.kliu.org/misc/fontreg/).
				// Eventually we will probably have to get our version signed.
				FileName = "Install Bloom Literacy Fonts.exe",
				Arguments = "/copy",
				WorkingDirectory = sourcePath,
				UseShellExecute = true, // required for runas to achieve privilege elevation
				WindowStyle = ProcessWindowStyle.Hidden,
				Verb = "runas" // that is, run as admin (required to install fonts)
			};

			try
			{
				Process.Start(info);
				if (needsRestart)
					Program.RestartBloom();
				return true;
			}
			// I hate catching 'Exception' but the one that is likely to happen, the user refused the privilege escalation
			// or is not authorized to do it, comes out as Win32Exception, which is not much more helpful.
			// We probably want to ignore anything else that can go wrong with trying to install the fonts.
			catch (Exception e)
			{
				SIL.Reporting.Logger.WriteEvent("**** Error trying to install font: " + e.Message);
				Debug.Fail(e.Message);
			}

			return false;
		}

		private static bool AllFontsExist(string sourcePath)
		{
			var fontFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
			foreach (var fontFile in Directory.GetFiles(sourcePath, "*.ttf"))
			{
				var destPath = Path.Combine(fontFolder, Path.GetFileName(fontFile));
				if (!RobustFile.Exists(destPath))
					return false;
			}
			return true;
		}
	}
}
