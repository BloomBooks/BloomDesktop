using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SIL.IO;
#if !__MonoCS__
using IWshRuntimeLibrary;
#endif

namespace Bloom.Collection
{
	static class ShortcutMaker
	{
#if !__MonoCS__
		const int MAX_PATH = 255;

		[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
		private static extern int GetShortPathName([MarshalAs(UnmanagedType.LPTStr)] string path,
			[MarshalAs(UnmanagedType.LPTStr)] StringBuilder shortPath, int shortPathLength);
#endif

		public static void CreateDirectoryShortcut(string targetPath, string whereToPutItPath)
		{
			var name = Path.GetFileName(targetPath);
			var linkPath = Path.Combine(whereToPutItPath, name) + ".lnk";
			var shortLinkPath = "";

			if(RobustFile.Exists(linkPath))
				RobustFile.Delete(linkPath);

#if !__MonoCS__
			var wshShell = new WshShellClass();
			var shortcut = (IWshShortcut)wshShell.CreateShortcut(linkPath);

			try
			{
				shortcut.TargetPath = targetPath;
			}
			catch (Exception)
			{
				if (targetPath == Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(targetPath))) throw;

				// this exception was caused by non-ascii characters in the path, use 8.3 names instead
				var shortTargetPath = new StringBuilder(MAX_PATH);
				GetShortPathName(targetPath, shortTargetPath, MAX_PATH);

				var shortWhereToPutPath = new StringBuilder(MAX_PATH);
				GetShortPathName(whereToPutItPath, shortWhereToPutPath, MAX_PATH);

				name = Path.GetFileName(shortTargetPath.ToString());

				shortLinkPath = Path.Combine(shortWhereToPutPath.ToString(), name) + ".lnk";
				if (RobustFile.Exists(shortLinkPath))
					RobustFile.Delete(shortLinkPath);

				shortcut = (IWshShortcut)wshShell.CreateShortcut(shortLinkPath);
				shortcut.TargetPath = shortTargetPath.ToString();
			}

			shortcut.Save();

			// now rename the link to the correct name if needed
			if (!string.IsNullOrEmpty(shortLinkPath))
				RobustFile.Move(shortLinkPath, linkPath);

#else
			// It's tempting to use symbolic links instead which would work much nicer - iff
			// the UnixSymbolicLinkInfo class wouldn't cause us to crash...
//			var name = Path.GetFileName(targetPath);
//			string linkPath = Path.Combine(whereToPutItPath, name);
//			var shortcut = new Mono.Unix.UnixSymbolicLinkInfo(linkPath);
//			if (shortcut.Exists)
//				shortcut.Delete();
//
//			var target = new Mono.Unix.UnixSymbolicLinkInfo(targetPath);
//			target.CreateSymbolicLink(linkPath);

			RobustFile.WriteAllText(linkPath, targetPath);
#endif
		}
	}
}
