using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.Book;
using Bloom.CollectionTab;
using Bloom.MiscUI;
using Bloom.Utils;
using Bloom.web;
using BloomTemp;
using Newtonsoft.Json;
using SIL.Extensions;
using SIL.IO;
using SIL.Program;

namespace Bloom.Publish.BloomPub
{
	public class BulkBloomPubCreator
	{
		private readonly BookServer _bookServer;
		private readonly CollectionModel _collectionModel;
		private readonly BloomWebSocketServer _webSocketServer;

		public delegate BulkBloomPubCreator Factory(BookServer bookServer, CollectionModel collectionModel,
			BloomWebSocketServer webSocketServer); //autofac uses this

		public BulkBloomPubCreator(BookServer bookServer, CollectionModel collectionModel,
			BloomWebSocketServer webSocketServer)
		{
			_bookServer = bookServer;
			_collectionModel = collectionModel;
			_webSocketServer = webSocketServer;
		}

		// Precondition: bulkSaveSettings must be non-null
		public async Task PublishAllBooksAsync(BulkBloomPubPublishSettings bulkSaveSettings)
		{
			await BrowserProgressDialog.DoWorkWithProgressDialogAsync(_webSocketServer,
				async (progress, worker) =>
				{
					var dest = new TemporaryFolder("BloomPubs");
					progress.MessageWithoutLocalizing($"Creating files in {dest.FolderPath}...");

					var filenameWithoutExtension = _collectionModel.CollectionSettings.DefaultBookshelf.SanitizeFilename(' ', true);
;
					if (bulkSaveSettings.makeBookshelfFile)
					{
						// see https://docs.google.com/document/d/1UUvwxJ32W2X5CRgq-TS-1HmPj7gCKH9Y9bxZKbmpdAI

						progress.MessageWithoutLocalizing($"Creating bloomshelf file...");
						System.Diagnostics.Debug.Assert(!bulkSaveSettings.bookshelfColor.Contains("\n") && !bulkSaveSettings.bookshelfColor.Contains("\r"), "(BL-10190 Repro) Invalid bookshelfColor setting (contains newline). Please investigate!");
						var colorString = getBloomReaderColorString(bulkSaveSettings.bookshelfColor);
						System.Diagnostics.Debug.Assert(!colorString.Contains("\n") && !colorString.Contains("\r"), "(BL-10190 Repro) Invalid computed colorString value (contains newline). Please investigate!");

						// OK I know this looks lame but trust me, using jsconvert to make that trivial label array is way too verbose.
						var template =
							"{ 'label': [{ 'en': 'bookshelf-name'}], 'id': 'id-of-the-bookshelf', 'color': 'hex-color-value'}";
						var json = template.Replace('\'', '"')
							.Replace("bookshelf-name", bulkSaveSettings.bookshelfLabel.Replace('"', '\''))
							.Replace("id-of-the-bookshelf", _collectionModel.CollectionSettings.DefaultBookshelf)
							.Replace("hex-color-value", colorString);
						var filename = $"{filenameWithoutExtension}.bloomshelf";
						var bloomShelfPath = Path.Combine(dest.FolderPath, filename);
						RobustFile.WriteAllText(bloomShelfPath, json, Encoding.UTF8);
					}

					foreach (var bookInfo in _collectionModel.TheOneEditableCollection.GetBookInfos())
					{
						if (worker.CancellationPending)
						{
							progress.MessageWithoutLocalizing("Cancelled.");
							return true;
						}
						progress.MessageWithoutLocalizing($"Making BloomPUB for {bookInfo.QuickTitleUserDisplay}...",
							ProgressKind.Heading);

						var settings = BloomPubPublishSettings.GetPublishSettingsForBook(_bookServer, bookInfo);
						settings.DistributionTag = bulkSaveSettings.distributionTag;
						if (bulkSaveSettings.makeBookshelfFile)
						{
							settings.BookshelfTag = _collectionModel.CollectionSettings.DefaultBookshelf;
						}
						BloomPubMaker.CreateBloomPub(settings, bookInfo, dest.FolderPath, _bookServer, progress);
					}

					if (bulkSaveSettings.makeBloomBundle)
					{
						var bloomBundlePath = Path.Combine(dest.FolderPath, $"{filenameWithoutExtension}.bloombundle");

						var bloomBundleFile = new BloomTarArchive(bloomBundlePath);
						bloomBundleFile.AddDirectoryContents(dest.FolderPath, new string[] { ".bloombundle" });
						bloomBundleFile.Save();
					}

					progress.MessageWithoutLocalizing("Done.", ProgressKind.Heading);
					Process.SafeStart(dest.FolderPath);
					// true means wait for the user, don't close automatically
					return true;
				}, "readerPublish", "Bulk Save BloomPubs", showCancelButton: true);
		}


		/// <summary>
		/// Bloom Reader expects this to not have a # sign, and to be a hex value.
		/// Here we take a defensive approach that guarantees that whether we are given
		/// a name of a color, a bogus name, nothing at all, a #rrggbbaa, or rrggbb, etc., we'll return a hex value
		/// value that Bloom Reader can handle.
		/// </summary>
		private string getBloomReaderColorString(string s)
		{
			string colorString;
			int ignore = 0;
			// is it already a nice HTML color?
			if (s.StartsWith("#"))
			{
				colorString = s.Replace("#", "");
			}
			// is it at least a Hex number?
			else if (Int32.TryParse(s, System.Globalization.NumberStyles.HexNumber,
				System.Globalization.CultureInfo.InvariantCulture, out ignore))
			{
				colorString = s;
			}
			// is it a recognized color name?
			else
			{
				var color = Color.FromName(s);

				// wasn't recognized
				if (color.IsEmpty || !color.IsKnownColor)
				{
					colorString = "FFFF00"; // we give up, so yellow it is
				}
				// the name was recognized, so make it into a hex number
				else
				{
					colorString = string.Format("{0:x6}", color.ToArgb()).ToUpperInvariant();
				}
			}
			return colorString;
		}
	}
}
