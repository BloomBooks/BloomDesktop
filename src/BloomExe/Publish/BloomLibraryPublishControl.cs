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
		private bool _okToUpload = true;

		private string _pleaseSetThis = LocalizationManager.GetString("Publish.Upload.PleaseSetThis",
			"Please set this from the edit tab");
		public BloomLibraryPublishControl(PublishView parentView, BookTransfer bookTransferrer, LoginDialog login, Book.Book book)
		{
			_parentView = parentView;
			_bookTransferrer = bookTransferrer;
			_loginDialog = login;
			_book = book;
			InitializeComponent();
			_originalLoginText = _loginLink.Text; // Before anything might modify it (but after InitializeComponent creates it).
			_titleLabel.Text = book.BookInfo.Title;

		    _progressBox.ShowDetailsMenuItem = true;
		    _progressBox.ShowCopyToClipboardMenuItem = true;

			var metadata = book.GetLicenseMetadata();
			// This is usually redundant, but might not be on old books where the license was set before the new
			// editing code was written.
			book.UpdateLicenseMetdata(metadata);
			if (string.IsNullOrEmpty(metadata.License.RightsStatement) && metadata.License is CreativeCommonsLicense)
			{
				// Don't do this for non-CC licences, since for them, if Rights is empty we want to display the "please fill this in" message.
				_licenseNotesLabel.Text = metadata.License.GetDescription("en");
			}
			else
				_licenseNotesLabel.Text = metadata.License.RightsStatement;
			_licenseImageBox.Image = metadata.License.GetImage();
			if (string.IsNullOrEmpty(metadata.License.GetDescription("en")))
				_ccDescriptionButton.Visible = false;
			_copyrightLabel.Text = book.BookInfo.Copyright;

			_languagesLabel.Text = string.Join(", ", book.AllLanguages.Select(lang => _book.PrettyPrintLanguage(lang)).ToArray());

			_creditsLabel.Text = book.BookInfo.Credits;
			_summaryBox.Text = book.BookInfo.Summary;

			try
			{
				_loginDialog.LogIn(); // See if saved credentials work.
			}
			catch (Exception e)
			{
			    Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e,
			        LocalizationManager.GetString("Publish.Upload.LoginFailure",
			            "Bloom could not log in to BloomLibrary.org using your saved credentials. Please check your network connection"));
			}
			_optional1.Left = _summaryBox.Right - _optional1.Width; // right-align these (even if localization changes their width)
			RequireValue(_copyrightLabel);
			RequireValue(_titleLabel);
			RequireValue(_languagesLabel);
			RequireValue(_licenseNotesLabel); // optional for CC license, but if so we display the description
			// UploadedBy is also required, but this is handled in UpdateDisplay because it can change.
			UpdateDisplay();
		}

		void RequireValue(Label item)
		{
			if (string.IsNullOrWhiteSpace(item.Text))
			{
				item.Text = _pleaseSetThis;
				item.ForeColor = Color.Red;
				_okToUpload = false;
			}
			
		}

		private void UpdateDisplay()
		{
			bool okToUpload = _okToUpload;
			_uploadButton.Enabled = _bookTransferrer.LoggedIn && okToUpload;
			if (_uploadButton.Enabled)
			{
				_progressBox.Text = "";
				_progressBox.ForeColor = Color.FromKnownColor(KnownColor.WindowText);
			}
			else
			{
				_progressBox.ForeColor = Color.Red;
				if (!okToUpload)
				{
					_progressBox.Text = LocalizationManager.GetString("Publish.Upload.FieldsNeedAttention",
						"One or more fields above need your attention before uploading");
				}
				if (!_bookTransferrer.LoggedIn)
				{
					if (_progressBox.Text != "")
						_progressBox.Text += Environment.NewLine;
					_progressBox.Text += LocalizationManager.GetString("Publish.Upload.PleaseLogIn",
						"Please log in to BloomLibrary.org (or sign up) before uploading");
				}
			}
			_loginLink.Text = _bookTransferrer.LoggedIn ? LocalizationManager.GetString("Publish.Upload.Logout", "Log out of BloomLibrary.org") : _originalLoginText;
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
			_uploadButton.Enabled = false; // can't start another until done.
			ScrollControlIntoView(_progressBox);
			_progressBox.Clear();
			var info = _book.BookInfo;
			if (string.IsNullOrEmpty(info.Id))
			{
				info.Id = Guid.NewGuid().ToString();
			}
			info.Uploader = _bookTransferrer.UserId;

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
				if (completedEvent.Error != null)
				{
					string errorMessage = LocalizationManager.GetString("Publish.Upload.ErrorUploading","Sorry, there was a problem uploading {0}. Some details follow. You may need technical help.");
					_progressBox.WriteError(errorMessage,_book.Title);
					_progressBox.WriteException(completedEvent.Error);
				}
				else if (string.IsNullOrEmpty((string)completedEvent.Result))
				{
					// Something went wrong, typically already reported.
					string sorryMessage = LocalizationManager.GetString("Publish.Upload.FinalUploadFailureNotice", "Sorry, \"{0}\" was not successfully uploaded");
					_progressBox.WriteError(sorryMessage, _book.Title);
				}
				else {
					string congratsMessage = LocalizationManager.GetString("Publish.Upload.UploadCompleteNotice","Congratulations, \"{0}\" is now available on BloomLibrary.org");
					_progressBox.WriteMessageWithColor(Color.Blue, congratsMessage, _book.Title);
				}
				_uploadButton.Enabled = true; // Don't call UpdateDisplay, it will wipe out the progress messages.
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
                    _progressBox.WriteStatus(LocalizationManager.GetString("Publish.Upload.MakingPdf", "Making PDF Preview..."));
					while (_parentView.IsMakingPdf)
						Thread.Sleep(100);
				}
				if (File.Exists(_parentView.PdfPreviewPath))
				{
					File.Copy(_parentView.PdfPreviewPath, uploadPdfPath, true);
				}
			}
			e.Result = _bookTransferrer.UploadBook(bookFolder, _progressBox);
		}

		private void _ccDescriptionButton_Click(object sender, EventArgs e)
		{
			var licenseInfo = _book.GetLicenseMetadata().License;
			string description = licenseInfo.GetDescription("en");
			if (!string.IsNullOrEmpty(description) && !string.IsNullOrEmpty(licenseInfo.RightsStatement))
				description += Environment.NewLine + Environment.NewLine;
			description += licenseInfo.RightsStatement;
			MessageBox.Show(this, description, LocalizationManager.GetString("Publish.Upload.LicenseDetails", "License Details"));
		}

		private void _summaryBox_TextChanged(object sender, EventArgs e)
		{
			_book.BookInfo.Summary = _summaryBox.Text;
			_book.BookInfo.Save(); // Review: is this too often?

		}
	}
}
