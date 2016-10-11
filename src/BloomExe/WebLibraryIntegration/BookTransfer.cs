using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Web;
using System.Windows.Forms;
using Amazon.Runtime;
using Amazon.S3;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Properties;
using Bloom.Publish;
using DesktopAnalytics;
using L10NSharp;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;
using SIL.Windows.Forms.Progress;

namespace Bloom.WebLibraryIntegration
{
	/// <summary>
	/// Currently pushes a book's metadata to Parse.com (a mongodb service) and files to Amazon S3.
	/// We are using both because Parse offers a more structured, query-able data organization
	/// that is useful for metadata, but does not allow large enough files for some of what we need.
	/// </summary>
	public class BookTransfer
	{
		private BloomParseClient _parseClient;
		private BloomS3Client _s3Client;
		private readonly BookThumbNailer _thumbnailer;
		private readonly BookDownloadStartingEvent _bookDownloadStartingEvent;

		public event EventHandler<BookDownloadedEventArgs> BookDownLoaded;

		public BookTransfer(BloomParseClient bloomParseClient, BloomS3Client bloomS3Client, BookThumbNailer htmlThumbnailer, BookDownloadStartingEvent bookDownloadStartingEvent)
		{
			this._parseClient = bloomParseClient;
			this._s3Client = bloomS3Client;
			_thumbnailer = htmlThumbnailer;
			_bookDownloadStartingEvent = bookDownloadStartingEvent;
		}

		public string LastBookDownloadedPath { get; set; }

		public static bool UseSandbox
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
					
				return BookTransfer.UseSandbox ? BloomS3Client.SandboxBucketName : BloomS3Client.ProductionBucketName; 
			}
		}

		/// <summary>
		/// Download a book
		/// </summary>
		/// <param name="orderUrl">bloom://localhost/order?orderFile=BloomLibraryBooks-UnitTests/unittest@example.com/a211f07b-2c9f-4b97-b0b1-71eb24fdbed79887cda9_bb1d_4422_aa07_bc8c19285ca9/My Url Book/My Url Book.BloomBookOrder</param>
		/// <param name="destPath"></param>
		/// <param name="title"></param>
		/// <returns></returns>
		public string DownloadFromOrderUrl(string orderUrl, string destPath, string title = "unknown")
		{
			var uri = new Uri(orderUrl);
			var order  = HttpUtility.ParseQueryString(uri.Query)["orderFile"];
			IEnumerable<string> parts = order.Split(new char[] {'/'});
			string bucket = parts.First();
			var s3OrderKey = string.Join("/",parts.Skip(1));

			string url = "unknown";
			try
			{
				GetUrlAndTitle(bucket,s3OrderKey, ref url, ref title);
				if (_progressDialog != null)
					_progressDialog.Invoke((Action) (() => { _progressDialog.Progress = 1; }));
				// downloading the metadata is considered step 1.
				var destinationPath = DownloadBook(bucket, url, destPath);
				LastBookDownloadedPath = destinationPath;

				Analytics.Track("DownloadedBook-Success",
					new Dictionary<string, string>() {{"url", url}, {"title", title}});
				return destinationPath;
			}
			catch (Exception e)
			{
				try
				{
					// We want to try this before we give a report that may terminate the program. But if something
					// more goes wrong, ignore it.
					Analytics.Track("DownloadedBook-Failure",
						new Dictionary<string, string>() { { "url", url }, { "title", title } });
					Analytics.ReportException(e);
				}
				catch (Exception)
				{
				}
				var message = LocalizationManager.GetString("Download.ProblemNotice",
					"There was a problem downloading your book. You may need to restart Bloom or get technical help.");
				// BL-1233, we've seen what appear to be timeout exceptions, can't confirm the actual Exception subclass though.
				// It's likely that S3 wraps the original TimeoutException from .net with its own AmazonServiceException.
				if (e is TimeoutException || e.InnerException is TimeoutException)
					message = LocalizationManager.GetString("Download.TimeoutProblemNotice",
					"There was a problem downloading the book: something took too long. You can try again at a different time, or write to us at issues@bloomlibrary.org if you cannot get the download to work from your location.");
				if (e is AmazonServiceException || e is WebException) // Network problems, not an internal error, less alarming message called for
					message = LocalizationManager.GetString("Download.GenericNetworkProblemNotice",
						"There was a problem downloading the book.  You can try again at a different time, or write to us at issues@bloomlibrary.org if you cannot get the download to work from your location.");
				DisplayProblem(e, message);
				return "";
			}
		}

		private void GetUrlAndTitle(string bucket, string s3orderKey, ref string url, ref string title)
		{
			int index = s3orderKey.IndexOf('/');
			if (index > 0)
				index = s3orderKey.IndexOf('/', index + 1); // second slash
			if (index > 0)
				url = s3orderKey.Substring(0,index);
			if (url == "unknown" || string.IsNullOrWhiteSpace(title) || title == "unknown")
			{
				// not getting the info we want in the expected way. This old algorithm may work.
				var metadata = BookMetaData.FromString(_s3Client.DownloadFile(s3orderKey, bucket));
				url = metadata.DownloadSource;
				title = metadata.Title;
			}
		}

		private static void DisplayProblem(Exception e, string message)
		{
			var action = new Action(() => SIL.Reporting.ErrorReport.NotifyUserOfProblem(e, message));
				var shellWindow = ShellWindow;
				if (shellWindow != null)
					shellWindow.Invoke(action);
				else
					action.Invoke();
		}

		private static void DisplayNetworkUploadProblem(Exception e, IProgress progress)
		{
			progress.WriteError(LocalizationManager.GetString("PublishTab.Upload.GenericUploadProblemNotice",
				"There was a problem uploading your book."));
			progress.WriteError(e.Message.Replace("{", "{{").Replace("}", "}}"));
			progress.WriteVerbose(e.StackTrace);
		}

		public static string DownloadFolder
		{
			get
			{
				return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
					.CombineForPath(ProjectContext.GetInstalledCollectionsDirectory(), BookCollection.DownloadedBooksCollectionNameInEnglish);
			}
		}

		private IProgressDialog _progressDialog;
		private string _downloadRequest;

		internal void HandleBloomBookOrder(string order)
		{
			_downloadRequest = order;
			using (var progressDialog = new ProgressDialog())
			{
				_progressDialog = new ProgressDialogWrapper(progressDialog);
				progressDialog.CanCancel = false; // one day we may allow this...
				progressDialog.Overview = LocalizationManager.GetString("Download.DownloadingDialogTitle", "Downloading book");
				progressDialog.ProgressRangeMaximum = 14; // a somewhat minimal file count. We will fine-tune it when we know.
				if (IsUrlOrder(order))
				{
					var link = new BloomLinkArgs(order);
					progressDialog.StatusText = link.Title;
				}
				else
				{
					progressDialog.StatusText = Path.GetFileNameWithoutExtension(order);
				}

				// We must do the download in a background thread, even though the whole process is doing nothing else,
				// so we can invoke stuff on the main thread to (e.g.) update the progress bar.
				BackgroundWorker worker = new BackgroundWorker();
				worker.DoWork += OnDoDownload;
				progressDialog.BackgroundWorker = worker;
				//dlg.CancelRequested += new EventHandler(OnCancelRequested);
				progressDialog.ShowDialog(); // hidden automatically when task completes
				if (progressDialog.ProgressStateResult != null &&
					progressDialog.ProgressStateResult.ExceptionThatWasEncountered != null)
				{
					SIL.Reporting.ErrorReport.ReportFatalException(
						progressDialog.ProgressStateResult.ExceptionThatWasEncountered);
				}
			}
		}

		/// <summary>
		/// url is typically something like https://s3.amazonaws.com/BloomLibraryBooks/somebody@example.com/0a2745dd-ca98-47ea-8ba4-2cabc67022e
		/// It is harmless if there are more elements in it (e.g. address to a particular file in the folder)
		/// Note: if you copy the url from part of the link to a file in the folder from AWS,
		/// you typically need to change %40 to @ in the uploader's email.
		/// </summary>
		/// <param name="url"></param>
		/// <param name="destRoot"></param>
		internal string HandleDownloadWithoutProgress(string url, string destRoot)
		{
			_progressDialog = new ConsoleProgress();
			if (!url.StartsWith(BloomS3UrlPrefix))
			{
				Console.WriteLine("Url unexpectedly does not start with https://s3.amazonaws.com/");
				return "";
			}
			var bookOrder = url.Substring(BloomS3UrlPrefix.Length);
			var index = bookOrder.IndexOf('/');
			var bucket = bookOrder.Substring(0, index);
			var folder = bookOrder.Substring(index + 1);

			return DownloadBook(bucket, folder, destRoot);
		}

		/// <summary>
		/// this runs in a worker thread
		/// </summary>
		private void OnDoDownload(object sender, DoWorkEventArgs args)
		{
			// If we are passed a bloom book order URL, download the corresponding book and open it.
			if (IsUrlOrder(_downloadRequest))
			{
				var link = new BloomLinkArgs(_downloadRequest);
				DownloadFromOrderUrl(_downloadRequest, DownloadFolder, link.Title);
			}
				// If we are passed a bloom book order, download the corresponding book and open it.
			else if (_downloadRequest.ToLowerInvariant().EndsWith(BookTransfer.BookOrderExtension.ToLowerInvariant()) &&
					 RobustFile.Exists(_downloadRequest))
			{
				HandleBookOrder(_downloadRequest);
			}
		}

		private static Form ShellWindow
		{
			get { return Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell); }
		}

		private static bool IsUrlOrder(string argument)
		{
			return argument.ToLowerInvariant().StartsWith(BloomLinkArgs.kBloomUrlPrefix);
		}

		private void HandleBookOrder(string bookOrderPath)
		{
			HandleBookOrder(bookOrderPath, DownloadFolder);
		}


		public bool LogIn(string account, string password)
		{
			return _parseClient.LogIn(account, password);
		}

		public void Logout()
		{
			_parseClient.Logout();
		}

		public bool LoggedIn
		{
			get { return _parseClient.LoggedIn; }
		}

		public const string BookOrderExtension = ".BloomBookOrder";
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

		public string UserId
		{
			get { return _parseClient.UserId; }
		}

		public string UploadBook(string bookFolder, IProgress progress)
		{
			string parseId;
			return UploadBook(bookFolder, progress, out parseId);
		}

		public string UploadBook(string bookFolder, IProgress progress, out string parseId, string pdfToInclude = null)
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
			if (!string.IsNullOrEmpty(htmlFile))
			{
				var xmlDomFromHtmlFile = XmlHtmlConverter.GetXmlDomFromHtmlFile(htmlFile, false);
				domForLocking = new HtmlDom(xmlDomFromHtmlFile);
				wasLocked = Book.Book.HtmlHasLockedDownFlag(domForLocking);
				allowLocking = !metadata.IsSuitableForMakingShells;
				if (allowLocking && !wasLocked)
				{
					Book.Book.RecordAsLockedDown(domForLocking, true);
					XmlHtmlConverter.SaveDOMAsHtml5(domForLocking.RawDom, htmlFile);
				}
			}
			string s3BookId;
			try
			{
				// In case we somehow have a book with no ID, we must have one to upload it.
				if (string.IsNullOrEmpty(metadata.Id))
				{
					metadata.Id = Guid.NewGuid().ToString();
				}
				// And similarly it should have SOME title.
				if (string.IsNullOrEmpty(metadata.Title))
				{
					metadata.Title = Path.GetFileNameWithoutExtension(bookFolder);
				}
				metadata.SetUploader(UserId);
				s3BookId = S3BookId(metadata);
				metadata.DownloadSource = s3BookId;
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
				var orderPath = Path.Combine(bookFolder, Path.GetFileName(bookFolder) + BookOrderExtension);
				RobustFile.Copy(metadataPath, orderPath, true);
				parseId = "";
				try
				{
					_s3Client.UploadBook(s3BookId, bookFolder, progress, pdfToInclude: pdfToInclude);
					metadata.BaseUrl = _s3Client.BaseUrl;
					metadata.BookOrder = _s3Client.BookOrderUrlOfRecentUpload;
					progress.WriteStatus(LocalizationManager.GetString("PublishTab.Upload.UploadingBookMetadata", "Uploading book metadata", "In this step, Bloom is uploading things like title, languages, & topic tags to the bloomlibrary.org database."));
					// Do this after uploading the books, since the ThumbnailUrl is generated in the course of the upload.
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
				catch (WebException e)
				{
					DisplayNetworkUploadProblem(e, progress);
					if (!UseSandbox) // don't make it seem like there are more upload failures than their really are if this a tester pushing to the sandbox
						Analytics.Track("UploadBook-Failure", new Dictionary<string, string>() { { "url", metadata.BookOrder }, { "title", metadata.Title }, { "error", e.Message } });
					return "";
				}
				catch (AmazonS3Exception e)
				{
					if (e.Message.Contains("The difference between the request time and the current time is too large"))
					{
						progress.WriteError(LocalizationManager.GetString("PublishTab.Upload.TimeProblem",
							"There was a problem uploading your book. This is probably because your computer is set to use the wrong timezone or your system time is badly wrong. See http://www.di-mgt.com.au/wclock/help/wclo_setsysclock.html for how to fix this."));
						if (!UseSandbox)
							Analytics.Track("UploadBook-Failure-SystemTime");
					}
					else
					{
						DisplayNetworkUploadProblem(e, progress);
						if (!UseSandbox)
							// don't make it seem like there are more upload failures than their really are if this a tester pushing to the sandbox
							Analytics.Track("UploadBook-Failure",
								new Dictionary<string, string>() { { "url", metadata.BookOrder }, { "title", metadata.Title }, { "error", e.Message } });
					}
					return "";
				}
				catch (AmazonServiceException e)
				{
					DisplayNetworkUploadProblem(e, progress);
					if (!UseSandbox) // don't make it seem like there are more upload failures than their really are if this a tester pushing to the sandbox
						Analytics.Track("UploadBook-Failure", new Dictionary<string, string>() { { "url", metadata.BookOrder }, { "title", metadata.Title }, { "error", e.Message } });
					return "";
				}
				catch (Exception e)
				{
					progress.WriteError(LocalizationManager.GetString("PublishTab.Upload.UploadProblemNotice",
						"There was a problem uploading your book. You may need to restart Bloom or get technical help."));
					progress.WriteError(e.Message.Replace("{", "{{").Replace("}", "}}"));
					progress.WriteVerbose(e.StackTrace);
					if (!UseSandbox) // don't make it seem like there are more upload failures than their really are if this a tester pushing to the sandbox
						Analytics.Track("UploadBook-Failure", new Dictionary<string, string>() {{"url", metadata.BookOrder}, {"title", metadata.Title}, {"error", e.Message}});
					return "";
				}
			}
			finally
			{
				if (domForLocking != null && allowLocking && !wasLocked)
				{
					Book.Book.RecordAsLockedDown(domForLocking, false);
					XmlHtmlConverter.SaveDOMAsHtml5(domForLocking.RawDom, htmlFile);
				}

			}
			return s3BookId;
		}

		internal string BookOrderUrl {get { return _s3Client.BookOrderUrlOfRecentUpload; }}

		private static string MetaDataText(string bookFolder)
		{
			return RobustFile.ReadAllText(bookFolder.CombineForPath(BookInfo.MetaDataFileName));
		}

		private string S3BookId(BookMetaData metadata)
		{
			// It's tempting to use '/' so that S3 tools will treat all the books with the same ID as a folder.
			// But this complicates things because that character is taken as a path separator (even in Windows),
			// which gives us an extra level of folder in our temp folder...too much trouble for now, anyway.
			// So use a different separator.
			var s3BookId = _parseClient.Account + "/" + metadata.Id;
			return s3BookId;
		}

		/// <summary>
		/// Internal for testing because it's not yet clear this is the appropriate public routine.
		/// Probably some API gets a list of BloomInfo objects from the parse.com data, and we pass one of
		/// them as the argument for the public method.
		/// </summary>
		/// <param name="bucket"></param>
		/// <param name="s3BookId"></param>
		/// <param name="dest"></param>
		/// <returns></returns>
		internal string DownloadBook(string bucket, string s3BookId, string dest)
		{
			var destinationPath = _s3Client.DownloadBook(bucket, s3BookId, dest, _progressDialog);
			if (BookDownLoaded != null)
			{
				var bookInfo = new BookInfo(destinationPath, false); // A downloaded book is a template, so never editable.
				BookDownLoaded(this, new BookDownloadedEventArgs() {BookDetails = bookInfo});
			}
			// Books in the library should generally show as locked-down, so new users are automatically in localization mode.
			// Occasionally we may want to upload a new authoring template, that is, a 'book' that is suitableForMakingShells.
			// Such books should not be locked down.
			// So, we try to lock it. What we want to do is Book.RecordedAsLockedDown = true; Book.Save().
			// But all kinds of things have to be set up before we can create a Book. So we duplicate a few bits of code.
			var htmlFile = BookStorage.FindBookHtmlInFolder(destinationPath);
			if (htmlFile == "")
				return destinationPath; //argh! we can't lock it.
			var xmlDomFromHtmlFile = XmlHtmlConverter.GetXmlDomFromHtmlFile(htmlFile, false);
			var dom = new HtmlDom(xmlDomFromHtmlFile);
			if (!BookMetaData.FromString(MetaDataText(destinationPath)).IsSuitableForMakingShells)
			{
				Book.Book.RecordAsLockedDown(dom, true);
				XmlHtmlConverter.SaveDOMAsHtml5(dom.RawDom, htmlFile);
			}

			return destinationPath;
		}

		public void HandleBookOrder(string bookOrderPath, string projectPath)
		{
			var metadata = BookMetaData.FromString(RobustFile.ReadAllText(bookOrderPath));
			var s3BookId = metadata.DownloadSource;
			var bucket = BloomS3Client.ProductionBucketName; //TODO
			_s3Client.DownloadBook(bucket, s3BookId, Path.GetDirectoryName(projectPath));
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
			var count = Directory.GetFiles(bookPath).Length;
			for (int i = 0; i < 30; i++)
			{
				var uploaded = _s3Client.GetBookFileCount(bucket, s3Id);
				if (uploaded >= count)
					return;
				Thread.Sleep(100);
			}
			throw new ApplicationException("S3 is very slow today");
		}

		/// <summary>
		/// Upload bloom books in the specified folder to the bloom library.
		/// Folders that contain exactly one .htm file are interpreted as books and uploaded.
		/// Other folders are searched recursively for children that appear to be bloom books.
		/// The parent folder of a bloom book is searched for a .bloomContainer file and, if one is found,
		/// the book is treated as part of that collection (e.g., for determining vernacular language).
		/// If no collection is found there it uses whatever collection was last open, or the current default.
		/// </summary>
		/// <param name="folder"></param>
		public void UploadFolder(string folder, ApplicationContainer container)
		{
			if (!LogIn(Settings.Default.WebUserId, Settings.Default.WebPassword))
			{
				SIL.Reporting.ErrorReport.NotifyUserOfProblem("Could not log you in using user='" + Settings.Default.WebUserId + "' and pwd='" + Settings.Default.WebPassword+"'."+System.Environment.NewLine+
					"For some reason, from the command line, we cannot get these credentials out of Settings.Default. However if you place your command line arguments in the properties of the project in visual studio and run from there, it works. If you are already doing that and get this message, then try running Bloom normally (gui), go to publish, and make sure you are logged in. Then quit and try this again.");
				return;
			}
			using (var dlg = new BulkUploadProgressDlg())
			{
				var worker = new BackgroundWorker();
				worker.DoWork += BackgroundUpload;
				worker.RunWorkerCompleted += (sender, args) =>
				{
					dlg.Close();
				};
				worker.RunWorkerAsync(new object[] { folder, dlg, container });
				dlg.ShowDialog(); // waits until worker completed closes it.
			}
		}

		/// <summary>
		/// Worker function for a background thread task. See first lines for required args passed to RunWorkerAsync, which triggers this.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="doWorkEventArgs"></param>
		private void BackgroundUpload(object sender, DoWorkEventArgs doWorkEventArgs)
		{
			var args = (object[]) doWorkEventArgs.Argument;
			var folder = (string) args[0];
			var dlg = (BulkUploadProgressDlg) args[1];
			var appContext = (ApplicationContainer)args[2];
			ProjectContext context = null; // Expensive to create; hold each one we make until we find a book that needs a different one.
			try
			{
				UploadInternal(folder, dlg, appContext, ref context);
			}
			finally
			{
				if (context != null)
					context.Dispose();
			}
		}

		/// <summary>
		/// Handles the recursion through directories: if a folder looks like a Bloom book upload it; otherwise, try its children.
		/// Invisible folders like .hg are ignored.
		/// </summary>
		/// <param name="folder"></param>
		/// <param name="dlg"></param>
		/// <param name="container"></param>
		/// <param name="context"></param>
		private void UploadInternal(string folder, BulkUploadProgressDlg dlg, ApplicationContainer container, ref ProjectContext context)
		{
			if (Path.GetFileName(folder).StartsWith("."))
				return; // secret folder, probably .hg

			if (Directory.GetFiles(folder, "*.htm").Count() == 1)
			{
				// Exactly one htm file, assume this is a bloom book folder.
				dlg.Progress.WriteMessage("Starting to upload " + folder);

				// Make sure the files we want to upload are up to date.
				// Unfortunately this requires making a book object, which requires making a ProjectContext, which must be created with the
				// proper parent book collection if possible.
				var parent = Path.GetDirectoryName(folder);
				var collectionPath = Directory.GetFiles(parent, "*.bloomCollection").FirstOrDefault();
				if (collectionPath == null && context == null)
				{
					collectionPath = Settings.Default.MruProjects.Latest;
				}
				if (context == null || context.SettingsPath != collectionPath)
				{
					if (context != null)
						context.Dispose();
					// optimise: creating a context seems to be quite expensive. Probably the only thing we need to change is
					// the collection. If we could update that in place...despite autofac being told it has lifetime scope...we would save some time.
					// Note however that it's not good enough to just store it in the project context. The one that is actually in
					// the autofac object (_scope in the ProjectContext) is used by autofac to create various objects, in particular, books.
					context = container.CreateProjectContext(collectionPath);
				}
				var server = context.BookServer;
				var book = server.GetBookFromBookInfo(new BookInfo(folder, true));
				book.BringBookUpToDate(new NullProgress());

				// Assemble the various arguments needed to make the objects normally involved in an upload.
				// We leave some constructor arguments not actually needed for this purpose null.
				var bookSelection = new BookSelection();
				bookSelection.SelectBook(book);
				var currentEditableCollectionSelection = new CurrentEditableCollectionSelection();
				if (collectionPath != null)
				{
					var collection = new BookCollection(collectionPath, BookCollection.CollectionType.SourceCollection,
						bookSelection);
					currentEditableCollectionSelection.SelectCollection(collection);
				}
				var publishModel = new PublishModel(bookSelection, new PdfMaker(), currentEditableCollectionSelection, null, server, _thumbnailer, null);
				publishModel.PageLayout = book.GetLayout();
				var view = new PublishView(publishModel, new SelectedTabChangedEvent(), new LocalizationChangedEvent(), this, null, null);
				string dummy;
				// Normally we let the user choose which languages to upload. Here, just the ones that have complete information.
				var langDict = book.AllLanguages;
				var languagesToUpload = langDict.Keys.Where(l => langDict[l]).ToArray();
				if (languagesToUpload.Any())
					FullUpload(book, dlg.Progress, view, languagesToUpload, out dummy, dlg);
				return;
			}
			foreach (var sub in Directory.GetDirectories(folder))
				UploadInternal(sub, dlg, container, ref context);
		}

		/// <summary>
		/// Common routine used in normal upload and bulk upload.
		/// </summary>
		/// <param name="book"></param>
		/// <param name="progressBox"></param>
		/// <param name="publishView"></param>
		/// <param name="parseId"></param>
		/// <param name="languages"></param>
		/// <param name="invokeTarget"></param>
		/// <returns></returns>
		internal string FullUpload(Book.Book book, LogBox progressBox, PublishView publishView, string[] languages, out string parseId, Form invokeTarget = null)
		{
			var bookFolder = book.FolderPath;
			parseId = ""; // in case of early return
			// Set this in the metadata so it gets uploaded. Do this in the background task as it can take some time.
			// These bits of data can't easily be set while saving the book because we save one page at a time
			// and they apply to the book as a whole.
			book.BookInfo.LanguageTableReferences = _parseClient.GetLanguagePointers(book.CollectionSettings.MakeLanguageUploadData(languages));
			book.BookInfo.PageCount = book.GetPages().Count();
			book.BookInfo.Save();
			progressBox.WriteStatus(LocalizationManager.GetString("PublishTab.Upload.MakingThumbnail", "Making thumbnail image..."));
			//the largest thumbnail I found on Amazon was 300px high. Prathambooks.org about the same.
			_thumbnailer.MakeThumbnailOfCover(book, 70, invokeTarget); // this is a sacrificial one to prime the pump, to fix BL-2673
			_thumbnailer.MakeThumbnailOfCover(book, 70, invokeTarget);
			if (progressBox.CancelRequested)
				return "";
			_thumbnailer.MakeThumbnailOfCover(book, 256, invokeTarget);
			if (progressBox.CancelRequested)
				return "";
			var uploadPdfPath = UploadPdfPath(bookFolder);
			// If there is not already a locked preview in the book folder
			// (which we take to mean the user has created a customized one that he prefers),
			// make sure we have a current correct preview and then copy it to the book folder so it gets uploaded.
			if (!FileUtils.IsFileLocked(uploadPdfPath))
			{
				progressBox.WriteStatus(LocalizationManager.GetString("PublishTab.Upload.MakingPdf", "Making PDF Preview..."));
				publishView.MakePublishPreview();
				if (RobustFile.Exists(publishView.PdfPreviewPath))
				{
					RobustFile.Copy(publishView.PdfPreviewPath, uploadPdfPath, true);
				}
			}
			if (progressBox.CancelRequested)
				return "";
			return UploadBook(bookFolder, progressBox, out parseId, Path.GetFileName(uploadPdfPath));
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
	}
}
