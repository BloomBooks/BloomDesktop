using System.Windows.Forms;
#if !__MonoCS__
using System.Linq;
using Microsoft.WindowsAPICodePack.Dialogs;
#endif

namespace Bloom.MiscUI
{
    /// <summary>
    /// Static class for launching a folder chooser dialog.  This launches the modern Windows
    /// file chooser dialog as a folder chooser on Windows.
    /// </summary>
    public static class BloomFolderChooser
    {
        public static string ChooseFolder(string initialFolderPath, string description = null)
        {
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
            var result =
                rootForm == null ? dialog.ShowDialog() : dialog.ShowDialog(rootForm.Handle);
            if (result == CommonFileDialogResult.Ok)
                return dialog.FileName;
            return null;
        }
    }
}
