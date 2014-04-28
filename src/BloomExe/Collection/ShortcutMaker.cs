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
#if !__MonoCS__
			var name = Path.GetFileName(targetPath);
			var WshShell = new WshShellClass();
			string linkPath = Path.Combine(whereToPutItPath, name) + ".lnk";
			if(File.Exists(linkPath))
			{
				File.Delete(linkPath);
			}
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
			throw new NotImplementedException();
#endif
		}
	}
}
