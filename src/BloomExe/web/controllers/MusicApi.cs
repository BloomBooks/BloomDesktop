using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Publish;
using L10NSharp;
using SIL.IO;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Api used by the Background Audio (Music) toolbox.
    /// The one current function is pretty generic...ask the user to choose a file, return it.
    /// There's a similar function in ReadersApi.
    /// If we get a third we might want to think about how to parameterize it in some common place.
    /// However, there are several variations, such has how to handle duplicates, the file type wanted,
    /// and so forth that make sharing a single API non-trivial.
    /// </summary>
    public class MusicApi
    {
        private readonly BookSelection _bookSelection;

        // Called by autofac, which creates the one instance and registers it with the server.
        public MusicApi(BookSelection _bookSelection)
        {
            this._bookSelection = _bookSelection;
        }

        // The current book we are editing. Needed so we can copy the background audio file to the appropriate subfolder
        private Book.Book CurrentBook
        {
            get { return _bookSelection.CurrentSelection; }
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler("music/ui/chooseFile", HandleRequest, true);
        }

        public void HandleRequest(ApiRequest request)
        {
            lock (request)
            {
                request.ReplyWithText(ShowSelectMusicFile());
            }
        }

        /// <summary>
        /// Check whether an .mp3 file is known not to be playable by Bloom.
        /// ("Invalid" may be too strong, but it isn't valid for Bloom!)
        /// </summary>
        /// <remarks>
        /// See https://silbloom.myjetbrains.com/youtrack/issue/BL-5742.
        /// </remarks>
        public static bool IsInvalidMp3File(string path)
        {
            if (Path.GetExtension(path).ToLowerInvariant() != ".mp3")
                return false; // doesn't claim to be an mp3 file.
            using (var stream = RobustFile.OpenRead(path))
            {
                var data = new byte[4];
                var count = stream.Read(data, 0, 4);
                stream.Close();
                if (count < 4)
                    return true; // way too short to be a valid audio file!
                if (
                    (char)data[0] == 'R'
                    && (char)data[1] == 'I'
                    && (char)data[2] == 'F'
                    && (char)data[3] == 'F'
                )
                {
                    // We may have an mpeg audio stream embedded in a RIFF (wave) format file.
                    // But Bloom can't play it, so we consider it invalid.
                    return true;
                }
            }
            return false; // nothing obviously wrong with the mp3 file
        }

        private string ShowSelectMusicFile()
        {
            var returnVal = "";

            var destPath = Path.Combine(CurrentBook.FolderPath, "audio");
            if (!Directory.Exists(destPath))
                Directory.CreateDirectory(destPath);

            var soundFiles = LocalizationManager.GetString(
                "EditTab.Toolbox.Music.FileDialogSoundFiles",
                "Sound files"
            );
            var dlg = new DialogAdapters.OpenFileDialogAdapter
            {
                Multiselect = false,
                CheckFileExists = true,
                Filter = $"{soundFiles} {BuildFileFilter()}"
            };
            var result = dlg.ShowDialog();
            if (result == DialogResult.OK)
            {
                var srcFile = dlg.FileName;
                var destFile = Path.GetFileName(srcFile);
                if (destFile != null)
                {
                    if (IsInvalidMp3File(srcFile))
                    {
                        var badMp3File = LocalizationManager.GetString(
                            "EditTab.Toolbox.Music.InvalidMp3File",
                            "The selected sound file \"{0}\" cannot be played by Bloom.  Please choose another sound file.",
                            "The {0} is replaced by the filename."
                        );
                        var badMp3Title = LocalizationManager.GetString(
                            "EditTab.Toolbox.Music.InvalidMp3FileTitle",
                            "Choose Another Sound File",
                            "Title for a message box"
                        );
                        MessageBox.Show(
                            String.Format(badMp3File, destFile),
                            badMp3Title,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                        return String.Empty;
                    }
                    // if file is already in the desired directory, do not try to copy it again.
                    if (Path.GetFullPath(srcFile) != Path.Combine(destPath, destFile))
                    {
                        var i = 0;

                        // get a unique destination file name
                        while (RobustFile.Exists(Path.Combine(destPath, destFile)))
                        {
                            // Enhance: if the file in the destination directory already has the required contents,
                            // we can just return this name.
                            destFile = Path.GetFileName(srcFile);
                            var fileExt = Path.GetExtension(srcFile);
                            destFile =
                                destFile.Substring(0, destFile.Length - fileExt.Length) + " - Copy";
                            if (++i > 1)
                                destFile += " " + i;
                            destFile += fileExt;
                        }

                        RobustFile.Copy(srcFile, Path.Combine(destPath, destFile));
                    }

                    returnVal = destFile;
                }
            }

            return returnVal;
        }

        private string BuildFileFilter()
        {
            var lowerExtensionString = string.Join(
                ";",
                AudioProcessor.MusicFileExtensionsToImport.Select(ext => "*" + ext)
            );
            var upperExtensionString = lowerExtensionString.ToUpperInvariant();
            return $"({lowerExtensionString})|{lowerExtensionString};{upperExtensionString}";
        }
    }
}
