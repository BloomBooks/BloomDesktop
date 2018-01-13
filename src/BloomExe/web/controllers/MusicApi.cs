using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using L10NSharp;
using SIL.IO;

namespace Bloom.web.controllers
{
	/// <summary>
	/// Api used by the Background Audio (Music) toobox.
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
		private Book.Book CurrentBook { get { return _bookSelection.CurrentSelection; } }


		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler("music/ui/chooseFile", HandleRequest, true);
		}

		public void HandleRequest(ApiRequest request)
		{
			lock (request)
			{
				request.ReplyWithText(ShowSelectMusicFile());
			}
		}

		private string ShowSelectMusicFile()
		{
			var returnVal = "";

			var destPath = Path.Combine(CurrentBook.FolderPath, "audio");
			if (!Directory.Exists(destPath)) Directory.CreateDirectory(destPath);

			var soundFiles = LocalizationManager.GetString("EditTab.Toolbox.Music.FileDialogSoundFiles", "Sound files");
			var dlg = new OpenFileDialog
			{
				Multiselect = false,
				CheckFileExists = true,
				Filter = String.Format("{0} (*.mp3;*.wav)|*.mp3;*.wav", soundFiles)
			};
			var result = dlg.ShowDialog();
			if (result == DialogResult.OK)
			{
				var srcFile = dlg.FileName;
				var destFile = Path.GetFileName(srcFile);
				if (destFile != null)
				{
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
							destFile = destFile.Substring(0, destFile.Length - fileExt.Length) + " - Copy";
							if (++i > 1) destFile += " " + i;
							destFile += fileExt;
						}

						RobustFile.Copy(srcFile, Path.Combine(destPath, destFile));
					}

					returnVal = destFile;
				}
			}

			return returnVal;
		}
	}
}
