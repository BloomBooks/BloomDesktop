// // Copyright (c) 2017 SIL International
// // This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Drawing;
using Bloom.Book;
using Bloom.Utils;
using Bloom.web;
using L10NSharp;

namespace Bloom.Publish.BloomPub.file
{
    /// <summary>
    /// Saves a .bloompub as a local file which the user can somehow get onto devices using some other tools.
    /// </summary>
    public class FilePublisher
    {
        public static void Save(
            Book.Book book,
            BookServer bookServer,
            Color backColor,
            WebSocketProgress progress,
            BloomPubPublishSettings settings = null
        )
        {
            var progressWithL10N = progress.WithL10NPrefix("PublishTab.Android.File.Progress.");

            // Settings.Default.BloomDeviceFileExportFolder was used to save between sessions, but is no longer.
            // Similar functionality may be extended to all save options such as PDF, BloomPub, and ePUB, but a
            // better name will probably be chosen at that point.  See BL-11996.
            var initialPath = FilePathMemory.GetOutputFilePath(
                book,
                BloomPubMaker.BloomPubExtensionWithDot
            );

            var bloomPubFileDescription = LocalizationManager.GetString(
                "PublishTab.Android.bloomdFileFormatLabel",
                "Bloom Book for Devices",
                "This is shown in the 'Save' dialog when you save a bloom book in the format that works with the Bloom Reader Android App"
            );
            var filter = $"{bloomPubFileDescription}|*{BloomPubMaker.BloomPubExtensionWithDot}";

            var destFileName = MiscUtils.GetOutputFilePathOutsideCollectionFolder(
                initialPath,
                filter
            );
            if (String.IsNullOrEmpty(destFileName))
                return;

            FilePathMemory.RememberOutputFilePath(
                book,
                BloomPubMaker.BloomPubExtensionWithDot,
                destFileName
            );
            //Settings.Default.BloomDeviceFileExportFolder = Path.GetDirectoryName(destFileName);
            PublishToBloomPubApi.CheckBookLayout(book, progress);
            PublishToBloomPubApi.SendBook(
                book,
                bookServer,
                destFileName,
                sendAction: null,
                progressWithL10N,
                startingMessageFunction: (publishedFileName, bookTitle) =>
                    progressWithL10N.GetMessageWithParams(
                        "Saving",
                        "{0} is a file path",
                        "Saving as {0}",
                        destFileName
                    ),
                confirmFunction: null,
                backColor,
                settings
            );
            PublishToBloomPubApi.ReportAnalytics("file", book);
        }
    }
}
