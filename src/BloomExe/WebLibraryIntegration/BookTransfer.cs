using System;
using System.Collections.Generic;
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
using L10NSharp;
using Palaso.Extensions;
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
	}
}
