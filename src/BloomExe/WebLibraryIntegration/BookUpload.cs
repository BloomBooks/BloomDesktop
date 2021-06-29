using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Amazon.Runtime;
using Amazon.S3;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Publish;
using DesktopAnalytics;
using L10NSharp;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;
using System.Xml;
using System.Text;

namespace Bloom.WebLibraryIntegration
{
	/// <summary>
	/// Currently pushes a book's metadata to Parse.com (a mongodb service) and files to Amazon S3.
	/// We are using both because Parse offers a more structured, query-able data organization
	/// that is useful for metadata, but does not allow large enough files for some of what we need.
	/// </summary>
	public class BookUpload
	{
		private BloomParseClient _parseClient;
		private BloomS3Client _s3Client;
		private readonly BookThumbNailer _thumbnailer;
		public IProgress Progress;

		private HashSet<string> _collectionFoldersUploaded;
		private int _newBooksUploaded;
		private int _booksUpdated;
		private int _booksSkipped;
		private int _booksWithErrors;

		//private const string UploadLogFilename = "BloomBulkUploadLog.txt";
		public const string UploadHashesFilename = ".lastUploadInfo";	// this filename must begin with a period

		// The full path of the log text file used to restart failed bulk uploads.
		private string _bulkUploadLogPath;

		static string _destination;

		private IProgressDialog _progressDialog;

		public BookUpload(BloomParseClient bloomParseClient, BloomS3Client bloomS3Client, BookThumbNailer htmlThumbnailer)
		{
			this._parseClient = bloomParseClient;
			this._s3Client = bloomS3Client;
			_thumbnailer = htmlThumbnailer;
		}

		/// <summary>
		/// Implicitly use the sandbox as the destination target.  Can be explicitly overridden
		/// on the command line in upload commands.  See <see cref="Destination"/>.
		/// </summary>
		internal static bool UseSandboxByDefault
		{
			get
			{
#if DEBUG
				return true;
#else
				var temp = Environment.GetEnvironmentVariable("BloomSandbox");
				if (string.IsNullOrWhiteSpace(temp))
					return false;
				temp = temp.ToLowerInvariant();
				return temp == "yes" || temp == "true" || temp == "y" || temp == "t";
#endif
			}
		}

		/// <summary>
		/// whereas we can *download* from anywhere regardless of production, debug, or unit test,
		/// or the environment variable "BloomSandbox", we currently only allow *uploading*
		/// to only one bucket depending on these things. This also does double duty for selecting
		/// the parse.com keys that are appropriate
		/// </summary>
		public static string UploadBucketNameForCurrentEnvironment
		{
			get
			{
				if(Program.RunningUnitTests)
				{
					return BloomS3Client.UnitTestBucketName;
				}
				return BookUpload.UseSandbox ? BloomS3Client.SandboxBucketName : BloomS3Client.ProductionBucketName;
			}
		}


		private void DisplayNetworkUploadProblem(Exception e, IProgress progress)
		{
			var msg1 = LocalizationManager.GetString("PublishTab.Upload.GenericUploadProblemNotice",
				"There was a problem uploading your book.");
			var msg2 = e.Message.Replace("{", "{{").Replace("}", "}}");
			progress.WriteError(msg1);
			progress.WriteError(msg2);
			progress.WriteVerbose(e.StackTrace);
		}
		

		private static Form ShellWindow
		{
			get { return Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell); }
		}


		public bool LogIn(string account, string password)
		{
			return _parseClient.LegacyLogIn(account, password);
		}

		public void Logout()
		{
			_parseClient.Logout();
		}

		public bool LoggedIn => _parseClient.LoggedIn;

		internal const string BloomS3UrlPrefix = "https://s3.amazonaws.com/";

		private string _uploadedBy;
		private string _accountWhenUploadedByLastSet;

		/// <summary>
		/// The string that should be used to indicate who is uploading books.
		/// When set, this is remembered until someone different logs in; when next
		/// retrieved, it resets to the new account.
		/// </summary>
		public string UploadedBy
		{
			get
			{
				if (_accountWhenUploadedByLastSet == _parseClient.Account)
					return _uploadedBy;
				// If a different login has since occurred, default to uploaded by that account.
				UploadedBy = _parseClient.Account;
				return _uploadedBy;
			}
			set
			{
				_accountWhenUploadedByLastSet = _parseClient.Account;
				_uploadedBy = value;
			}
		}

		/// <summary>
		/// The Parse.com object ID of the person who is uploading the book.
		/// </summary>
		public string UserId
		{
			get { return _parseClient.UserId; }
		}
		internal string BookOrderUrlOfLastUploadForUnitTest { get { return _s3Client.BookOrderUrlOfRecentUpload; } }

		/// <summary>
		/// Only for use in tests
		/// </summary>
		public string UploadBook(string bookFolder, IProgress progress)
		{
			string parseId;
			return UploadBook(bookFolder, progress, out parseId);
		}

		private string UploadBook(string bookFolder, IProgress progress, out string parseId,
			string pdfToInclude = null, ISet<string> audioFilesToInclude = null, IEnumerable<string> videoFilesToInclude = null, string[] languages = null,
			CollectionSettings collectionSettings = null)
		{
			// Books in the library should generally show as locked-down, so new users are automatically in localization mode.
			// Occasionally we may want to upload a new authoring template, that is, a 'book' that is suitableForMakingShells.
			// Such books must never be locked.
			// So, typically we will try to lock it. What we want to do is Book.RecordedAsLockedDown = true; Book.Save().
			// But all kinds of things have to be set up before we can create a Book. So we duplicate a few bits of code.
			var htmlFile = BookStorage.FindBookHtmlInFolder(bookFolder);
			bool wasLocked = false;
			bool allowLocking = false;
			HtmlDom domForLocking = null;
			var metaDataText = MetaDataText(bookFolder);
			var metadata = BookMetaData.FromString(metaDataText);
			if (!String.IsNullOrEmpty(htmlFile))
			{
				var xmlDomFromHtmlFile = XmlHtmlConverter.GetXmlDomFromHtmlFile(htmlFile, false);
				domForLocking = new HtmlDom(xmlDomFromHtmlFile);
				wasLocked = domForLocking.RecordedAsLockedDown;
				allowLocking = !metadata.IsSuitableForMakingShells;
				if (allowLocking && !wasLocked)
				{
					domForLocking.RecordAsLockedDown(true);
					XmlHtmlConverter.SaveDOMAsHtml5(domForLocking.RawDom, htmlFile);
				}
			}
			string s3BookId;
			try
			{
				// In case we somehow have a book with no ID, we must have one to upload it.
				if (String.IsNullOrEmpty(metadata.Id))
				{
					metadata.Id = Guid.NewGuid().ToString();
				}
				// And similarly it should have SOME title.
				if (String.IsNullOrEmpty(metadata.Title))
				{
					metadata.Title = Path.GetFileNameWithoutExtension(bookFolder);
				}
				metadata.SetUploader(UserId);
				s3BookId = S3BookId(metadata);
#if DEBUG
				// S3 URL can be reasonably deduced, as long as we have the S3 ID, so print that out in Debug mode.
				// Format: $"https://s3.amazonaws.com/BloomLibraryBooks{isSandbox}/{s3BookId}/{title}"
				// Example: https://s3.amazonaws.com/BloomLibraryBooks-Sandbox/jeffrey_su@sil.org/8d0d9043-a1bb-422d-aa5b-29726cdcd96a/AutoSplit+Timings
				var msgBookId = "s3BookId: " + s3BookId;
				progress.WriteMessage(msgBookId);
				Console.WriteLine(msgBookId);
#endif
				metadata.DownloadSource = s3BookId;
				// If the collection has a default bookshelf, make sure the book has that tag.
				// Also make sure it doesn't have any other bookshelf tags (which would typically be
				// from a previous default bookshelf upload), including a duplicate of the one
				// we may be about to add.
				var tags = (metadata.Tags?? new string[0]).Where(t => !t.StartsWith("bookshelf:"));

				if (!String.IsNullOrEmpty(collectionSettings?.DefaultBookshelf))
				{
					tags = tags.Concat(new [] {"bookshelf:" + collectionSettings.DefaultBookshelf});
				}
				metadata.Tags = tags.ToArray();

				// Any updated ID at least needs to become a permanent part of the book.
				// The file uploaded must also contain the correct DownloadSource data, so that it can be used
				// as an 'order' to download the book.
				// It simplifies unit testing if the metadata file is also updated with the uploadedBy value.
				// Not sure if there is any other reason to do it (or not do it).
				// For example, do we want to send/receive who is the latest person to upload?
				metadata.WriteToFolder(bookFolder);
				// The metadata is also a book order...but we need it on the server with the desired file name,
				// because we can't rename on download. The extension must be the one Bloom knows about,
				// and we want the file name to indicate which book, so use the name of the book folder.
				var metadataPath = BookMetaData.MetaDataPath(bookFolder);
				RobustFile.Copy(metadataPath, BookInfo.BookOrderPath(bookFolder), true);
				parseId = "";
				try
				{
					_s3Client.UploadBook(s3BookId, bookFolder, progress, pdfToInclude, audioFilesToInclude, videoFilesToInclude, languages);
					metadata.BaseUrl = _s3Client.BaseUrl;
					metadata.BookOrder = _s3Client.BookOrderUrlOfRecentUpload;
					var metaMsg = LocalizationManager.GetString("PublishTab.Upload.UploadingBookMetadata", "Uploading book metadata", "In this step, Bloom is uploading things like title, languages, and topic tags to the BloomLibrary.org database.");
					if (IsDryRun)
						metaMsg = "(Dry run) Would upload book metadata";	// TODO: localize?
					progress.WriteStatus(metaMsg);
					Console.WriteLine(metaMsg);
					// Do this after uploading the books, since the ThumbnailUrl is generated in the course of the upload.
					if (!IsDryRun)
					{
						var response = _parseClient.SetBookRecord(metadata.WebDataJson);
						parseId = response.ResponseUri.LocalPath;
						int index = parseId.LastIndexOf('/');
						parseId = parseId.Substring(index + 1);
						if (parseId == "books")
						{
							// For NEW books the response URL is useless...need to do a new query to get the ID.
							var json = _parseClient.GetSingleBookRecord(metadata.Id);
							parseId = json.objectId.Value;
						}
						//   if (!UseSandbox) // don't make it seem like there are more uploads than their really are if this a tester pushing to the sandbox
						{
							Analytics.Track("UploadBook-Success", new Dictionary<string, string>() { { "url", metadata.BookOrder }, { "title", metadata.Title } });
						}
					}
				}
				catch (WebException e)
				{
					DisplayNetworkUploadProblem(e, progress);
					if (IsProductionRun) // don't make it seem like there are more upload failures than their really are if this a tester pushing to the sandbox
						Analytics.Track("UploadBook-Failure", new Dictionary<string, string>() { { "url", metadata.BookOrder }, { "title", metadata.Title }, { "error", e.Message } });
					return "";
				}
				catch (AmazonS3Exception e)
				{
					if (e.Message.Contains("The difference between the request time and the current time is too large"))
					{
						progress.WriteError(LocalizationManager.GetString("PublishTab.Upload.TimeProblem",
							"There was a problem uploading your book. This is probably because your computer is set to use the wrong timezone or your system time is badly wrong. See http://www.di-mgt.com.au/wclock/help/wclo_setsysclock.html for how to fix this."));
						if (IsProductionRun)
							Analytics.Track("UploadBook-Failure-SystemTime");
					}
					else
					{
						DisplayNetworkUploadProblem(e, progress);
						if (IsProductionRun)
							// don't make it seem like there are more upload failures than there really are if this a tester pushing to the sandbox
							Analytics.Track("UploadBook-Failure",
								new Dictionary<string, string>() { { "url", metadata.BookOrder }, { "title", metadata.Title }, { "error", e.Message } });
					}
					return "";
				}
				catch (AmazonServiceException e)
				{
					DisplayNetworkUploadProblem(e, progress);
					if (IsProductionRun) // don't make it seem like there are more upload failures than there really are if this a tester pushing to the sandbox
						Analytics.Track("UploadBook-Failure", new Dictionary<string, string>() { { "url", metadata.BookOrder }, { "title", metadata.Title }, { "error", e.Message } });
					return "";
				}
				catch (Exception e)
				{
					var msg1 = LocalizationManager.GetString("PublishTab.Upload.UploadProblemNotice",
						"There was a problem uploading your book. You may need to restart Bloom or get technical help.");
					var msg2 = e.Message.Replace("{", "{{").Replace("}", "}}");
					progress.WriteError(msg1);
					progress.WriteError(msg2);
					progress.WriteVerbose(e.StackTrace);

					if (IsProductionRun) // don't make it seem like there are more upload failures than there really are if this a tester pushing to the sandbox
						Analytics.Track("UploadBook-Failure", new Dictionary<string, string>() { { "url", metadata.BookOrder }, { "title", metadata.Title }, { "error", e.Message } });
					return "";
				}
			}
			finally
			{
				if (domForLocking != null && allowLocking && !wasLocked)
				{
					domForLocking.RecordAsLockedDown(false);
					XmlHtmlConverter.SaveDOMAsHtml5(domForLocking.RawDom, htmlFile);
				}

			}
			return s3BookId;
		}

		/// <summary>
		/// The upload destination possibly set from the command line.  This must be set even before calling
		/// the constructor of this class because it is used in UploadBucketNameForCurrentEnvironment
		/// which is called by the BloomParseClient constructor.  And the constructor for this class has a
		/// BloomParseClient argument.
		/// </summary>
		/// <remarks>
		/// If not set explicitly before accessing, the destination is set according to <see cref="UseSandboxByDefault"/>.
		/// It can only be set once while the program is running.  Trying to change it will cause an
		/// exception to be thrown.
		/// </remarks>
		internal static string Destination
		{
			get
			{
				if (_destination == null)
					Destination = UseSandboxByDefault ? UploadDestination.Development : UploadDestination.Production;
				return _destination;
			}
			set
			{
				if (_destination == null && value != null)
					_destination = value;
				else if (_destination != value)
					throw new Exception("Cannot change upload destination after setting it!");
			}
		}

		/// <summary>
		/// Is this dry run (regardless of whether we're supposedly targetting the sandbox or production)?
		/// </summary>
		public static bool IsDryRun => Destination == UploadDestination.DryRun;

		/// <summary>
		/// Are we actually uploading to production (not a dry run)?
		/// </summary>
		public static bool IsProductionRun => Destination == UploadDestination.Production;

		/// <summary>
		/// Are we supposed to upload to the sandbox, either explicitly or by default?  (could be a dry run)
		/// </summary>
		public static bool UseSandbox
		{
			get
			{
				switch (Destination)
				{
					case UploadDestination.Development: return true;
					case UploadDestination.Production: return false;
					default: return UseSandboxByDefault;	// dry run
				}
			}
		}

		private static string MetaDataText(string bookFolder)
		{
			return RobustFile.ReadAllText(bookFolder.CombineForPath(BookInfo.MetaDataFileName));
		}

		private string S3BookId(BookMetaData metadata)
		{
			var s3BookId = _parseClient.Account + BloomS3Client.kDirectoryDelimeterForS3 + metadata.Id;
			return s3BookId;
		}

		public bool IsBookOnServer(string bookPath)
		{
			var metadata = BookMetaData.FromString(RobustFile.ReadAllText(bookPath.CombineForPath(BookInfo.MetaDataFileName)));
			return _parseClient.GetSingleBookRecord(metadata.Id) != null;
		}

		// Wait (up to three seconds) for data uploaded to become available.
		// Currently only used in unit testing.
		// I have no idea whether 3s is an adequate time to wait for 'eventual consistency'. So far it seems to work.
		internal void WaitUntilS3DataIsOnServer(string bucket, string bookPath)
		{
			var s3Id = S3BookId(BookMetaData.FromFolder(bookPath));
			// There's a few files we don't upload, but meta.bak is the only one that regularly messes up the count.
			// Some tests also deliberately include a _broken_ file to check they aren't uploaded,
			// so we'd better not wait for that to be there, either.
			var count = Directory.GetFiles(bookPath).Count(p=>!p.EndsWith(".bak") && !p.Contains(BookStorage.PrefixForCorruptHtmFiles));
			for (int i = 0; i < 30; i++)
			{
				var uploaded = _s3Client.GetBookFileCount(bucket, s3Id);
				if (uploaded >= count)
					return;
				Thread.Sleep(100);
			}
			throw new ApplicationException("S3 is very slow today");
		}


		internal bool CheckAgainstUploadedHashfile(string currentHashes, string bookFolder)
		{
			string uploadedHashes = null;
			try
			{
				if (!IsBookOnServer(bookFolder))
					return false;
				var bkInfo = new BookInfo(bookFolder, true);
				var s3id = S3BookId(bkInfo.MetaData);
				var key = s3id + BloomS3Client.kDirectoryDelimeterForS3 + Path.GetFileName(bookFolder) + BloomS3Client.kDirectoryDelimeterForS3 + UploadHashesFilename;
				uploadedHashes = _s3Client.DownloadFile(UseSandbox ? BloomS3Client.SandboxBucketName : BloomS3Client.ProductionBucketName, key);
			}
			catch
			{
				uploadedHashes = "";	// probably file doesn't exist because it hasn't yet been uploaded
			}
			return currentHashes == uploadedHashes;
		}

		internal bool CheckAgainstLocalHashfile(string currentHashes, string uploadInfoPath)
		{
			if (RobustFile.Exists(uploadInfoPath))
			{
				var previousHashes = RobustFile.ReadAllText(uploadInfoPath);
				return currentHashes == previousHashes;
			}
			return false;
		}


		/// <summary>
		/// If we do not have enterprise enabled, copy the book and remove all enterprise level features.
		/// </summary>
		internal static bool PrepareBookForUpload(ref Book.Book book, BookServer bookServer, string tempFolderPath, IProgress progress)
		{
			if (book.CollectionSettings.HaveEnterpriseFeatures)
				return false;

			// We need to be sure that any in-memory changes have been written to disk
			// before we start copying/loading the new book to/from disk
			book.Save();

			Directory.CreateDirectory(tempFolderPath);
			BookStorage.CopyDirectory(book.FolderPath, tempFolderPath);
			var bookInfo = new BookInfo(tempFolderPath, true);
			var copiedBook = bookServer.GetBookFromBookInfo(bookInfo);
			copiedBook.BringBookUpToDate(new NullProgress(), true);
			var pages = new List<XmlElement>();
			foreach (XmlElement page in copiedBook.GetPageElements())
				pages.Add(page);
			ISet<string> warningMessages = new HashSet<string>();
			PublishHelper.RemoveEnterpriseFeaturesIfNeeded(copiedBook, pages, warningMessages);
			PublishHelper.SendBatchedWarningMessagesToProgress(warningMessages, progress);
			copiedBook.Save();
			copiedBook.Storage.UpdateSupportFiles();
			book = copiedBook;
			return true;

		}

		/// <summary>
		/// Common routine used in normal upload and bulk upload.
		/// </summary>
		internal string FullUpload(Book.Book book, IProgress progress , PublishView publishView, BookUploadParameters bookParams, out string parseId)
		{
			book.Storage.CleanupUnusedSupportFiles(isForPublish:false); // we are publishing, but this is the real folder not a copy, so play safe.
			var bookFolder = book.FolderPath;
			parseId = ""; // in case of early return
			// Set this in the metadata so it gets uploaded. Do this in the background task as it can take some time.
			// These bits of data can't easily be set while saving the book because we save one page at a time
			// and they apply to the book as a whole.
			book.BookInfo.LanguageTableReferences = _parseClient.GetLanguagePointers(book.BookData.MakeLanguageUploadData(bookParams.LanguagesToUpload));
			book.BookInfo.PageCount = book.GetPages().Count();
			book.BookInfo.Save();
			// If the caller wants to preserve existing thumbnails, recreate them only if one or more of them do not exist.
			var thumbnailsExist = File.Exists(Path.Combine(bookFolder, "thumbnail-70.png")) &&
				File.Exists(Path.Combine(bookFolder, "thumbnail-256.png")) &&
				File.Exists(Path.Combine(bookFolder, "thumbnail.png"));
			if (!bookParams.PreserveThumbnails || !thumbnailsExist)
			{
				var thumbnailMsg = LocalizationManager.GetString("PublishTab.Upload.MakingThumbnail", "Making thumbnail image...");
				progress.WriteStatus(thumbnailMsg);
				//the largest thumbnail I found on Amazon was 300px high. Prathambooks.org about the same.
				_thumbnailer.MakeThumbnailOfCover(book, 70); // this is a sacrificial one to prime the pump, to fix BL-2673
				_thumbnailer.MakeThumbnailOfCover(book, 70);
				if (progress.CancelRequested)
					return "";
				_thumbnailer.MakeThumbnailOfCover(book, 256);
				if (progress.CancelRequested)
					return "";

				// It is possible the user never went back to the Collection tab after creating/updating the book, in which case
				// the 'normal' thumbnail never got created/updating. See http://issues.bloomlibrary.org/youtrack/issue/BL-3469.
				_thumbnailer.MakeThumbnailOfCover(book);
				if (progress.CancelRequested)
					return "";
			}
			var uploadPdfPath = UploadPdfPath(bookFolder);
			// If there is not already a locked preview in the book folder
			// (which we take to mean the user has created a customized one that he prefers),
			// make sure we have a current correct preview and then copy it to the book folder so it gets uploaded.
			if (!FileHelper.IsLocked(uploadPdfPath))
			{
				var pdfMsg = LocalizationManager.GetString("PublishTab.Upload.MakingPdf", "Making PDF Preview...");
				progress.WriteStatus(pdfMsg);
				
				publishView.MakePDFForUpload(progress);
				if (RobustFile.Exists(publishView.PdfPreviewPath))
				{
					RobustFile.Copy(publishView.PdfPreviewPath, uploadPdfPath, true);
				}
				else
				{
					return "";		// no PDF, no upload (See BL-6719)
				}
			}
			if (progress.CancelRequested)
				return "";

			return UploadBook(bookFolder, progress, out parseId, Path.GetFileName(uploadPdfPath),
				GetAudioFilesToInclude(book, bookParams.ExcludeNarrationAudio, bookParams.ExcludeMusic), GetVideoFilesToInclude(book),
				bookParams.LanguagesToUpload, book.CollectionSettings);
		}

		/// <summary>
		/// Figure out if any video files are unused in this book, in case we haven't had them stripped out by opening
		/// the saved book yet (when BookStorage will do it for us).
		/// </summary>
		/// <param name="book"></param>
		/// <returns></returns>
		internal static IEnumerable<string> GetVideoFilesToInclude(Book.Book book)
		{
			return BookStorage.GetVideoPathsRelativeToBook(book.RawDom.DocumentElement);
		}

		/// <summary>
		/// Conditionally exclude .mp3 files for narration and music.
		/// Always exclude .wav files for narration.
		/// </summary>
		private static ISet<string> GetAudioFilesToInclude(Book.Book book, bool excludeNarrationAudio, bool excludeMusic)
		{
			HashSet<string> result = new HashSet<string>();
			if (!excludeNarrationAudio)
				result.AddRange(book.Storage.GetNarrationAudioFileNamesReferencedInBook(false));
			if (!excludeMusic)
				result.AddRange(book.Storage.GetBackgroundMusicFileNamesReferencedInBook());
			return result;
		}

		internal static string UploadPdfPath(string bookFolder)
		{
			// Do NOT use ChangeExtension here. If the folder name has a period (e.g.: "Look at the sky. What do you see")
			// ChangeExtension will strip of the last sentence, which is not what we want (and not what BloomLibrary expects).
			return Path.Combine(bookFolder, Path.GetFileName(bookFolder) + ".pdf");
		}

		internal bool IsThisVersionAllowedToUpload()
		{
			return _parseClient.IsThisVersionAllowedToUpload();
		}

		/// <summary>
		/// In the past we've had problems with users copying folders manually and creating derivative books with
		/// the same bookInstanceId guids. Then we try to bulk upload a folder structure with books like this and the
		/// duplicates overwrite whichever book got uploaded first.
		/// This method recurses through the folders under 'rootFolderPath' and keeps track of all the unique bookInstanceId
		/// guids. When a duplicate is found, we will call BookInfo.InstallFreshInstanceGuid().
		/// </summary>
		/// <remarks>Internal for testing.</remarks>
		/// <param name="rootFolderPath"></param>
		internal static void BulkRepairInstanceIds(string rootFolderPath)
		{
			BookInfo.RepairDuplicateInstanceIds(rootFolderPath);
		}

		public static string HashBookFolder(string directory)
		{
			var bldr = new StringBuilder();
			// Start file with the Bloom version.
			var assembly = Assembly.GetExecutingAssembly();
			bldr.AppendLineFormat("{0} Version {1} [{2}]", assembly.GetName().Name, assembly.GetName().Version,
				UseSandbox ? BloomS3Client.SandboxBucketName : BloomS3Client.ProductionBucketName);
			Debug.Assert(Directory.Exists(directory));
			var dirInfo = new DirectoryInfo(directory);
			var htmFiles = dirInfo.GetFiles("*.htm", SearchOption.TopDirectoryOnly);
			Debug.Assert(htmFiles.Length == 1);
			var htmContent = RobustFile.ReadAllText(htmFiles[0].FullName);
			var hash = Book.Book.MakeVersionCode(htmContent, htmFiles[0].FullName);
			bldr.AppendLineFormat("{0} {1}", htmFiles[0].Name, hash);
			return bldr.ToString().Replace(Environment.NewLine,"\r\n");	// cross-platform line endings for this file
		}
	}

	public class BookUploadParameters
	{
		public string Folder;
		public bool ExcludeNarrationAudio;
		public bool ExcludeMusic;
		public bool PreserveThumbnails;
		public bool ForceUpload;
		public string[] LanguagesToUpload;

		public BookUploadParameters()
		{
		}

		public BookUploadParameters(UploadParameters options)
		{
			Folder = options.Path;
			ExcludeNarrationAudio = options.ExcludeNarrationAudio;
			ExcludeMusic = options.ExcludeMusicAudio;
			PreserveThumbnails = options.PreserveThumbnails;
			ForceUpload = options.ForceUpload;
		}
	}
}
