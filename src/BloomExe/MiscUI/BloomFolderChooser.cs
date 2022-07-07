using System.Windows.Forms;
#if !__MonoCS__
using System.Linq;
using Microsoft.WindowsAPICodePack.Dialogs;
#endif

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
		public static string ChooseFolder(string initialFolderPath, string description = null)
		{
			if (SIL.PlatformUtilities.Platform.IsWindows)
			{
				// Split into a separate method to prevent the Mono runtime from trying to
				// reference Windows-only assemblies on Linux.
				return SelectFolderOnWindows(initialFolderPath, description);
			}
			else
			{
				var dialog = new DialogAdapters.FolderBrowserDialogAdapter();
				if (!string.IsNullOrEmpty(initialFolderPath))
					dialog.SelectedPath = initialFolderPath;
				if (!string.IsNullOrEmpty(description))
					dialog.Description = description;
				if (dialog.ShowDialog() == DialogResult.OK)
					return dialog.SelectedPath;
			}
			return null;
		}

		private static string SelectFolderOnWindows(string initialFolderPath, string description)
		{
#if !__MonoCS__
			// Note, this is Windows only.
			CommonOpenFileDialog dialog = new CommonOpenFileDialog
			{
				InitialDirectory = initialFolderPath,
				IsFolderPicker = true
			};
			if (!string.IsNullOrEmpty(description))
				dialog.Title = description;
			// If we can find Bloom's main window, bring the dialog up on that screen.
			var rootForm = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
			var result = rootForm == null ? dialog.ShowDialog() : dialog.ShowDialog(rootForm.Handle);
			if (result == CommonFileDialogResult.Ok)
				return dialog.FileName;
#endif
			return null;
		}
	}
}
