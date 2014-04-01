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
		// A list of 'orders' to download books. These may be urls or (this may be obsolete) paths to book order files.
		// One order is created when a url or book order is found as the single command line argument.
		// It gets processed by an initial call to HandleOrders in LibraryListView.ManageButtonsAtIdleTime
		// when everything is sufficiently initialized to handle downloading a new book.
		// Orders may also be created in the Program.ServerThreadAction method, on a thread that is set up
		// to receive download orders from additional instances of Bloom created by clicking a download link
		// in a web page. These may be handled at any time.
	    private OrderList _orders;

	    public event EventHandler<BookDownloadedEventArgs> BookDownLoaded;

		public BookTransfer(BloomParseClient bloomParseClient, BloomS3Client bloomS3Client, OrderList orders)
		{
			this._parseClient = bloomParseClient;
			this._s3Client = bloomS3Client;
			_orders = orders;
			if (_orders != null)
			{
				_orders.OrderAdded += _OrderAdded;
			}
		}

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

		void _OrderAdded(object sender, EventArgs e)
		{
			HandleOrders();
		}

	    public void HandleOrders()
	    {
		    if (_orders == null)
			    return;
		    string order;
		    while ((order = _orders.GetOrder()) != null)
		    {
			    HandleBloomBookOrder(order);
		    }
	    }

	    public string DownloadFromOrderUrl(string orderUrl, string destPath)
	    {
		    var decoded = HttpUtilityFromMono.UrlDecode(orderUrl);
		    var bucketStart = decoded.IndexOf(_s3Client.BucketName,StringComparison.InvariantCulture);
			if (bucketStart == -1)
				throw new ArgumentException("URL is not within expected bucket");
		    var s3orderKey = decoded.Substring(bucketStart  + _s3Client.BucketName.Length + 1);
		    try
		    {
			    var metadata = BookMetaData.FromString(_s3Client.DownloadFile(s3orderKey));
				if (_progressDialog != null)
					_progressDialog.Invoke((Action) (() => { _progressDialog.Progress = 1; }));
				    // downloading the metadata is considered step 1.
			    return DownloadBook(metadata.DownloadSource, destPath);

		    }
			catch(WebException e)
			{
				DisplayNetworkDownloadProblem(e);
				return "";
			}
			catch (AmazonServiceException e)
		    {
			    DisplayNetworkDownloadProblem(e);
			    return "";
		    }
		    catch (Exception e)
		    {
	        ShellWindow.Invoke((Action) (() => 
	            Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e,
                LocalizationManager.GetString("Publish.Upload.DownloadProblem",
		                "There was a problem downloading your book. You may need to restart Bloom or get technical help."))));
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

		private void HandleBloomBookOrder(string argument)
		{
			var mainWindow = ShellWindow;
			if (mainWindow == null)
				return; // We shouldn't be trying to handle orders while we don't have a main window open.
			mainWindow.Invoke((Action)(() =>
			{
				_progressDialog = new ProgressDialog();
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
				_progressDialog.Show(mainWindow);
			}));
			try
			{
				// If we are passed a bloom book order URL, download the corresponding book and open it.
				if (IsUrlOrder(argument))
				{
					var link = new BloomLinkArgs(argument);
					DownloadFromOrderUrl(link.OrderUrl, DownloadFolder);
				}
				// If we are passed a bloom book order, download the corresponding book and open it.
				else if (argument.ToLower().EndsWith(BookTransfer.BookOrderExtension.ToLower()) && File.Exists(argument))
				{
					HandleBookOrder(argument);
				}
			}
			finally
			{
				_progressDialog.Invoke((Action) (() => _progressDialog.Dispose()));
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
				metadata.Thumbnail = _s3Client.ThumbnailUrl;
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
		    }
			catch (WebException e)
			{
                DisplayNetworkUploadProblem(e, progress);
				return "";
			}
			catch (AmazonServiceException e)
			{
                DisplayNetworkUploadProblem(e, progress);
				return "";
			}
			catch (Exception e)
			{			    
			    progress.WriteError(LocalizationManager.GetString("Publish.Upload.UploadProblemNotice",
                                "There was a problem uploading your book. You may need to restart Bloom or get technical help."));
                progress.WriteError(e.Message.Replace("{","{{").Replace("}","}}")); 
                progress.WriteVerbose(e.StackTrace);
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
			var result = _s3Client.DownloadBook(s3BookId, dest, _progressDialog);
			if (BookDownLoaded != null)
			{
				var bookInfo = new BookInfo(result, false); // A downloaded book is a template, so never editable.
				BookDownLoaded(this, new BookDownloadedEventArgs() {BookDetails = bookInfo});
			}

			return result;
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
				MessageBox.Show("To use this feature, you must first run Bloom normally and log in.");
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
			    var publishModel = new PublishModel(bookSelection, new PdfMaker(), currentEditableCollectionSelection, null, server);
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
			book.BookInfo.Languages = book.AllLanguages.ToArray();
			book.BookInfo.PageCount = book.GetPages().Count();
			book.BookInfo.Save();
			progressBox.WriteStatus(LocalizationManager.GetString("Publish.Upload.MakingThumbnail", "Making thumbnail image..."));
			RebuildThumbnail(book, invokeTarget);
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

		static void RebuildThumbnail(Book.Book book, Control invokeTarget)
		{
			bool done = false;
			string error = null;
			book.RebuildThumbNailAsync((info, image) => done = true,
				(info, ex) =>
				{
					done = true;
					throw ex;
				});
			while (!done)
			{
				Thread.Sleep(100);
				Application.DoEvents();
				// In the context of bulk upload, when a model dialog is the only window, apparently Application.Idle is never invoked.
				// So we need a trick to allow the thumbnailer to actually make some progress, since it usually works while idle.
				book.MakeThumbnailerAdvance(invokeTarget);
			}
		}
    }
}
