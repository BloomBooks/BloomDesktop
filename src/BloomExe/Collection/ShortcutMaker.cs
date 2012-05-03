using System.IO;
using IWshRuntimeLibrary;
using File = System.IO.File;

namespace Bloom.Collection
{
	class ShortcutMaker
	{
		public static void CreateDirectoryShortcut(string targetPath, string whereToPutItPath)
		{
			var name = Path.GetFileName(targetPath);
			var WshShell = new WshShellClass();
			string linkPath = Path.Combine(whereToPutItPath, name) + ".lnk";
			if(File.Exists(linkPath))
			{
				File.Delete(linkPath);
			}
			var shortcut = (IWshShortcut)WshShell.CreateShortcut(linkPath);
			shortcut.TargetPath = targetPath;
			//shortcut.Description = "Launch My Application";
			//shortcut.IconLocation = Application.StartupPath + @"\app.ico";
			shortcut.Save();
		}
	}
}
