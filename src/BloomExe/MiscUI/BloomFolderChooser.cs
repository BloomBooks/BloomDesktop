using System.Windows.Forms;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace Bloom.MiscUI
{
	/// <summary>
	/// Static class for launching a folder chooser dialog.  This launches the modern Windows
	/// file chooser dialog as a folder chooser on Windows.  Using DialogAdapters.FolderBrowser
	/// would launch an old fashioned dialog chooser on Windows.
	/// Linux sticks with the tried and true means of selecting folders through the GUI.
	/// </summary>
	public static class BloomFolderChooser
	{
		public static string ChooseFolder(string initialFolderPath)
		{
			if (SIL.PlatformUtilities.Platform.IsWindows)
			{
				// Split into a separate method to prevent the Mono runtime from trying to
				// reference Windows-only assemblies on Linux.
				return SelectFolderOnWindows(initialFolderPath);
			}
			else
			{
				var dialog = new DialogAdapters.FolderBrowserDialogAdapter();
				dialog.SelectedPath = initialFolderPath;
				if (dialog.ShowDialog() == DialogResult.OK)
					return dialog.SelectedPath;
			}
			return null;
		}

		private static string SelectFolderOnWindows(string initialFolderPath)
		{
			// Note, this is Windows only.
			CommonOpenFileDialog dialog = new CommonOpenFileDialog
			{
				InitialDirectory = initialFolderPath,
				IsFolderPicker = true
			};
			if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
				return dialog.FileName;
			return null;
		}
	}
}
