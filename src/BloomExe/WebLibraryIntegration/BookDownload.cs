using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Web;
using System.Windows.Forms;
using Amazon.Runtime;
using Amazon.S3;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Properties;
using Bloom.Publish;
using Bloom.Publish.BloomLibrary;
using Bloom.Publish.PDF;
using DesktopAnalytics;
using L10NSharp;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms.Progress;
using BloomTemp;
using System.Xml;
using Bloom.web.controllers;
using System.Text;

namespace Bloom.WebLibraryIntegration
{
	/// <summary>
	/// Gets files from Amazon S3.
	/// </summary>
	public class BookDownload
	{
		
		private readonly BloomS3Client _s3Client;
		private readonly BookDownloadStartingEvent _bookDownloadStartingEvent;
		public IProgress Progress;

		public event EventHandler<BookDownloadedEventArgs> BookDownLoaded;

		public BookDownload(BloomParseClient bloomParseClient, BloomS3Client bloomS3Client, BookDownloadStartingEvent bookDownloadStartingEvent)
		{
			this._s3Client = bloomS3Client;
			_bookDownloadStartingEvent = bookDownloadStartingEvent;
		}

		public string LastBookDownloadedPath { get; set; }

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
				GetUrlAndTitle(bucket, s3OrderKey, ref url, ref title);
				if (_progressDialog != null)
					_progressDialog.Invoke((Action) (() => { _progressDialog.Progress = 1; }));
				// downloading the metadata is considered step 1.
				// uncomment line below to simulate bad internet connection
				// throw new WebException();
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
				var showSendReport = true;
				var message = LocalizationManager.GetString("Download.ProblemNotice",
					"There was a problem downloading your book. You may need to restart Bloom or get technical help.");
				// BL-1233, we've seen what appear to be timeout exceptions, can't confirm the actual Exception subclass though.
				// It's likely that S3 wraps the original TimeoutException from .net with its own AmazonServiceException.
				if (e is TimeoutException || e.InnerException is TimeoutException)
				{
					message = LocalizationManager.GetString("Download.TimeoutProblemNotice",
						"There was a problem downloading the book: something took too long. You can try again at a different time, or write to us at issues@bloomlibrary.org if you cannot get the download to work from your location.");
					showSendReport = false;
				}
				if (e is AmazonServiceException || e is WebException || e is IOException) // Network problems, not an internal error, less alarming message called for
				{
					message = LocalizationManager.GetString("Download.GenericNetworkProblemNotice",
						"There was a problem downloading the book.  You can try again at a different time, or write to us at issues@bloomlibrary.org if you cannot get the download to work from your location.");
					showSendReport = false;
				}
				DisplayProblem(e, message, showSendReport);
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

		private static void DisplayProblem(Exception e, string message, bool showSendReport = true)
		{
			var action = new Action(() => NonFatalProblem.Report(ModalIf.Alpha, PassiveIf.All, message, null, e, showSendReport));
				var shellWindow = ShellWindow;
				if (shellWindow != null)
					shellWindow.Invoke(action);
				else
					action.Invoke();
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
				progressDialog.CanCancel = true;
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
				progressDialog.ShowDialog(); // hidden automatically when task completes
				if (progressDialog.ProgressStateResult != null &&
					progressDialog.ProgressStateResult.ExceptionThatWasEncountered != null)
				{
					var exc = progressDialog.ProgressStateResult.ExceptionThatWasEncountered;
					ProblemReportApi.ShowProblemDialog(null, exc, "", "fatal");
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
			else if (_downloadRequest.ToLowerInvariant().EndsWith(BookInfo.BookOrderExtension.ToLowerInvariant()) &&
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

		internal const string BloomS3UrlPrefix = "https://s3.amazonaws.com/";

		
		private static string MetaDataText(string bookFolder)
		{
			return RobustFile.ReadAllText(bookFolder.CombineForPath(BookInfo.MetaDataFileName));
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
			bool needToSave = false;
			// If the book is downloaded from Bloom Library, we don't want to treat it as though
			// it were directly created from a Reader bloomPack.  So relax the formatting lock.
			// See https://issues.bloomlibrary.org/youtrack/issue/BL-9996.
			if (dom.HasMetaElement("lockFormatting"))
			{
				dom.RemoveMetaElement("lockFormatting");
				needToSave = true;
			}
			if (!BookMetaData.FromString(MetaDataText(destinationPath)).IsSuitableForMakingShells)
			{
				dom.RecordAsLockedDown(true);
				needToSave = true;
			}
			if (needToSave)
				XmlHtmlConverter.SaveDOMAsHtml5(dom.RawDom, htmlFile);

			return destinationPath;
		}

		public void HandleBookOrder(string bookOrderPath, string projectPath)
		{
			var metadata = BookMetaData.FromString(RobustFile.ReadAllText(bookOrderPath));
			var s3BookId = metadata.DownloadSource;
			var bucket = BloomS3Client.ProductionBucketName; //TODO
			_s3Client.DownloadBook(bucket, s3BookId, Path.GetDirectoryName(projectPath));
		}
	}
}
