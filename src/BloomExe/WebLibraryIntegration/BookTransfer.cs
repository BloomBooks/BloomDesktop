using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Amazon.Runtime;
using Autofac.Features.Metadata;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Properties;
using Bloom.Publish;
using DesktopAnalytics;
using L10NSharp;
using Palaso.Extensions;
using Palaso.IO;
using Palaso.Network;
using Palaso.Progress;
using Palaso.UI.WindowsForms.Progress;

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
        private readonly HtmlThumbNailer _htmlThumbnailer;
	    private readonly BookDownloadStartingEvent _bookDownloadStartingEvent;

	    public event EventHandler<BookDownloadedEventArgs> BookDownLoaded;

		public BookTransfer(BloomParseClient bloomParseClient, BloomS3Client bloomS3Client, HtmlThumbNailer htmlThumbnailer, BookDownloadStartingEvent bookDownloadStartingEvent)
		{
			this._parseClient = bloomParseClient;
			this._s3Client = bloomS3Client;
		    _htmlThumbnailer = htmlThumbnailer;
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

	    public string DownloadFromOrderUrl(string orderUrl, string destPath)
	    {
		    var decoded = HttpUtilityFromMono.UrlDecode(orderUrl);
		    var bucketStart = decoded.IndexOf(_s3Client.BucketName,StringComparison.InvariantCulture);
			if (bucketStart == -1)
            {
#if DEBUG
                if (decoded.StartsWith(("BloomLibraryBooks")))
                {
                    Palaso.Reporting.ErrorReport.NotifyUserOfProblem(
                    "The book is from bloomlibrary.org, but you are running the DEBUG version of Bloom, which can only use dev.bloomlibrary.org.");
                }
                else
                {
                    throw new ApplicationException("Can't match URL of bucket of the book being downloaded, and I don't know why.");
                }

#else
                if (decoded.StartsWith(("BloomLibraryBooks-Sandbox")))
                {
                    Palaso.Reporting.ErrorReport.NotifyUserOfProblem(
                        "The book is from the testing version of the bloomlibrary, but you are running the RELEASE version of Bloom. The RELEASE build cannot use the 'dev.bloomlibrary.org' site. If you need to do that for testing purposes, set the windows Environment variable 'BloomSandbox' to 'true'.", decoded);
                }  
                else
                {
                    throw new ApplicationException(string.Format("Can't match URL of bucket of the book being downloaded {0}, and I don't know why.", decoded));
                }
#endif
                return null;
            }

		    var s3orderKey = decoded.Substring(bucketStart  + _s3Client.BucketName.Length + 1);
	        string url = "unknown";
	        string title = "unknown";
	        try
	        {
	            var metadata = BookMetaData.FromString(_s3Client.DownloadFile(s3orderKey));
	            url = metadata.DownloadSource;
	            title = metadata.Title;
	            if (_progressDialog != null)
	                _progressDialog.Invoke((Action) (() => { _progressDialog.Progress = 1; }));
	            // downloading the metadata is considered step 1.
	            var destinationPath = DownloadBook(metadata.DownloadSource, destPath);
	            LastBookDownloadedPath = destinationPath;

	            Analytics.Track("DownloadedBook-Success",
	                new Dictionary<string, string>() {{"url", url}, {"title",title}});
	            return destinationPath;
	        }
	        catch (WebException e)
	        {
	            DisplayNetworkDownloadProblem(e);
	            Analytics.Track("DownloadedBook-Failure",
                    new Dictionary<string, string>() { { "url", url }, { "title", title } });
                Analytics.ReportException(e);
	            return "";
	        }
	        catch (AmazonServiceException e)
	        {
	            DisplayNetworkDownloadProblem(e);
	            Analytics.Track("DownloadedBook-Failure",
                    new Dictionary<string, string>() { { "url", url }, { "title", title } });
                Analytics.ReportException(e);
	            return "";
	        }
	        catch (Exception e)
	        {
	            ShellWindow.Invoke((Action) (() =>
	                Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e,
	                    LocalizationManager.GetString("Publish.Upload.DownloadProblem",
	                        "There was a problem downloading your book. You may need to restart Bloom or get technical help."))));
	            Analytics.Track("DownloadedBook-Failure",
                    new Dictionary<string, string>() { { "url", url }, { "title", title } });
                Analytics.ReportException(e);
	            return "";
	        }
	    }

	    private static void DisplayNetworkDownloadProblem(Exception e)
	    {
	        ShellWindow.Invoke((Action) (() => 
	            Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e,
                    LocalizationManager.GetString("Download.GenericDownloadProblemNotice",
	                    "There was a problem downloading your book."))));

	    }

		private static void DisplayNetworkUploadProblem(Exception e, IProgress progress)
		{
            progress.WriteError(LocalizationManager.GetString("Publish.Upload.GenericUploadProblemNotice",
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

	    private ProgressDialog _progressDialog;
        private string _downloadRequest;

		internal void HandleBloomBookOrder(string argument)
		{
			_downloadRequest = argument;
			using (_progressDialog = new ProgressDialog())
			{
				_progressDialog.CanCancel = false; // one day we may allow this...
				_progressDialog.Overview = LocalizationManager.GetString("Download.DownloadingDialogTitle", "Downloading book");
				_progressDialog.ProgressRangeMaximum = 14; // a somewhat minimal file count. We will fine-tune it when we know.
				if (IsUrlOrder(argument))
				{
					var link = new BloomLinkArgs(argument);
					var indexOfSlash = link.OrderUrl.LastIndexOf('/');
					var bookOrder = link.OrderUrl.Substring(indexOfSlash + 1);
					_progressDialog.StatusText = Path.GetFileNameWithoutExtension(bookOrder);
				}
				else
				{
					_progressDialog.StatusText = Path.GetFileNameWithoutExtension(argument);
				}

				// We must do the download in a background thread, even though the whole process is doing nothing else,
				// so we can invoke stuff on the main thread to (e.g.) update the progress bar.
				BackgroundWorker worker = new BackgroundWorker();
				worker.DoWork += OnDoDownload;
				_progressDialog.BackgroundWorker = worker;
				//dlg.CancelRequested += new EventHandler(OnCancelRequested);
				_progressDialog.ShowDialog(); // hidden automatically when task completes
				if (_progressDialog.ProgressStateResult != null &&
					_progressDialog.ProgressStateResult.ExceptionThatWasEncountered != null)
				{
					Palaso.Reporting.ErrorReport.ReportFatalException(
						_progressDialog.ProgressStateResult.ExceptionThatWasEncountered);
				}
			}
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
				DownloadFromOrderUrl(link.OrderUrl, DownloadFolder);
			}
				// If we are passed a bloom book order, download the corresponding book and open it.
			else if (_downloadRequest.ToLower().EndsWith(BookTransfer.BookOrderExtension.ToLower()) &&
					 File.Exists(_downloadRequest))
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
		    return argument.ToLower().StartsWith(BloomLinkArgs.kBloomUrlPrefix);
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

		public string UploadBook(string bookFolder, IProgress progress, out string parseId)
		{
			var metaDataText = MetaDataText(bookFolder);
			var metadata = BookMetaData.FromString(metaDataText);
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
			var s3BookId = S3BookId(metadata);
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
			File.Copy(metadataPath, orderPath, true);
			parseId = "";
		    try
		    {
				_s3Client.UploadBook(s3BookId, bookFolder, progress);
				metadata.BaseUrl = _s3Client.BaseUrl;
				metadata.BookOrder = _s3Client.BookOrderUrl;
				progress.WriteStatus(LocalizationManager.GetString("Publish.Upload.UploadingBook", "Uploading book record"));
				// Do this after uploading the books, since the ThumbnailUrl is generated in the course of the upload.
				var response = _parseClient.SetBookRecord(metadata.Json);
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
			catch (AmazonServiceException e)
			{
                DisplayNetworkUploadProblem(e, progress);
                if (!UseSandbox) // don't make it seem like there are more upload failures than their really are if this a tester pushing to the sandbox
                    Analytics.Track("UploadBook-Failure", new Dictionary<string, string>() { { "url", metadata.BookOrder }, { "title", metadata.Title }, { "error", e.Message } });
                return "";
			}
			catch (Exception e)
			{			    
			    progress.WriteError(LocalizationManager.GetString("Publish.Upload.UploadProblemNotice",
                                "There was a problem uploading your book. You may need to restart Bloom or get technical help."));
                progress.WriteError(e.Message.Replace("{","{{").Replace("}","}}")); 
                progress.WriteVerbose(e.StackTrace);
                if (!UseSandbox) // don't make it seem like there are more upload failures than their really are if this a tester pushing to the sandbox
                    Analytics.Track("UploadBook-Failure", new Dictionary<string, string>() { { "url", metadata.BookOrder }, { "title", metadata.Title }, { "error", e.Message } });
                return "";
			}
			return s3BookId;
		}

		internal string BookOrderUrl {get { return _s3Client.BookOrderUrl; }}

	    private static string MetaDataText(string bookFolder)
	    {
		    return File.ReadAllText(bookFolder.CombineForPath(BookInfo.MetaDataFileName));
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
		/// <param name="s3BookId"></param>
		/// <param name="dest"></param>
		/// <returns></returns>
	    internal string DownloadBook(string s3BookId, string dest)
		{
			var destinationPath = _s3Client.DownloadBook(s3BookId, dest, _progressDialog);
			if (BookDownLoaded != null)
			{
				var bookInfo = new BookInfo(destinationPath, false); // A downloaded book is a template, so never editable.
				BookDownLoaded(this, new BookDownloadedEventArgs() {BookDetails = bookInfo});
			}

			return destinationPath;
		}

	    public void HandleBookOrder(string bookOrderPath, string projectPath)
	    {
		    var metadata = BookMetaData.FromString(File.ReadAllText(bookOrderPath));
		    var s3BookId = metadata.DownloadSource;
		    _s3Client.DownloadBook(s3BookId, Path.GetDirectoryName(projectPath));
	    }

		public bool IsBookOnServer(string bookPath)
		{
			var metadata = BookMetaData.FromString(File.ReadAllText(bookPath.CombineForPath(BookInfo.MetaDataFileName)));
			return _parseClient.GetSingleBookRecord(metadata.Id) != null;
		}

		// Wait (up to three seconds) for data uploaded to become available.
		// Currently only used in unit testing.
		// I have no idea whether 3s is an adequate time to wait for 'eventual consistency'. So far it seems to work.
	    internal void WaitUntilS3DataIsOnServer(string bookPath)
	    {
		    var s3Id = S3BookId(BookMetaData.FromFolder(bookPath));
			var count = Directory.GetFiles(bookPath).Length;
			for (int i = 0; i < 30; i++)
		    {
			    var uploaded = _s3Client.GetBookFileCount(s3Id);
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
                Palaso.Reporting.ErrorReport.NotifyUserOfProblem("Could not log you in using user='" + Settings.Default.WebUserId + "' and pwd='" + Settings.Default.WebPassword+"'."+System.Environment.NewLine+
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
			    var publishModel = new PublishModel(bookSelection, new PdfMaker(), currentEditableCollectionSelection, null, server, _htmlThumbnailer);
			    publishModel.PageLayout = book.GetLayout();
			    var view = new PublishView(publishModel, new SelectedTabChangedEvent(), this, null);
			    string dummy;
			    FullUpload(book, dlg.Progress, view, out dummy, dlg);
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
		/// <param name="invokeTarget"></param>
		/// <returns></returns>
	    internal string FullUpload(Book.Book book, LogBox progressBox, PublishView publishView, out string parseId, Form invokeTarget = null)
		{
			var bookFolder = book.FolderPath;
			// Set this in the metadata so it gets uploaded. Do this in the background task as it can take some time.
			// These bits of data can't easily be set while saving the book because we save one page at a time
			// and they apply to the book as a whole.
            book.BookInfo.LanguageTableReferences = _parseClient.GetLanguagePointers(book.CollectionSettings.MakeLanguageUploadData(book.AllLanguages.ToArray()));
			book.BookInfo.PageCount = book.GetPages().Count();
			book.BookInfo.Save();
			progressBox.WriteStatus(LocalizationManager.GetString("Publish.Upload.MakingThumbnail", "Making thumbnail image..."));
            MakeThumbnail(book, 70, invokeTarget);
            MakeThumbnail(book, 256, invokeTarget);
            //the largest thumbnail I found on Amazon was 300px high. Prathambooks.org about the same.
            var uploadPdfPath = Path.Combine(bookFolder, Path.ChangeExtension(Path.GetFileName(bookFolder), ".pdf"));
			// If there is not already a locked preview in the book folder
			// (which we take to mean the user has created a customized one that he prefers),
			// make sure we have a current correct preview and then copy it to the book folder so it gets uploaded.
			if (!FileUtils.IsFileLocked(uploadPdfPath))
			{
				progressBox.WriteStatus(LocalizationManager.GetString("Publish.Upload.MakingPdf", "Making PDF Preview..."));
				publishView.MakePublishPreview();
				if (File.Exists(publishView.PdfPreviewPath))
				{
					File.Copy(publishView.PdfPreviewPath, uploadPdfPath, true);
				}
			}
			string result = UploadBook(bookFolder, progressBox, out parseId);
			return result;
		}

		void MakeThumbnail(Book.Book book, int height, Control invokeTarget)
		{
			bool done = false;
			string error = null;

		    HtmlThumbNailer.ThumbnailOptions options = new HtmlThumbNailer.ThumbnailOptions()
		    {
		        CenterImageUsingTransparentPadding = false,
		        //since this is destined for HTML, it's much easier to handle if there is no pre-padding

                Height=height,
                Width =-1,
                FileName = "thumbnail-"+height+".png"
		    };

			book.RebuildThumbNailAsync(options, (info, image) => done = true,
				(info, ex) =>
				{
					done = true;
					throw ex;
				});
		    var giveUpTime = DateTime.Now.AddSeconds(5);
			while (!done && DateTime.Now < giveUpTime)
			{
				Thread.Sleep(100);
				Application.DoEvents();
				// In the context of bulk upload, when a model dialog is the only window, apparently Application.Idle is never invoked.
				// So we need a trick to allow the thumbnailer to actually make some progress, since it usually works while idle.
                this._htmlThumbnailer.Advance(invokeTarget);
			}
		    if (!done)
		    {
		        throw new ApplicationException(string.Format("Gave up waiting for the {0} to be created.", options.FileName));
		    }
		}

        internal bool IsThisVersionAllowedToUpload()
        {
            return _parseClient.IsThisVersionAllowedToUpload();
        }
    }
}
