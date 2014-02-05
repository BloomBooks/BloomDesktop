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

		private string _pleaseSetThis = LocalizationManager.GetString("PublishWeb.PleaseSetThis",
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
			catch (Exception)
			{
				MessageBox.Show(this,
					LocalizationManager.GetString("PublishWeb.LoginFailure",
						"Bloom could not log in to BloomLibrary.org using your saved credentials. Please check your network connection"),
					LocalizationManager.GetString("PublishWeb.LoginFailed", "Login Failed"),
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			if (bookTransferrer.LoggedIn)
				_uploadedByTextBox.Text = bookTransferrer.UploadedBy;
			_optional1.Left = _summaryBox.Right - _optional1.Width; // right-align these (even if localization changes their width)
			_optional2.Left = _summaryBox.Right - _optional2.Width;
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
			_pleaseSetUploadedByLabel.Visible = string.IsNullOrWhiteSpace(_uploadedByTextBox.Text);
			okToUpload &= !_pleaseSetUploadedByLabel.Visible;
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
					_progressBox.Text = LocalizationManager.GetString("PublishWeb.FieldsNeedAttention",
						"One or more fields above need your attention before uploading");
				}
				if (!_bookTransferrer.LoggedIn)
				{
					if (_progressBox.Text != "")
						_progressBox.Text += Environment.NewLine;
					_progressBox.Text += LocalizationManager.GetString("PublishWeb.PleaseLogIn",
						"Please log in to BloomLibrary.org (or sign up) before uploading");
				}
			}
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
			_uploadButton.Enabled = false; // can't start another until done.
			ScrollControlIntoView(_progressBox);
			_progressBox.Text = "";
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
				else if (string.IsNullOrEmpty((string)completedEvent.Result))
				{
					// Something went wrong, typically already reported.
					string sorryMessage = LocalizationManager.GetString("PublishWeb.Sorry", "Sorry, {0} was not successfully uploaded");
					_progressBox.Text += string.Format(sorryMessage, _book.Title);
				}
				else {
					string congratsMessage = LocalizationManager.GetString("PublishWeb.Congratulations","Congratulations, {0} is now on bloom library");
					_progressBox.Text += string.Format(congratsMessage, _book.Title);
				}
				ScrollProgressToEnd();
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
					AddNotification(LocalizationManager.GetString("PublishWeb.MakingPdf", "Making PDF Preview..."));
					while (_parentView.IsMakingPdf)
						Thread.Sleep(100);
				}
				if (File.Exists(_parentView.PdfPreviewPath))
				{
					File.Copy(_parentView.PdfPreviewPath, uploadPdfPath, true);
				}
			}
			e.Result = _bookTransferrer.UploadBook(bookFolder, AddNotification);
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
			UpdateDisplay(); // depends in part on whether this box is empty.
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
