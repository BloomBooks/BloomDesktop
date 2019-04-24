// // Copyright (c) 2017 SIL International
// // This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Properties;
using Bloom.web;
using L10NSharp;

namespace Bloom.Publish.Android.file
{
	/// <summary>
	/// Saves a .bloomd as a local file which the user can somehow get onto devices using some other tools.
	/// </summary>
	public class FilePublisher
	{
		public static void Save(Book.Book book, BookServer bookServer, Color backColor, WebSocketProgress progress)
		{
			var progressWithL10N = (WebSocketProgress)progress.WithL10NPrefix("PublishTab.Android.File.Progress.");
			using (var dlg = new DialogAdapters.SaveFileDialogAdapter())
			{
				dlg.DefaultExt = BookCompressor.ExtensionForDeviceBloomBook;
				var bloomdFileDescription = LocalizationManager.GetString("PublishTab.Android.bloomdFileFormatLabel", "Bloom Book for Devices", "This is shown in the 'Save' dialog when you save a bloom book in the format that works with the Bloom Reader Android App");
				dlg.Filter = $"{bloomdFileDescription}|*{BookCompressor.ExtensionForDeviceBloomBook}";
				dlg.FileName = Path.GetFileName(book.FolderPath) + BookCompressor.ExtensionForDeviceBloomBook;
				if(!string.IsNullOrWhiteSpace(Settings.Default.BloomDeviceFileExportFolder) &&
					Directory.Exists(Settings.Default.BloomDeviceFileExportFolder))
				{
					dlg.InitialDirectory = Settings.Default.BloomDeviceFileExportFolder;
					//(otherwise leave to default save location)
				}
				dlg.OverwritePrompt = true;
				if (DialogResult.OK == dlg.ShowDialog())
				{
					Settings.Default.BloomDeviceFileExportFolder = Path.GetDirectoryName(dlg.FileName);
					PublishToAndroidApi.CheckBookLayout(book, progress);
					PublishToAndroidApi.SendBook(book, bookServer, dlg.FileName, null,
						progressWithL10N,
						(publishedFileName, bookTitle) => progressWithL10N.GetMessageWithParams("Saving", "{0} is a file path", "Saving as {0}", dlg.FileName),
						null,
						backColor);
					PublishToAndroidApi.ReportAnalytics("file", book);
				}
			}
		}
	}
}
