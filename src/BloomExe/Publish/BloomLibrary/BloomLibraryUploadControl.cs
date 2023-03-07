using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Collection;
using Bloom.MiscUI;
using Bloom.web;
using Bloom.web.controllers;
using Bloom.WebLibraryIntegration;
using Bloom.Workspace;
using L10NSharp;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.Publish.BloomLibrary
{
	/// <summary>
	/// This class replaces the AdobeReaderControl in the PublishView when the Upload To BloomLibrary.org option is selected.
	/// </summary>
	public partial class BloomLibraryUploadControl : UserControl
	{
		private readonly PublishView _parentView;
		private readonly string _originalLoginText;
		private bool _okToUpload;
		private readonly bool _okToUploadDependsOnLangsChecked;
		private readonly bool _usingNotesLabel = true;
		private readonly bool _usingNotesSuggestion = true;
		private readonly bool _usingCcControls = true;
		private BackgroundWorker _uploadWorker;
		private string _originalUploadText;
		private readonly BloomLibraryPublishModel _model;
		private IBloomWebSocketServer _webSocketServer;

		// We would love to be able to access this in the designer, but we can't...
		private readonly Padding _checkBoxMargin = new Padding(3, 3, 40, 3);

		private readonly string _pleaseSetThis = LocalizationManager.GetString("PublishTab.Upload.PleaseSetThis",
			"Please set this from the edit tab", "This shows next to the license, if the license has not yet been set.");
		public BloomLibraryUploadControl(PublishView parentView, BloomLibraryPublishModel model, IBloomWebSocketServer webSocketServer)
		{
			_model = model;
			_parentView = parentView;
			_webSocketServer = webSocketServer;
			InitializeComponent();
			_originalLoginText = _loginLink.Text; // Before anything might modify it (but after InitializeComponent creates it).
			_titleLabel.Text = _model.Title;

			_uploadSource.SelectedIndex = 0;

			_progressBox.ShowDetailsMenuItem = true;
			_progressBox.ShowCopyToClipboardMenuItem = true;
			_progressBox.LinkClicked += _progressBox_LinkClicked;

			_okToUpload = _model.MetadataIsReadyToPublish;

			// See if saved credentials work.
			try
			{
				_model.LogIn();
			}
			catch (Exception e)
			{
				LogAndInformButDontReportFailureToConnectToServer(e);
			}
			CommonApi.NotifyLogin(() =>
			{
				this.Invoke((Action)(UpdateDisplay));
				;
			});

			switch (_model.LicenseType)
			{
				case LicenseState.CreativeCommons:
					_creativeCommonsLink.Text = _model.LicenseToken;
					_usingNotesSuggestion = false;
					if (string.IsNullOrWhiteSpace(_model.LicenseRights))
					{
						_licenseNotesLabel.Hide();
					}
					else
					{
						_licenseNotesLabel.Text = LocalizationManager.GetString("PublishTab.Upload.AdditionalRequests", "Additional Requests: ") + _model.LicenseRights;
					}
					break;
				case LicenseState.Null:
					_usingCcControls = false;
					_licenseNotesLabel.Text = LocalizationManager.GetString("PublishTab.Upload.AllReserved", "All rights reserved (Contact the Copyright holder for any permissions.)");
					if (!string.IsNullOrWhiteSpace(_model.LicenseRights))
					{
						_licenseNotesLabel.Text += Environment.NewLine + _model.LicenseRights;
					}
					_licenseSuggestion.Text = LocalizationManager.GetString("PublishTab.Upload.SuggestAssignCC", "Suggestion: Assigning a Creative Commons License makes it easy for you to clearly grant certain permissions to everyone.");
					break;
				case LicenseState.Custom:
					// This must be custom a license (with non-blank rights...actually,
					// currently, the palaso dialog will not allow a custom license with no rights statement).
					_usingCcControls = false;
					_licenseNotesLabel.Text = _model.LicenseRights;
					_licenseSuggestion.Text = LocalizationManager.GetString("PublishTab.Upload.SuggestChangeCC", "Suggestion: Creative Commons Licenses make it much easier for others to use your book, even if they aren't fluent in the language of your custom license.");
					break;
				default:
					throw new ApplicationException("Unknown License state.");
			}

			_copyrightLabel.Text = _model.Copyright;
			_creditsLabel.Text = _model.Credits;
			_summaryBox.Text = _model.Summary;
			if (!_model.Book.IsSaveable)
			{
				_summaryBox.Enabled = false;
				_summaryOptionalLabel.Text = LocalizationManager.GetString("TeamCollection.OptionalCheckOutEdit",
					"optional--check out to edit");
			}

			_model.InitializeLanguages();

			UpdateFeaturesCheckBoxesDisplay();

			var languageSelectionsForThisBook = _model.Book.BookInfo.PublishSettings.BloomLibrary.TextLangs;
			var allLanguages = _model.AllLanguages;
			foreach (var lang in allLanguages.Keys)
			{
				var checkBox = new CheckBox();
				checkBox.UseMnemonic = false;
				checkBox.Text = _model.PrettyLanguageName(lang);
				if (languageSelectionsForThisBook[lang].IsIncluded())
					checkBox.Checked = true;
				if (!allLanguages[lang])
				{
					checkBox.Text += @" " + LocalizationManager.GetString("PublishTab.Upload.IncompleteTranslation",
						"(incomplete translation)",
						"This is added after the language name, in order to indicate that some parts of the book have not been translated into this language yet.");
				}
				// Disable clicking on languages that have been selected for display in this book.
				// See https://issues.bloomlibrary.org/youtrack/issue/BL-7166.
				if (BloomLibraryPublishModel.IsRequiredLanguageForBook(lang, _model.Book))
				{
					checkBox.Checked = true;	// even if partial
					checkBox.AutoCheck = false;
					checkBox.Enabled = false;
				}
				checkBox.Margin = _checkBoxMargin;
				checkBox.AutoSize = true;
				checkBox.Tag = lang;
				checkBox.CheckStateChanged += delegate(object sender, EventArgs args)
				{
					var changedCheckBox = (CheckBox)sender;
					_model.SaveTextLanguageSelection((string)changedCheckBox.Tag, changedCheckBox.Checked);

					_langsLabel.ForeColor = LanguagesOkToUpload ? Color.Black : Color.Red;
					if (_okToUploadDependsOnLangsChecked)
					{
						// Note: We no longer prevent the user from uploading if there are no text languages. Just provide the red warning text.
						_okToUpload = true;	// Used to be _okToUpload = LanguagesOkToUpload
						UpdateDisplay();
					}
				};
				_languagesFlow.Controls.Add(checkBox);
			}

			UpdateAudioCheckBoxDisplay();

			_summaryOptionalLabel.Left = _summaryBox.Right - _summaryOptionalLabel.Width; // right-align these (even if localization changes their width)
			// Copyright info is not required if the book has been put in the public domain
			// or if we are publishing from a source collection and we have original copyright info
			if (!_model.IsBookPublicDomain && !_model.HasOriginalCopyrightInfoInSourceCollection)
				RequireValue(_copyrightLabel);
			RequireValue(_titleLabel);

			if (BookUpload.UseSandbox)
			{
				var oldTextWidth = TextRenderer.MeasureText(_uploadButton.Text, _uploadButton.Font).Width;
				// Do not localize the following string (https://issues.bloomlibrary.org/youtrack/issue/BL-7383).
				_uploadButton.Text = "Upload (to dev.bloomlibrary.org)";
				var neededWidth = TextRenderer.MeasureText(_uploadButton.Text, _uploadButton.Font).Width;
				_uploadButton.Width += neededWidth - oldTextWidth;
			}

			LibraryPublishApi_Obsolete.SetUploadControl = this; // Needed to get the Upload Id Collision dialog working.

			// After considering all the factors except whether any languages are selected,
			// if we can upload at this point, whether we can from here on depends on whether one is checked.
			// This test needs to come after evaluating everything else uploading depends on (except login)
			_okToUploadDependsOnLangsChecked = _okToUpload;
			if (allLanguages.Keys.Any())
				return;
			// No languages in the book have complete data
			const string space = " ";
			_langsLabel.Text += space + LocalizationManager.GetString("PublishTab.Upload.NoLangsFound", "(None found)");
			if (!_model.OkToUploadWithNoLanguages)
			{
				_langsLabel.ForeColor = Color.Red;
				// Note: We no longer prevent the user from uploading if there are no text languages. Just provide the red warning text.
				// _okToUpload = false;
			}
		}

		private void UpdateFeaturesCheckBoxesDisplay()
		{
			var bookInfoMetaData = _model.Book.BookInfo.MetaData;
			var hasImageDesc = _model.Book.OurHtmlDom.GetLangCodesWithImageDescription().Any();

			_blindCheckBox.Enabled = hasImageDesc;
			_blindCheckBox.Checked = hasImageDesc && bookInfoMetaData.Feature_Blind;
			_signLanguageCheckBox.Enabled = _model.Book.HasSignLanguageVideos();
			_signLanguageCheckBox.Checked = _signLanguageCheckBox.Enabled && _model.IsPublishSignLanguage();

			// Set Sign Language link
			_changeSignLanguageLinkLabel.Visible = _signLanguageCheckBox.Checked;
			if (!string.IsNullOrEmpty(CurrentSignLanguageName))
			{
				_changeSignLanguageLinkLabel.Text = CurrentSignLanguageName;
			}
		}

		private void UpdateAudioCheckBoxDisplay()
		{
			var book = _model.Book;

			_narrationAudioCheckBox.Checked = book.BookInfo.PublishSettings.BloomLibrary.IncludeAudio;

			if (!book.Storage.GetNarrationAudioFileNamesReferencedInBook(false)
				.Any(fileName => RobustFile.Exists(Path.Combine(AudioProcessor.GetAudioFolderPath(book.FolderPath), fileName))))
			{
				_narrationAudioCheckBox.Enabled = false;
				_narrationAudioCheckBox.Checked = false;
			}
			if (!book.Storage.GetBackgroundMusicFileNamesReferencedInBook()
				.Any(fileName => RobustFile.Exists(Path.Combine(AudioProcessor.GetAudioFolderPath(book.FolderPath), fileName))))
			{
				_backgroundMusicCheckBox.Enabled = false;
				_backgroundMusicCheckBox.Checked = false;
			}
		}

		private bool LanguagesOkToUpload =>
			_model.OkToUploadWithNoLanguages || _languagesFlow.Controls.Cast<CheckBox>().Any(b => b.Checked);

		private void LogAndInformButDontReportFailureToConnectToServer(Exception exc)
		{
			var msg = LocalizationManager.GetString("PublishTab.Upload.LoginFailure",
				"Bloom could not sign in to BloomLibrary.org using your saved credentials. Please check your network connection.");
			MessageBox.Show(this, msg, FirebaseLoginDialog.LoginFailureString, MessageBoxButtons.OK, MessageBoxIcon.Error);
			Logger.WriteEvent("Failure connecting to parse server " + exc.Message);
		}

		void _progressBox_LinkClicked(object sender, LinkClickedEventArgs e)
		{
			SIL.Program.Process.SafeStart(e.LinkText);
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
		private void AdjustLabelSize(Label label)
		{
			label.Size = TextRenderer.MeasureText(label.Text, label.Font,
				new Size(_progressBox.Width, int.MaxValue), TextFormatFlags.WordBreak);
			label.Height += 2; // Just a slight gap between paragraphs.
		}

		private void RequireValue(Label item)
		{
			if (string.IsNullOrWhiteSpace(item.Text))
			{
				item.Text = _pleaseSetThis;
				item.ForeColor = Color.Red;
			}
		}

		private void UpdateDisplay()
		{
			
//TODO I would really like something like this, but at the moment the code is so convoluted and you can't set it once someone has checked it. Grrrr.
 //#if !DEBUG
 _targetProduction.Visible = false;
//#endif
	//		 _targetProduction.Checked = !BookUpload.UseSandbox;
		

			_uploadButton.Enabled = _model.MetadataIsReadyToPublish && _model.LoggedIn && _okToUpload;
			_progressBox.Clear();
			_uploadSource.Enabled = _uploadButton.Enabled;

			_uploadSource.Visible = _model.Book.CollectionSettings.HaveEnterpriseSubscription;


			if (_uploadSource.SelectedIndex != 0)
			{
				// we are uploading a collection a collection of a folders, so shorten the "Upload Book" label.
				// This is not localized. Also, once it is shown, it will not go back to the old label.
				// I don't want to go  to the trouble, because only handful of people will ever see this.
				_uploadButton.Text = "Upload"; 
			}

			if (!_uploadButton.Enabled)
			{
				if (!_okToUpload)
				{
					_progressBox.WriteMessageWithColor(Color.Red, LocalizationManager.GetString("PublishTab.Upload.FieldsNeedAttention",
						"One or more fields above need your attention before uploading."));
				}
				if (!_model.LoggedIn)
				{
					_progressBox.WriteMessageWithColor(Color.Red, LocalizationManager.GetString("PublishTab.Upload.PleaseLogIn",
						"Please sign in to BloomLibrary.org (or sign up) before uploading"));
				}
			}
			_loginLink.Text = _model.LoggedIn ? LocalizationManager.GetString("PublishTab.Upload.Logout", "Sign out of BloomLibrary.org") : _originalLoginText;
			if (_model.LoggedIn)
			{
				_userId.Text = _model.WebUserId;
				_userId.Visible = true;
			}
			else
			{
				_userId.Visible = false;
			}
		}

		private void _loginLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			if (_model.LoggedIn)
			{
				// This becomes a logout button
				_model.Logout();
			}
			else
			{
				// The dialog is configured by Autofac to interact with the single instance of BloomParseClient,
				// which it will update with all the relevant information if login is successful.
				FirebaseLoginDialog.ShowFirebaseLoginDialog(_webSocketServer);
			}
			UpdateDisplay();
		}

		private void _termsLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			SIL.Program.Process.SafeStart(BloomLibraryUrls.BloomLibraryUrlPrefix + "/page/termsOfUse");
		}

		private void SetStateOfNonUploadControls(bool enable)
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
			SetParentControlsState(enable);
		}

		private void SetParentControlsState(bool enable)
		{
			var parent = this.Parent;
			while (parent != null && !(parent is PublishView))
				parent = parent.Parent;
			if (parent == null)
				return;
			((PublishView) parent).SetStateOfNonUploadRadios(enable);

			while (parent != null && !(parent is WorkspaceView))
				parent = parent.Parent;
			((WorkspaceView) parent)?.SetStateOfNonPublishTabs(enable);
		}

		private void _uploadButton_Click(object sender, EventArgs e)
		{
			// for now, we're limiting this to projects that have set up a default bookshelf
			// so that all their books go to the correct place.
			var missingDefaultBookshelf = string.IsNullOrEmpty(_model.Book.CollectionSettings.DefaultBookshelf);
			if ( _uploadSource.SelectedIndex > 0 && missingDefaultBookshelf)
			{
				// Intentionally not localized ( because it's complicated, rare, and generally advanced )
				const string msg = "Before sending all of your books to BloomLibrary.org, you probably want to tell " +
				                   "Bloom which bookshelf this collection belongs in. Please go to Collection Tab : Settings: " +
				                   "Book Making and set the \"Bloom Library Bookshelf\".";
				BloomMessageBox.ShowInfo(msg);
				return;
			}
			if (_uploadSource.SelectedIndex == 1)
			{
				BulkUploadThisCollection();
				return;
			}
			if (_uploadSource.SelectedIndex == 2)
			{
				SelectFolderAndUploadCollectionsWithinIt();
				return;
			}
			if (_uploadWorker != null)
			{
				// We're already doing an upload, this is now the Cancel button.
				_progressBox.CancelRequested = true;
				return;
			}
			_progressBox.CancelRequested = false;
			ScrollControlIntoView(_progressBox);
			_progressBox.Clear();

			if (_signLanguageCheckBox.Checked && string.IsNullOrEmpty(CurrentSignLanguageName))
			{
				// report error in progress and bail
				_progressBox.WriteMessageWithColor(Color.Red,
					LocalizationManager.GetString("PublishTab.Upload.ChooseSignLanguageWarning",
					"Please choose the sign language for this book"));
				return;
			}

			if (_model.IsTemplate)
			{
				var msg = LocalizationManager.GetString("PublishTab.Upload.Template",
					"This book seems to be a template, that is, it contains blank pages for authoring a new book "
					+ "rather than content to translate into other languages. "
					+ "If that is not what you intended, you should get expert help before uploading this book."
					+ "\n\n"
					+ "Do you want to go ahead?");
				var warning = LocalizationManager.GetString("Warning", "Warning");
				if (MessageBox.Show(Form.ActiveForm, msg,
					warning, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
					return;
			}

			try
			{
				_progressBox.WriteMessage("Checking bloom version eligibility...");
				if (!_model.IsThisVersionAllowedToUpload)
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
				// If we don't need to trigger our upload ID Collision dialog, go ahead and upload the book.
				if (!_model.BookIsAlreadyOnServer)
				{
					UploadBook();
					return;
				}
				// OK, so we do need our uploadIDCollision dialog.
				ShowIdCollisionDialog(LanguagesCheckedToUpload, _signLanguageCheckBox.Checked);
			}
			catch (Exception)
			{
				ReportTryAgainDuringUpload();
				_uploadButton.Enabled = true;
			}
		}

		public void ShowIdCollisionDialog(IEnumerable<string> languagesToUpload, bool signLanguageFeatureSelected)
		{
			using (var dlg = new ReactDialog("uploadIDCollisionDlgBundle",
				_model.GetUploadCollisionDialogProps(languagesToUpload, signLanguageFeatureSelected)
			))
			{
				dlg.Width = 770;
				dlg.Height = 570;
				dlg.ControlBox = false;
				dlg.ShowDialog(Shell.GetShellOrOtherOpenForm());
			}
		}

		public void CancelUpload()
		{
			_progressBox.WriteMessage("Canceled.");

		}

		public void UploadBookAfterChangingId()
		{
			_model.ChangeBookId(_progressBox);
			UploadBook();
		}

		public void UploadBook()
		{
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
					_progressBox.WriteMessageWithColor(Color.Red,
						LocalizationManager.GetString("PublishTab.Upload.Cancelled", "Upload was cancelled"));
				}
				else
				{
					if (completedEvent.Error != null)
					{
						string errorMessage = GetBasicErrorUploadingMessage();
						_progressBox.WriteError(errorMessage, _model.Title);
						_progressBox.WriteException(completedEvent.Error);
					}
					else if ((string) completedEvent.Result == "quiet")
					{
						// no more reporting, sufficient message already given.
					}
					else if (string.IsNullOrEmpty((string) completedEvent.Result))
					{
						// Something went wrong, possibly already reported.
						if (!_model.PdfGenerationSucceeded)
							ReportPdfGenerationFailed();
						else
							ReportTryAgainDuringUpload();
					}
					else
					{
						var url = BloomLibraryUrls.BloomLibraryDetailPageUrlFromBookId(_parseId,true);
						string congratsMessage = LocalizationManager.GetString("PublishTab.Upload.UploadCompleteNotice",
							"Congratulations, \"{0}\" is now available on BloomLibrary.org ({1})",
							"{0} is the book title; {1} is a clickable url which will display the book on the website");
						_progressBox.WriteMessageWithColor(Color.Blue, congratsMessage, _model.Title, url);
						_model.AddHistoryRecordForLibraryUpload(url);
					}
				}

				_uploadWorker = null;
			};
			SetStateOfNonUploadControls(false); // Last thing we do before launching the worker, so we can't get stuck in this state.
			_uploadWorker.RunWorkerAsync(_model);
		}

		private string GetBasicErrorUploadingMessage()
		{
			return LocalizationManager.GetString("PublishTab.Upload.ErrorUploading",
				"Sorry, there was a problem uploading {0}. Some details follow. You may need technical help.");
		}

		private void ReportPdfGenerationFailed()
		{
			string message = GetBasicErrorUploadingMessage();
			_progressBox.WriteError(message, _model.Title);
			message = LocalizationManager.GetString("PublishTab.PdfMaker.BadPdfShort", "Bloom had a problem making a PDF of this book.");
			_progressBox.WriteError(message, _model.Title);
		}

		private void ReportTryAgainDuringUpload()
		{
			string sorryMessage = LocalizationManager.GetString("PublishTab.Upload.FinalUploadFailureNotice",
				"Sorry, \"{0}\" was not successfully uploaded. Sometimes this is caused by temporary problems with the servers we use. It's worth trying again in an hour or two. If you regularly get this problem please report it to us.");
			_progressBox.WriteError(sorryMessage, _model.Title);
		}

		private IEnumerable<string> LanguagesCheckedToUpload
		{
			get
			{
				return _languagesFlow.Controls.Cast<CheckBox>().
					Where(b => b.Checked).Select(b => b.Tag).Cast<string>();
			}
		}

		string _parseId;

		void BackgroundUpload(object sender, DoWorkEventArgs e)
		{
			var model = (BloomLibraryPublishModel)e.Argument;
			var book = model.Book;
			var checker = new LicenseChecker();

			var message = checker.CheckBook(book, LanguagesCheckedToUpload.ToArray());
			if (message != null)
			{
				// Using WriteError allows users to send bug reports: this isn't a bug!
				_progressBox.WriteMessageWithColor(Color.Red, message);
				e.Result = "quiet"; // suppress other completion/fail messages
				return;
			}

			_model.UpdateBookMetadataFeatures(
				_blindCheckBox.Checked,
				_narrationAudioCheckBox.Checked,
				_signLanguageCheckBox.Checked);

			var includeBackgroundMusic = _backgroundMusicCheckBox.Checked;
			var result = _model.UploadOneBook(book, _progressBox, _parentView, !includeBackgroundMusic, out _parseId);
			e.Result = result;
		}

		private void _summaryBox_TextChanged(object sender, EventArgs e)
		{
			_model.Summary = _summaryBox.Text;
		}

		private void _creativeCommonsLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			var url = _model.CCLicenseUrl;
			if (url == null)
				return;
			try
			{
				SIL.Program.Process.SafeStart(url);
			}
			catch (Exception)
			{
				// Report a problem or just ignore it?
			}
		}

		private void _signLanguageCheckBox_CheckedChanged(object sender, EventArgs e)
		{
			_changeSignLanguageLinkLabel.Visible = _signLanguageCheckBox.Checked;

			if (!_signLanguageCheckBox.Checked)
			{
				_model.ClearSignLanguageToPublish();
			}
			else if (!string.IsNullOrEmpty(_model.Book.CollectionSettings.SignLanguageTag))
			{
				_model.SetOnlySignLanguageToPublish(_model.Book.CollectionSettings.SignLanguageTag);
			}
		}

		private void _changeSignLanguageLinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			var collectionSettings = _model.Book.CollectionSettings;
			var l = CollectionSettingsDialog.ChangeLanguage(collectionSettings.SignLanguageTag, CurrentSignLanguageName, false);
			if (l == null)
			{
				// no change; dialog cancelled
				return;
			}
			_changeSignLanguageLinkLabel.Text = l.DesiredName;
			// How to know if the new sign language name is custom or not!?
			// 1- set the Tag (which also sets the Name to the non-custom default
			// 2- read the Name
			// 3- if it's not the same as DesiredName, the new name is custom
			collectionSettings.SignLanguageTag = l.LanguageTag;
			var slIsCustom = collectionSettings.SignLanguage.Name != l.DesiredName;
			collectionSettings.SignLanguage.SetName(l.DesiredName, slIsCustom);
			collectionSettings.Save();

			_model.SetOnlySignLanguageToPublish(collectionSettings.SignLanguageTag);
		}

		private string CurrentSignLanguageName
		{
			get
			{
				return _model.Book.CollectionSettings.SignLanguage.Name;
			}
		}

		private void _blindCheckBox_CheckedChanged(object sender, EventArgs e)
		{
			if (!_blindCheckBox.Checked)
			{
				_model.ClearBlindAccessibleToPublish();
			}
			else
			{
				_model.SetOnlyBlindAccessibleToPublish(LanguagesCheckedToUpload.FirstOrDefault());
			}
		}

		private void SelectFolderAndUploadCollectionsWithinIt()
		{
			var folderPath = MiscUI.BloomFolderChooser.ChooseFolder(_model.Book.CollectionSettings.FolderPath);
			if (!String.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
				BulkUpload(folderPath);
		}

		private void BulkUploadThisCollection()
		{
			BulkUpload(_model.Book.CollectionSettings.FolderPath);
		}

		private void BulkUpload(string rootFolderPath)
		{
			_progressBox.Clear();
			_model.BulkUpload(rootFolderPath, _progressBox);
		}

		private void _uploadSource_SelectedIndexChanged(object sender, EventArgs e)
		{
			UpdateDisplay();
		}

		private void _targetProduction_CheckedChanged(object sender, EventArgs e)
		{
			BookUpload.Destination =
				_targetProduction.Checked ? UploadDestination.Production : UploadDestination.Development;
		}

		private void _narrationAudioCheckBox_CheckedChanged(object sender, EventArgs e)
		{
			_model.SaveAudioLanguageSelection(_narrationAudioCheckBox.Checked);
		}
	}
}
