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
using Palaso.UI.WindowsForms.ClearShare;

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
		private string _originalLoginText;
		public BloomLibraryPublishControl(PublishView parentView, BookTransfer bookTransferrer, LoginDialog login, Book.Book book)
		{
			_parentView = parentView;
			_bookTransferrer = bookTransferrer;
			_loginDialog = login;
			_book = book;
			InitializeComponent();
			_titleLabel.Text = book.BookInfo.Title; // Todo: fill in whatever we will, if initially empty

			var metadata = book.GetLicenseMetadata();
			// This is usually redundant, but might not be on old books where the license was set before the new
			// editing code was written.
			book.UpdateLicenseMetdata(metadata);
			if (string.IsNullOrEmpty(metadata.License.RightsStatement))
				_licenseNotesLabel.Text = metadata.License.GetDescription("en");
			else
				_licenseNotesLabel.Text = metadata.License.RightsStatement;
			_licenseImageBox.Image = metadata.License.GetImage();
			if (string.IsNullOrEmpty(metadata.License.GetDescription("en")))
				_ccDescriptionButton.Visible = false;
			_copyrightLabel.Text = book.BookInfo.Copyright;
			_languagesLabel.Text = string.Join(", ", book.AllLanguages.ToArray());

			_creditsLabel.Text = book.BookInfo.Credits;
			_summaryBox.Text = book.BookInfo.Summary;

			_loginDialog.LogIn(); // See if saved credentials work.
			if (bookTransferrer.LoggedIn)
				_uploadedByTextBox.Text = bookTransferrer.UploadedBy;
			_originalLoginText = _loginLink.Text;
			UpdateDisplay();
		}

		private void UpdateDisplay()
		{
			// Enhance: should we disable, if critical metadata is missing? Or give a message when clicked?
			_uploadButton.Enabled = _bookTransferrer.LoggedIn;
			_loginLink.Text = _bookTransferrer.LoggedIn ? LocalizationManager.GetString("PublishWeb.Logout", "Log out of BloomLibrary.org") : _originalLoginText;
			// Right-align the login link. (There ought to be a setting to make this happen, but I can't find it.)
			_loginLink.Left = _progressBox.Right - _loginLink.Width;
			_signUpLink.Visible = !_bookTransferrer.LoggedIn;
		}

		private void _loginLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			if (_bookTransferrer.LoggedIn)
			{
				// This becomes a logout button
				_bookTransferrer.Logout();
			}
			else
			{
				// The dialog is configured by Autofac to interact with the single instance of BloomParseClient,
				// which it will update with all the relevant information if login is successful.
				_loginDialog.ShowDialog(this);
				_uploadedByTextBox.Text = _bookTransferrer.UploadedBy;
			}
			UpdateDisplay();
		}

		private void _signUpLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			_loginDialog.SignUp(this);
			UpdateDisplay();
		}

		private void _uploadButton_Click(object sender, EventArgs e)
		{
			ScrollControlIntoView(_progressBox);
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
			worker.RunWorkerAsync(_book);
			//_bookTransferrer.UploadBook(_book.FolderPath, AddNotification);
		}

		void BackgroundUpload(object sender, DoWorkEventArgs e)
		{
			var book = (Book.Book) e.Argument;
			var bookFolder = book.FolderPath;
			// Set this in the metadata so it gets uploaded. Do this in the background task as it can take some time.
			// These bits of data can't easily be set while saving the book because we save one page at a time
			// and they apply to the book as a whole.
			book.BookInfo.Languages = _book.AllLanguages.ToArray();
			book.BookInfo.PageCount = _book.GetPages().Count();
			book.BookInfo.Save();
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
					File.Copy(_parentView.PdfPreviewPath, uploadPdfPath, true);
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

		private void _uploadedByTextBox_TextChanged(object sender, EventArgs e)
		{
			_bookTransferrer.UploadedBy = _uploadedByTextBox.Text;
		}

		private void _ccDescriptionButton_Click(object sender, EventArgs e)
		{
			var licenseInfo = _book.GetLicenseMetadata().License;
			string description = licenseInfo.GetDescription("en");
			if (!string.IsNullOrEmpty(description) && !string.IsNullOrEmpty(licenseInfo.RightsStatement))
				description += Environment.NewLine + Environment.NewLine;
			description += licenseInfo.RightsStatement;
			MessageBox.Show(this, description, LocalizationManager.GetString("PublishWeb.LicenseDetails", "License Details"));
		}

		private void _summaryBox_TextChanged(object sender, EventArgs e)
		{
			_book.BookInfo.Summary = _summaryBox.Text;
			_book.BookInfo.Save(); // Review: is this too often?

		}
	}
}
