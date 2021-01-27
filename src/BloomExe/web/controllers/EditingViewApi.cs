using System;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Edit;
using L10NSharp;
using System.IO;
using SIL.IO;

namespace Bloom.web.controllers
{
	/// <summary>
	/// Api for handling requests regarding the edit tab view itself
	/// </summary>
	public class EditingViewApi
	{
		public EditingView View { get; set; }

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("editView/setModalState", HandleSetModalState, true);
			apiHandler.RegisterEndpointHandler("editView/chooseWidget", HandleChooseWidget, true);
			apiHandler.RegisterEndpointHandler("editView/getBookColors", HandleGetColors, true);
		}

		public void HandleSetModalState(ApiRequest request)
		{
			lock (request)
			{
				View.SetModalState(request.RequiredPostBooleanAsJson());
				request.PostSucceeded();
			}
		}

		private void HandleChooseWidget(ApiRequest request)
		{
			if (!View.Model.CanChangeImages())
			{
				// Enhance: more widget-specific message?
				MessageBox.Show(
					LocalizationManager.GetString("EditTab.CantPasteImageLocked",
						"Sorry, this book is locked down so that images cannot be changed."));
				request.ReplyWithText("");
				return;
			}

			var fileType = ".wdgt;*.html";
			using (var dlg = new DialogAdapters.OpenFileDialogAdapter
			{
				Multiselect = false,
				CheckFileExists = true,
				Filter = $"Widget files|*{fileType}"
			})
			{
				var result = dlg.ShowDialog();
				if (result != DialogResult.OK)
				{
					request.ReplyWithText("");
					return;
				}

				var fullWidgetPath = dlg.FileName;
				var ext = Path.GetExtension(fullWidgetPath);
				if (ext.EndsWith("htm") || ext.EndsWith("html"))
				{
					fullWidgetPath = CreateWidgetFromHtmlFolder(fullWidgetPath);
				}
				string activityName = View.Model.MakeActivity(fullWidgetPath);
				var url = UrlPathString.CreateFromUnencodedString(activityName);
				request.ReplyWithText(url.UrlEncodedForHttpPath);
				// clean up the temporary widget file we created.
				if (fullWidgetPath != dlg.FileName)
					RobustFile.Delete(fullWidgetPath);
			}
		}

		private string CreateWidgetFromHtmlFolder(string fullWidgetPath)
		{
			var filename = Path.GetFileName(fullWidgetPath);
			var fullFolderName = Path.GetDirectoryName(fullWidgetPath);
			// First attempt: get the widget name from the name of the directory
			// containing the .html file.
			var widgetName = Path.GetFileName(fullFolderName);
			if (widgetName == "HTML5")
			{
				// Active Presenter export HTML5 files to folder structure <ProjectName>/HTML5/<files>
				// where the files include possibly practice.html, tutorial.html, and/or test.html.
				// The user presumably picked one of these three files.  Get the widget name from
				// the directory name from the level above the HTML5 subfolder.
				widgetName = Path.GetFileName(Path.GetDirectoryName(fullFolderName));
			}
			if (String.IsNullOrWhiteSpace(widgetName))
			{
				// The .html file must be at the top level of the filesystem??
				widgetName = "MYWIDGET";	// I doubt this ever happens, but it saves a compiler warning.
			}
			else if (widgetName.EndsWith(".wdgt"))
			{
				// Book Widgets creates a folder named <ProjectName>.wdgt in which to store the
				// widget files.  Why a folder and not a zip file, only the programmers/analysts
				// know...  So strip the extension off the folder name to get the widget name.
				widgetName = Path.GetFileNameWithoutExtension(widgetName);
			}
			var widgetPath = Path.Combine(Path.GetTempPath(), "Bloom", widgetName + ".wdgt");
			var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
			try
			{
				// Copy the relevant files and folders to a temporary location.
				CopyFolder(fullFolderName, tempDir);
				// Remove excess html files (if any), and ensure desired html file is named "index.html".
				foreach (var filePath in Directory.GetFiles(tempDir))
				{
					var name = Path.GetFileName(filePath);
					if (name == filename)
					{
						if (filename != "index.html")
							RobustFile.Move(filePath, Path.Combine(tempDir, "index.html"));
					}
					else if (name.EndsWith(".htm") || name.EndsWith(".html"))
					{
						RobustFile.Delete(filePath);
					}
				}
				// zip up the temporary folder contents into a widget file
				var zip = new BloomZipFile(widgetPath);
				foreach (var file in Directory.GetFiles(tempDir))
				{
					zip.AddTopLevelFile(file);
				}
				foreach (var dir in Directory.GetDirectories(tempDir))
					zip.AddDirectory(dir);
				zip.Save();
			}
			finally
			{
				// Delete the temporary folder and its contents.
				Directory.Delete(tempDir, true);
			}
			return widgetPath;
		}

		private static void CopyFolder(string sourcePath, string destinationPath)
		{
			Directory.CreateDirectory(destinationPath);
			foreach (var filePath in Directory.GetFiles(sourcePath))
			{
				RobustFile.Copy(filePath, Path.Combine(destinationPath, Path.GetFileName(filePath)));
			}
			foreach (var dirPath in Directory.GetDirectories(sourcePath))
			{
				CopyFolder(dirPath, Path.Combine(destinationPath, Path.GetFileName(dirPath)));
			}
		}

		private void HandleGetColors(ApiRequest request)
		{
			var model = View.Model;
			if (!model.HaveCurrentEditableBook)
			{
				request.ReplyWithText("");
				return;
			}
			var currentBook = View.Model.CurrentBook;
			// Enhance: Two ideas. (1) Get colors from the current page first, in case the book has too many
			// colors to fit in our dialog's swatch array. (2) Order the list returned by frequency, so the most
			// frequently used colors are at the front of the resultant swatch array.
			var currentBookDom = currentBook.OurHtmlDom;
			var colors = currentBookDom.GetColorsUsedInBookBubbleElements();
			request.ReplyWithText(colors);
		}
	}
}
