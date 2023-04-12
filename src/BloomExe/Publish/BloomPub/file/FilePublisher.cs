// // Copyright (c) 2017 SIL International
// // This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System.Collections.Generic;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Properties;
using Bloom.web;
using L10NSharp;
using Bloom.Utils;

namespace Bloom.Publish.BloomPub.file
{
	/// <summary>
	/// Saves a .bloompub as a local file which the user can somehow get onto devices using some other tools.
	/// </summary>
	public class FilePublisher
	{
		public static void Save(Book.Book book, BookServer bookServer, Color backColor, WebSocketProgress progress, BloomPubPublishSettings settings = null)
		{
			var progressWithL10N = progress.WithL10NPrefix("PublishTab.Android.File.Progress.");

			// Settings.Default.BloomDeviceFileExportFolder was used to save between sessions, but is no longer.
			// Similar functionality may be extended to all save options such as PDF, BloomPub, and ePUB, but a
			// better name will probably be chosen at that point.  See BL-11996.
			var initialPath = OutputFilenames.GetOutputFilePath(book, BloomPubMaker.BloomPubExtensionWithDot);

			var bloomdFileDescription = LocalizationManager.GetString("PublishTab.Android.bloomdFileFormatLabel", "Bloom Book for Devices",
				"This is shown in the 'Save' dialog when you save a bloom book in the format that works with the Bloom Reader Android App");
			var filter = $"{bloomdFileDescription}|*{BloomPubMaker.BloomPubExtensionWithDot}";

			var destFileName = Utils.MiscUtils.GetOutputFilePathOutsideCollectionFolder(initialPath, filter);
			if (String.IsNullOrEmpty(destFileName))
				return;

			OutputFilenames.RememberOutputFilePath(book, BloomPubMaker.BloomPubExtensionWithDot, destFileName);
			//Settings.Default.BloomDeviceFileExportFolder = Path.GetDirectoryName(destFileName);
			PublishToBloomPubApi.CheckBookLayout(book, progress);
			PublishToBloomPubApi.SendBook(book, bookServer, destFileName, null,
				progressWithL10N,
				(publishedFileName, bookTitle) => progressWithL10N.GetMessageWithParams("Saving", "{0} is a file path", "Saving as {0}", destFileName),
				null,
				backColor,
				settings: settings);
			PublishToBloomPubApi.ReportAnalytics("file", book);
		}
	}
}
