using System.Linq;
using System.Windows.Forms;

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
            // Under --e2e, answer with the folder a test armed rather than showing the dialog,
            // which no automation can dismiss. See BloomOpenFileDialog, which shares the armed
            // path, so a test arms one path whichever kind of chooser the feature opens.
            if (BloomOpenFileDialog.TryTakeNextPathToChooseInE2eTests(out var armedPath))
            {
                if (!System.IO.Directory.Exists(armedPath))
                    throw new System.IO.DirectoryNotFoundException(
                        $"The e2e test armed the folder chooser with a folder that does not exist: {armedPath}"
                    );
                return armedPath;
            }
            // On .NET Core 3.0 and later this is the modern Windows folder picker. It must be
            // shown on the UI thread, which is where every caller's API endpoint runs.
            using var dialog = new FolderBrowserDialog
            {
                InitialDirectory = initialFolderPath,
                UseDescriptionForTitle = true,
            };
            if (!string.IsNullOrEmpty(description))
                dialog.Description = description;
            // If we can find Bloom's main window, bring the dialog up on that screen.
            var rootForm = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
            var result = rootForm == null ? dialog.ShowDialog() : dialog.ShowDialog(rootForm);
            if (result == DialogResult.OK)
                return dialog.SelectedPath;
            return null;
        }
    }
}
