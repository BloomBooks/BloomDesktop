using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Bloom.Properties;
using Bloom.web;
using Bloom.WebLibraryIntegration;
using Bloom.Workspace;
using L10NSharp;
using SIL.Windows.Forms.ClearShare;

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
		private bool _okToUploadDependsOnLangsChecked;
		private bool _usingNotesLabel = true;
		private bool _usingNotesSuggestion = true;
		private bool _usingCcControls = true;
		private BackgroundWorker _uploadWorker;
		private string _originalUploadText;

		private string _pleaseSetThis = LocalizationManager.GetString("PublishTab.Upload.PleaseSetThis",
			"Please set this from the edit tab", "This shows next to the license, if the license has not yet been set.");
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
			_progressBox.LinkClicked += _progressBox_LinkClicked;

			var metadata = book.GetLicenseMetadata();
			// This is usually redundant, but might not be on old books where the license was set before the new
			// editing code was written.
			book.SetMetadata(metadata);
			var license = metadata.License;
			if (license == null || (license is NullLicense && string.IsNullOrWhiteSpace(metadata.CopyrightNotice)))
			{
				// A null license and no copyright indicates they never even opened the ClearShare dialog to choose a license.
				_usingCcControls = false;
				_usingNotesLabel = false;
				_licenseSuggestion.Text = _pleaseSetThis;
				_okToUpload = false;
			}
			else if (license is CreativeCommonsLicense)
			{
				_creativeCommonsLink.Text = license.Token.ToUpperInvariant();
				_usingNotesSuggestion = false;
				if (string.IsNullOrWhiteSpace(license.RightsStatement))
				{
					_licenseNotesLabel.Hide();
				}
				else
				{
					_licenseNotesLabel.Text = LocalizationManager.GetString("PublishTab.Upload.AdditionalRequests", "AdditionalRequests: ") + license.RightsStatement;
				}
			}
			else if (license is NullLicense)
			{
				_usingCcControls = false;
				_licenseNotesLabel.Text = LocalizationManager.GetString("PublishTab.Upload.AllReserved", "All rights reserved (Contact the Copyright holder for any permissions.)");
				if (!string.IsNullOrWhiteSpace(license.RightsStatement))
				{
					_licenseNotesLabel.Text += Environment.NewLine + license.RightsStatement;
				}
				_licenseSuggestion.Text = LocalizationManager.GetString("PublishTab.Upload.SuggestAssignCC", "Suggestion: Assigning a Creative Commons License makes it easy for you to clearly grant certain permissions to everyone.");

			}
			else
			{
				// So far, this means it must be custom license (with non-blank rights...actually, currently, the palaso dialog will not allow a custom license with no rights statement).
				_usingCcControls = false;
				_licenseNotesLabel.Text = license.RightsStatement;
				_licenseSuggestion.Text = LocalizationManager.GetString("PublishTab.Upload.SuggestChangeCC", "Suggestion: Creative Commons Licenses make it much easier for others to use your book, even if they aren't fluent in the language of your custom license.");
			}

			_copyrightLabel.Text = book.BookInfo.Copyright;

			var allLanguages = book.AllLanguages;
			foreach (var lang in allLanguages.Keys)
			{
				var checkBox = new CheckBox();
				checkBox.Text = _book.PrettyPrintLanguage(lang);
				if (allLanguages[lang])
					checkBox.Checked = true;
				else
				{
					checkBox.Text += @" " + LocalizationManager.GetString("PublishTab.Upload.Partial", "(partial)");
				}
				checkBox.Size = new Size(TextRenderer.MeasureText(checkBox.Text, checkBox.Font).Width + 50, checkBox.Height);
				checkBox.Tag = lang;
				checkBox.CheckStateChanged += delegate(object sender, EventArgs args)
				{
					bool someLangChecked = _languagesFlow.Controls.Cast<CheckBox>().Any(b => b.Checked);
					_langsLabel.ForeColor = someLangChecked ? Color.Black : Color.Red;
					if (_okToUploadDependsOnLangsChecked)
					{
						_okToUpload = someLangChecked;
						UpdateDisplay();
					}
				};
				_languagesFlow.Controls.Add(checkBox);
			}

			_creditsLabel.Text = book.BookInfo.Credits;
			_summaryBox.Text = book.BookInfo.Summary;

			try
			{
				_loginDialog.LogIn(); // See if saved credentials work.
			}
			catch (Exception e)
			{
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(e,
					LocalizationManager.GetString("PublishTab.Upload.LoginFailure",
						"Bloom could not log in to BloomLibrary.org using your saved credentials. Please check your network connection."));
			}
			_optional1.Left = _summaryBox.Right - _optional1.Width; // right-align these (even if localization changes their width)
			RequireValue(_copyrightLabel);
			RequireValue(_titleLabel);

			if (BookTransfer.UseSandbox)
			{
				var oldTextWidth = TextRenderer.MeasureText(_uploadButton.Text, _uploadButton.Font).Width;
				_uploadButton.Text = LocalizationManager.GetString("PublishTab.Upload.UploadSandbox","Upload Book (to Sandbox)");
				var neededWidth = TextRenderer.MeasureText(_uploadButton.Text, _uploadButton.Font).Width;
				_uploadButton.Width += neededWidth - oldTextWidth;
			}
			// After considering all the factors except whether any languages are selected,
			// if we can upload at this point, whether we can from here on depends on whether one is checked.
			// This test needs to come after evaluating everything else uploading depends on (except login)
			_okToUploadDependsOnLangsChecked = _okToUpload;
			if (!allLanguages.Keys.Any())
			{
				_langsLabel.Text += " " + LocalizationManager.GetString("PublishTab.Upload.NoLangsFound", "(None found)");
				_langsLabel.ForeColor = Color.Red;
				_okToUpload = false;
			}
		}

		void _progressBox_LinkClicked(object sender, LinkClickedEventArgs e)
		{
			Process.Start(e.LinkText);
		}

		protected override void OnSizeChanged(EventArgs e)
		{
			base.OnSizeChanged(e);
			AdjustControlPlacement();
		}

		/// <summary>
		/// Adjust things to look neat for the selected set of license controls and their content
		/// </summary>
		private void AdjustControlPlacement()
		{
			if (!_usingCcControls)
				_ccPanel.Hide();
			if (_usingNotesLabel)
				AdjustLabelSize(_licenseNotesLabel);
			else
				_licenseNotesLabel.Hide();
			if (_usingNotesSuggestion)
				AdjustLabelSize(_licenseSuggestion);
			else
				_licenseSuggestion.Hide();
			AdjustLabelSize(_creditsLabel);
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			UpdateDisplay(); // can't do in constructor, ProgressBox won't take messages until handle created.
		}

		// Make the label's size appropriate for showing its full contents (in the width currently assigned to the progress box).
		void AdjustLabelSize(Label label)
		{
			label.Size = TextRenderer.MeasureText(label.Text, label.Font,
				new Size(_progressBox.Width, int.MaxValue), TextFormatFlags.WordBreak);
			label.Height += 2; // Just a slight gap between paragraphs.
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
			_progressBox.Clear();
			if (!_uploadButton.Enabled)
			{
				if (!okToUpload)
				{
					_progressBox.WriteMessageWithColor(Color.Red, LocalizationManager.GetString("PublishTab.Upload.FieldsNeedAttention",
						"One or more fields above need your attention before uploading"));
				}
				if (!_bookTransferrer.LoggedIn)
				{
					_progressBox.WriteMessageWithColor(Color.Red, LocalizationManager.GetString("PublishTab.Upload.PleaseLogIn",
						"Please log in to BloomLibrary.org (or sign up) before uploading"));
				}
			}
			_loginLink.Text = _bookTransferrer.LoggedIn ? LocalizationManager.GetString("PublishTab.Upload.Logout", "Log out of BloomLibrary.org") : _originalLoginText;
			_signUpLink.Visible = !_bookTransferrer.LoggedIn;
			if (_bookTransferrer.LoggedIn)
			{
				_userId.Text = Settings.Default.WebUserId;
				_userId.Visible = true;
			}
			else
			{
				_userId.Visible = false;
			}
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

		private void _termsLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			Process.Start(BloomLibraryUrlPrefix + "/terms");
		}

		private void _signUpLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			_loginDialog.SignUp(this);
			UpdateDisplay();
		}

		void SetStateOfNonUploadControls(bool enable)
		{
			if (enable)
			{
				_uploadButton.Text = _originalUploadText;
			}
			else
			{
				_originalUploadText = _uploadButton.Text;
				_uploadButton.Text = LocalizationManager.GetString("Common.Cancel", "Cancel");
			}
			var parent = this.Parent;
			while (parent != null && !(parent is PublishView))
				parent = parent.Parent;
			if (parent == null)
				return;
			((PublishView)parent).SetStateOfNonUploadRadios(enable);
			while (parent != null && !(parent is WorkspaceView))
				parent = parent.Parent;
			if (parent == null)
				return;
			((WorkspaceView)parent).SetStateOfNonPublishTabs(enable);
		}

		private void _uploadButton_Click(object sender, EventArgs e)
		{
			if (_uploadWorker != null)
			{
				// We're already doing an upload, this is now the Cancel button.
				_progressBox.CancelRequested = true;
				return;
			}
			_progressBox.CancelRequested = false;
			ScrollControlIntoView(_progressBox);
			_progressBox.Clear();
			var info = _book.BookInfo;
			if (string.IsNullOrEmpty(info.Id))
			{
				info.Id = Guid.NewGuid().ToString();
			}
			info.Uploader = _bookTransferrer.UserId;

			if (_book.BookInfo.IsSuitableForMakingShells)
			{
				// Hopefully this message is never seen...there is supposed to be no way for an end user to create a template...so I think we can afford
				// not to burden localizers with it.
				if (MessageBox.Show(Form.ActiveForm,
					@"This book is marked as suitable for making shells, that is, a new template like Basic Book containing blank pages for authoring a new book. "
					+ @"Such books are normally only created and uploaded by HTML specialists. "
					+ @"If this book is intended as a shell to translate, something is wrong, and you should get expert help before uploading this book."
					+ "\n\n"
					+ @"Do you want to go ahead?",
					"Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
					return;
			}

			_progressBox.WriteMessage("Checking bloom version eligibility...");
			if (!_bookTransferrer.IsThisVersionAllowedToUpload())
			{
				MessageBox.Show(this,
					LocalizationManager.GetString("PublishTab.Upload.OldVersion",
						"Sorry, this version of Bloom Desktop is not compatible with the current version of BloomLibrary.org. Please upgrade to a newer version."),
					LocalizationManager.GetString("PublishTab.Upload.UploadNotAllowed", "Upload Not Allowed"),
					MessageBoxButtons.OK, MessageBoxIcon.Stop);
				_progressBox.WriteMessage("Canceled.");
				return;
			}

			// Todo: try to make sure it has a thumbnail.

			_progressBox.WriteMessage("Checking for existing copy on server...");
			if (_bookTransferrer.IsBookOnServer(_book.FolderPath))
			{
				using (var dlg = new OverwriteWarningDialog())
				{
					if (dlg.ShowDialog() == DialogResult.Cancel)
					{
						_progressBox.WriteMessage("Canceled.");
						return;
					}
				}
			}
			_progressBox.WriteMessage("Starting...");
			_uploadWorker = new BackgroundWorker();
			_uploadWorker.DoWork += BackgroundUpload;
			_uploadWorker.WorkerReportsProgress = true;
			_uploadWorker.RunWorkerCompleted += (theWorker, completedEvent) =>
			{
				// Return all controls to normal state. (Do this first, just in case we get some further exception somehow.)
				// I believe the event is guaranteed to be raised, even if something in the worker thread throws,
				// so there should be no way to get stuck in the state where the tabs etc. are disabled.
				SetStateOfNonUploadControls(true);
				// Don't call UpdateDisplay, it will wipe out the progress messages.
				if (_progressBox.CancelRequested)
				{
					_progressBox.WriteMessageWithColor(Color.Red, LocalizationManager.GetString("PublishTab.Upload.Cancelled", "Upload was cancelled"));
				}
				else {
					if (completedEvent.Error != null)
					{
						string errorMessage = LocalizationManager.GetString("PublishTab.Upload.ErrorUploading",
							"Sorry, there was a problem uploading {0}. Some details follow. You may need technical help.");
						_progressBox.WriteError(errorMessage, _book.Title);
						_progressBox.WriteException(completedEvent.Error);
					}
					else if (string.IsNullOrEmpty((string) completedEvent.Result))
					{
						// Something went wrong, typically already reported.
						string sorryMessage = LocalizationManager.GetString("PublishTab.Upload.FinalUploadFailureNotice",
							"Sorry, \"{0}\" was not successfully uploaded. Sometimes this is caused by temporary problems with the servers we use. It's worth trying again in an hour or two. If you regularly get this problem please report it to us.");
						_progressBox.WriteError(sorryMessage, _book.Title);
					}
					else
					{
						var url = BloomLibraryUrlPrefix + "/browse/detail/" + _parseId;
						string congratsMessage = LocalizationManager.GetString("PublishTab.Upload.UploadCompleteNotice",
							"Congratulations, \"{0}\" is now available on BloomLibrary.org ({1})");
						_progressBox.WriteMessageWithColor(Color.Blue, congratsMessage, _book.Title, url);
					}
				}
				_uploadWorker = null;
			};
			SetStateOfNonUploadControls(false); // Last thing we do before launching the worker, so we can't get stuck in this state.
			_uploadWorker.RunWorkerAsync(_book);
			//_bookTransferrer.UploadBook(_book.FolderPath, AddNotification);
		}

		public static string BloomLibraryUrlPrefix
		{
			get { return UrlLookup.LookupUrl(UrlType.LibrarySite, BookTransfer.UseSandbox) + "/#"; }
		}

		string _parseId;

		void BackgroundUpload(object sender, DoWorkEventArgs e)
		{
			var book = (Book.Book) e.Argument;
			var languages = _languagesFlow.Controls.Cast<CheckBox>().Where(b => b.Checked).Select(b => b.Tag).Cast<string>().ToArray();
			var result = _bookTransferrer.FullUpload(book, _progressBox, _parentView, languages, out _parseId);
			e.Result = result;
		}

		private void _summaryBox_TextChanged(object sender, EventArgs e)
		{
			_book.BookInfo.Summary = _summaryBox.Text;
			_book.BookInfo.Save(); // Review: is this too often?

		}

		private void _creativeCommonsLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			var url = ((CreativeCommonsLicense) _book.GetLicenseMetadata().License).Url;
			try
			{
				Process.Start(new ProcessStartInfo(url));
			}
			catch (Exception)
			{
				// Report a problem or just ignore it?
			}
		}
	}
}
