using System;
using System.IO;
#if !__MonoCS__
using IWshRuntimeLibrary;
#endif
using File = System.IO.File;

namespace Bloom.Collection
{
	class ShortcutMaker
	{
		public static void CreateDirectoryShortcut(string targetPath, string whereToPutItPath)
		{
			var name = Path.GetFileName(targetPath);
			string linkPath = Path.Combine(whereToPutItPath, name) + ".lnk";
			if(File.Exists(linkPath))
				File.Delete(linkPath);

#if !__MonoCS__
			var WshShell = new WshShellClass();
			var shortcut = (IWshShortcut)WshShell.CreateShortcut(linkPath);

			try
			{
				shortcut.TargetPath = targetPath;
				//shortcut.Description = "Launch My Application";
				//shortcut.IconLocation = Application.StartupPath + @"\app.ico";
			}
			catch (Exception error)
			{
				if (targetPath !=System.Text.Encoding.ASCII.GetString(System.Text.Encoding.ASCII.GetBytes(targetPath)))
					throw new ApplicationException("Unfortunately, windows had trouble making a shortcut to remember this project, because of a problem with non-ASCII characters. Sorry!");
				throw error;
			}
			shortcut.Save();
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

			File.WriteAllText(linkPath, targetPath);
#endif
		}
	}
}
