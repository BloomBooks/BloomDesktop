using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Bloom.Properties;
using Bloom.WebLibraryIntegration;
using L10NSharp;
using Palaso.IO;

namespace Bloom.Publish
{
	/// <summary>
	/// This class replaces the AdobeReaderControl in the PublishView when the Upload To BloomLibrary.org option is selected.
	/// </summary>
	public partial class BloomLibraryPublishControl : UserControl
	{
		private PublishView _parentView;
		private BookTransfer _bookTransferrer;
		private LoginDialog _loginDialog;
		private Book.Book _book;
		public BloomLibraryPublishControl(PublishView parentView, BookTransfer bookTransferrer, LoginDialog login, Book.Book book)
		{
			_parentView = parentView;
			_bookTransferrer = bookTransferrer;
			_loginDialog = login;
			_book = book;
			InitializeComponent();
			_loginDialog.LogIn(); // See if saved credentials work.
			UpdateDisplay();
		}

		private void UpdateDisplay()
		{
			_uploadButton.Enabled = _bookTransferrer.LoggedIn;
		}

		private void _loginButton_Click(object sender, EventArgs e)
		{
			// The dialog is configured by Autofac to interact with the single instance of BloomParseClient,
			// which it will update with all the relevant information if login is successful.
			_loginDialog.ShowDialog();
			UpdateDisplay();
		}

		private void _uploadButton_Click(object sender, EventArgs e)
		{
			var info = _book.BookInfo;
			if (string.IsNullOrEmpty(info.Id))
			{
				info.Id = Guid.NewGuid().ToString();
			}
			info.UploadedBy = Settings.Default.WebUserId;
			// Todo: try to make sure it has a thumbnail.
			if (_bookTransferrer.IsBookOnServer(_book.FolderPath))
			{
				using (var dlg = new OverwriteWarningDialog())
				{
					if (dlg.ShowDialog() == DialogResult.Cancel)
						return;
				}
			}
			var worker = new BackgroundWorker();
			worker.DoWork += BackgroundUpload;
			worker.WorkerReportsProgress = true;
			worker.RunWorkerCompleted += (theWorker, completedEvent) =>
			{
				if (!string.IsNullOrEmpty(_progressBox.Text))
				{
					string done = LocalizationManager.GetString("Common.Done", "done");
					_progressBox.Text += done + Environment.NewLine;
				}
				if (completedEvent.Error != null)
				{
					string errorMessage = LocalizationManager.GetString("PublishWeb.ErrorUploading","Sorry, there was a problem uploading {0}. Some details follow. You may need technical help.");
					_progressBox.Text +=
						String.Format(errorMessage + Environment.NewLine,
							_book.Title);
					_progressBox.Text += completedEvent.Error;
				}
				else
				{
					string congratsMessage = LocalizationManager.GetString("PublishWeb.Congratulations","Congratulations, {0} is now on bloom library");
					_progressBox.Text += string.Format(congratsMessage, _book.Title);
				}
				ScrollProgressToEnd();
			};
			worker.RunWorkerAsync(_book.FolderPath);
			//_bookTransferrer.UploadBook(_book.FolderPath, AddNotification);
		}

		void BackgroundUpload(object sender, DoWorkEventArgs e)
		{
			var bookFolder = (string)e.Argument;
			var uploadPdfPath = Path.Combine(bookFolder, Path.ChangeExtension(Path.GetFileName(bookFolder), ".pdf"));
			// If there is not already a locked preview in the book folder
			// (which we take to mean the user has created a customized one that he prefers),
			// copy the current preview to the book folder so it gets uploaded.
			if (!FileUtils.IsFileLocked(uploadPdfPath))
			{
				// If we're in the process of making it, finish.
				if (_parentView.IsMakingPdf)
				{
					AddNotification(LocalizationManager.GetString("PublishWeb.MakingPdf", "Making PDF Preview..."));
					while (_parentView.IsMakingPdf)
						Thread.Sleep(100);
				}
				if (File.Exists(_parentView.PdfPreviewPath))
				{
					File.Copy(_parentView.PdfPreviewPath, uploadPdfPath);
				}
			}
			_bookTransferrer.UploadBook(bookFolder, AddNotification);
		}

		private void AddNotification(string notification)
		{
			this.Invoke((Action) (() =>
			{
				string textToAdd = notification;
				if (!string.IsNullOrEmpty(_progressBox.Text))
					textToAdd = LocalizationManager.GetString("Common.Done", "done") + Environment.NewLine + notification;
				_progressBox.Text += textToAdd + "...";
				ScrollProgressToEnd();
			}));
		}

		private void ScrollProgressToEnd()
		{
			_progressBox.SelectionStart = _progressBox.Text.Length;
			_progressBox.ScrollToCaret();
		}
	}
}
