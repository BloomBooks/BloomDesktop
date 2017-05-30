using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using Bloom.Collection;
using Bloom.CollectionTab;
using Bloom.Edit;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.Publish;
using Bloom.Registration;
using Bloom.web;
using L10NSharp;
using Messir.Windows.Forms;
using SIL.IO;
using SIL.Reporting;
using SIL.Windows.Forms.ReleaseNotes;
using SIL.Windows.Forms.SettingProtection;
using System.Collections.Generic;

namespace Bloom.Workspace
{
	public partial class WorkspaceView : UserControl
	{
		private readonly WorkspaceModel _model;
		private readonly CollectionSettingsDialog.Factory _settingsDialogFactory;
		private readonly SelectedTabAboutToChangeEvent _selectedTabAboutToChangeEvent;
		private readonly SelectedTabChangedEvent _selectedTabChangedEvent;
		private readonly LocalizationChangedEvent _localizationChangedEvent;
		private readonly FeedbackDialog.Factory _feedbackDialogFactory;
		private readonly ProblemReporterDialog.Factory _problemReportDialogFactory;
#if CHORUS
			private readonly ChorusSystem _chorusSystem;
#else
		private readonly object _chorusSystem;
#endif
		private bool _viewInitialized;
		private int _originalToolStripPanelWidth;
		private int _originalToolSpecificPanelHorizPos;
		private int _originalUiMenuWidth;
		private int _stage1SpaceSaved;
		private int _stage2SpaceSaved;
		private string _originalSettingsText;
		private string _originalCollectionText;
		private string _originalHelpText;
		private Image _originalHelpImage;
		private string _originalUiLanguageSelection;
		private LibraryView _collectionView;
		private EditingView _editingView;
		private PublishView _publishView;
		private Control _previouslySelectedControl;
		public event EventHandler CloseCurrentProject;
		public event EventHandler ReopenCurrentProject;
		private readonly LocalizationManager _localizationManager;
		public static float DPIOfThisAccount;
		private ZoomControl _zoomControl;

		public delegate WorkspaceView Factory(Control libraryView);

//autofac uses this

		public WorkspaceView(WorkspaceModel model,
							 Control libraryView,
							 EditingView.Factory editingViewFactory,
							 PublishView.Factory pdfViewFactory,
							 CollectionSettingsDialog.Factory settingsDialogFactory,
							 EditBookCommand editBookCommand,
							SendReceiveCommand sendReceiveCommand,
							 SelectedTabAboutToChangeEvent selectedTabAboutToChangeEvent,
							SelectedTabChangedEvent selectedTabChangedEvent,
							LocalizationChangedEvent localizationChangedEvent,
							 FeedbackDialog.Factory feedbackDialogFactory,
							ProblemReporterDialog.Factory problemReportDialogFactory,
							//ChorusSystem chorusSystem,
							LocalizationManager localizationManager

			)
		{
			_model = model;
			_settingsDialogFactory = settingsDialogFactory;
			_selectedTabAboutToChangeEvent = selectedTabAboutToChangeEvent;
			_selectedTabChangedEvent = selectedTabChangedEvent;
			_localizationChangedEvent = localizationChangedEvent;
			_feedbackDialogFactory = feedbackDialogFactory;
			_problemReportDialogFactory = problemReportDialogFactory;
			//_chorusSystem = chorusSystem;
			_localizationManager = localizationManager;
			_model.UpdateDisplay += new System.EventHandler(OnUpdateDisplay);
			InitializeComponent();

			_checkForNewVersionMenuItem.Visible = SIL.PlatformUtilities.Platform.IsWindows;

			_toolStrip.Renderer = new NoBorderToolStripRenderer();

			//we have a number of buttons which don't make sense for the remote (therefore vulnerable) low-end user
			//_settingsLauncherHelper.CustomSettingsControl = _toolStrip;
			//NB: these aren't really settings, but we're using that feature to simplify this menu down to what makes sense for the easily-confused user
			_settingsLauncherHelper.ManageComponent(_keyBloomConceptsMenuItem);
			_settingsLauncherHelper.ManageComponent(_makeASuggestionMenuItem);
			_settingsLauncherHelper.ManageComponent(_webSiteMenuItem);
			_settingsLauncherHelper.ManageComponent(_showLogMenuItem);
			_settingsLauncherHelper.ManageComponent(_releaseNotesMenuItem);
			_settingsLauncherHelper.ManageComponent(_divider2);
			_settingsLauncherHelper.ManageComponent(_divider3);
			_settingsLauncherHelper.ManageComponent(_divider4);

			OnSettingsProtectionChanged(this, null);//initial setup
			SettingsProtectionSettings.Default.PropertyChanged += new System.ComponentModel.PropertyChangedEventHandler(OnSettingsProtectionChanged);


			_uiLanguageMenu.Visible = true;
			_settingsLauncherHelper.ManageComponent(_uiLanguageMenu);

			editBookCommand.Subscribe(OnEditBook);
			sendReceiveCommand.Subscribe(OnSendReceive);

			//Cursor = Cursors.AppStarting;
			Application.Idle += new EventHandler(Application_Idle);
			Text = _model.ProjectName;

			//SetupTabIcons();

			//
			// _collectionView
			//
			this._collectionView = (LibraryView) libraryView;
			_collectionView.ManageSettings(_settingsLauncherHelper);
			this._collectionView.Dock = System.Windows.Forms.DockStyle.Fill;

			//
			// _editingView
			//
			this._editingView = editingViewFactory();
			this._editingView.Dock = System.Windows.Forms.DockStyle.Fill;

			//
			// _pdfView
			//
			this._publishView = pdfViewFactory();
			this._publishView.Dock = System.Windows.Forms.DockStyle.Fill;

			_collectionTab.Tag = _collectionView;
			_publishTab.Tag = _publishView;
			_editTab.Tag = _editingView;

			this._collectionTab.Text = _collectionView.CollectionTabLabel;

			SetTabVisibility(_publishTab, false);
			SetTabVisibility(_editTab, false);

//			if (Program.StartUpWithFirstOrNewVersionBehavior)
//			{
//				_tabStrip.SelectedTab = _infoTab;
//				SelectPage(_infoView);
//			}
//			else
//			{
				_tabStrip.SelectedTab = _collectionTab;
				SelectPage(_collectionView);
//			}

			if (SIL.PlatformUtilities.Platform.IsMono)
			{
				// Without this adjustment, we lose some controls on smaller resolutions.
				AdjustToolPanelLocation(true);
				// in mono auto-size causes the height of the tab strip to be too short
				_tabStrip.AutoSize = false;
			}

			SetupUiLanguageMenu();
			SetupZoomControl();
			AdjustButtonTextsForLocale();
			_viewInitialized = false;
		}

		private void SetupZoomControl()
		{
			_zoomControl = new ZoomControl();
			_zoomWwrapper = new ToolStripControlHost(_zoomControl);
			// We're using a ToolStrip to display these three controls in the top right, and it does a nice job
			// of stretching the width to match localization. But height and spacing we must control exactly,
			// or it goes into an overflow mode that is very ugly.
			_zoomWwrapper.Margin = Padding.Empty;
		}

		private int TabButtonSectionWidth
		{
			get { return _tabStrip.Items.Cast<TabStripButton>().Sum(tab => tab.Width) + 10; }
		}

		/// <summary>
		/// Adjusts the tool panel location to allow more (or optionally less) space
		/// for the tab buttons.
		/// </summary>
		void AdjustToolPanelLocation(bool allowNarrowing)
		{
			var widthOfTabButtons = TabButtonSectionWidth;
			var location = _toolSpecificPanel.Location;
			if (widthOfTabButtons > location.X || allowNarrowing)
			{
				location.X = widthOfTabButtons;
				_toolSpecificPanel.Location = location;
			}
		}

		/// <summary>
		/// Adjust the tool panel location when the chosen localization changes.
		/// See https://jira.sil.org/browse/BL-1212 for what can happen if we
		/// don't adjust.  At the moment, we only widen, we never narrow the
		/// overall area allotted to the tab buttons.  Button widths adjust
		/// themselves automatically to their Text width.  There doesn't seem
		/// to be a built-in mechanism to limit the width to a given maximum
		/// so we implement such an operation ourselves.
		/// </summary>
		void HandleTabTextChanged(object sender, EventArgs e)
		{
			var btn = sender as Messir.Windows.Forms.TabStripButton;
			if (btn != null)
			{
				const string kEllipsis = "\u2026";
				// Preserve the original string as the tooltip.
				if (!btn.Text.EndsWith(kEllipsis))
					btn.ToolTipText = btn.Text;
				// Ensure the button width is no more than 110 pixels.
				if (btn.Width > 110)
				{
					using (Graphics g = btn.Owner.CreateGraphics())
					{
						btn.Text = ShortenStringToFit(btn.Text, 110, btn.Width, btn.Font, g);
					}
				}
			}
			AdjustToolPanelLocation(false);
		}

		/// <summary>
		/// Ensure that the TabStripItem or Control or Whatever is no wider than desired by
		/// truncating the Text as needed, with an ellipsis appended to show truncation has
		/// occurred.
		/// </summary>
		/// <returns>the possibly shortened string</returns>
		/// <param name="text">the string to shorten if necessary</param>
		/// <param name="maxWidth">the maximum item width allowed</param>
		/// <param name="originalWidth">the original item width (with the original string)</param>
		/// <param name="font">the font to use</param>
		/// <param name="g">the relevant Graphics object for drawing/measuring</param>
		/// <remarks>Would this be a good library method somewhere?  Where?</remarks>
		public static string ShortenStringToFit(string text, int maxWidth, int originalWidth, Font font, Graphics g)
		{
			const string kEllipsis = "\u2026";
			var txtWidth = g.MeasureString(text, font).Width;
			var padding = originalWidth - txtWidth;
			while (txtWidth + padding > maxWidth)
			{
				var len = text.Length - 2;
				if (len <= 0)
					break;	// I can't conceive this happening, but I'm also paranoid.
				text = text.Substring(0, len) + kEllipsis;	// trim, add ellipsis
				txtWidth = g.MeasureString(text, font).Width;
			}
			return text;
		}

		private void _applicationUpdateCheckTimer_Tick(object sender, EventArgs e)
		{
			_applicationUpdateCheckTimer.Enabled = false;
			if (!Debugger.IsAttached)
			{
				ApplicationUpdateSupport.CheckForASquirrelUpdate(ApplicationUpdateSupport.BloomUpdateMessageVerbosity.Quiet, newInstallDir => RestartBloom(newInstallDir), Settings.Default.AutoUpdate);
			}
		}

		private void OnSendReceive(object obj)
		{
#if CHORUS
			using (SyncDialog dlg = (SyncDialog) _chorusSystem.WinForms.CreateSynchronizationDialog())
			{
				dlg.ShowDialog();
				if(dlg.SyncResult.DidGetChangesFromOthers)
				{
					Invoke(ReopenCurrentProject);
				}
			}
#endif
		}

		void OnSettingsProtectionChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			//when we need to use Ctrl+Shift to display stuff, we don't want it also firing up the localization dialog (which shouldn't be done by a user under settings protection anyhow)

			LocalizationManager.EnableClickingOnControlToBringUpLocalizationDialog = !SettingsProtectionSettings.Default.NormallyHidden;
		}

		private void SetupUiLanguageMenu()
		{
			SetupUiLanguageMenuCommon(_uiLanguageMenu, FinishUiLanguageMenuItemClick);

			_uiLanguageMenu.DropDownItems.Add(new ToolStripSeparator());
			var menu = _uiLanguageMenu.DropDownItems.Add(LocalizationManager.GetString("CollectionTab.MoreLanguagesMenuItem", "More..."));
			menu.Click += new EventHandler((a, b) =>
			{
				_localizationManager.ShowLocalizationDialogBox(false);
				SetupUiLanguageMenu();
				LocalizationManager.ReapplyLocalizationsToAllObjectsInAllManagers(); //review: added this based on its name... does it help?
				_localizationChangedEvent.Raise(null);
				// The following is needed for proper display on Linux, and doesn't hurt anything on Windows.
				// See http://issues.bloomlibrary.org/youtrack/issue/BL-3444.
				AdjustButtonTextsForLocale();
			});
		}

		/// <summary>
		/// This is also called by CollectionChoosing.OpenCreateCloneControl
		/// </summary>
		public static void SetupUiLanguageMenuCommon(ToolStripDropDownButton uiMenuControl, Action finishClickAction = null)
		{
			uiMenuControl.DropDownItems.Clear();
			foreach (var lang in LocalizationManager.GetUILanguages(true))
			{
				var item = uiMenuControl.DropDownItems.Add(MenuItemName(lang));
				item.Tag = lang;
				item.Click += (sender, args) => UiLanguageMenuItemClickHandler(uiMenuControl, sender as ToolStripItem, finishClickAction);
				if (lang.IetfLanguageTag == Settings.Default.UserInterfaceLanguage)
					UpdateMenuTextToShorterNameOfSelection(uiMenuControl, lang);
			}
		}

		private static void UiLanguageMenuItemClickHandler(ToolStripDropDownButton toolStripButton, ToolStripItem item, Action finishClickAction)
		{
			var tag = (CultureInfo)item.Tag;

			LocalizationManager.SetUILanguage(tag.IetfLanguageTag, true);
			Settings.Default.UserInterfaceLanguage = tag.IetfLanguageTag;
			Settings.Default.UserInterfaceLanguageSetExplicitly = true;
			Settings.Default.Save();
			item.Select();
			UpdateMenuTextToShorterNameOfSelection(toolStripButton, tag);

			if (finishClickAction != null)
				finishClickAction();
		}

		private void FinishUiLanguageMenuItemClick()
		{
			// these lines deal with having a smaller workspace window and minimizing the button texts for smaller windows
			SaveOriginalButtonTexts();
			AdjustButtonTextsForLocale();
			_localizationChangedEvent.Raise(null);
		}

		public static string MenuItemName(CultureInfo lang)
		{
			string englishName = string.Empty;
			var languageNamesRecognizableByOtherLatinScriptReaders = new List<string> { "en", "fr", "es", "it", "tpi", "id" };
			if ((lang.EnglishName != lang.NativeName) && !(languageNamesRecognizableByOtherLatinScriptReaders.Contains(lang.Name)))
			{
				englishName = " (" + lang.EnglishName + ")";
			}
			var menuItemName = lang.NativeName + englishName;
			return menuItemName;
		}

		public static void UpdateMenuTextToShorterNameOfSelection(ToolStripDropDownButton toolStripButton, CultureInfo language)
		{
			toolStripButton.Text = language.NativeName;
		}

		private void OnEditBook(Book.Book book)
		{
			_tabStrip.SelectedTab = _editTab;
		}

		private void Application_Idle(object sender, EventArgs e)
		{
			Application.Idle -= Application_Idle;

		}


		private void OnUpdateDisplay(object sender, System.EventArgs e)
		{
			SetTabVisibility(_editTab, _model.ShowEditPage);
			SetTabVisibility(_publishTab, _model.ShowPublishPage);
		}

		private void SetTabVisibility(TabStripButton page, bool visible)
		{
			page.Visible = visible;
		}

		public void OpenCreateLibrary()
		{
			_settingsLauncherHelper.LaunchSettingsIfAppropriate(() =>
			{

				_selectedTabAboutToChangeEvent.Raise(new TabChangedDetails()
				{
					From = _previouslySelectedControl,
					To = null
				});

				_selectedTabChangedEvent.Raise(new TabChangedDetails()
				{
					From = _previouslySelectedControl,
					To = null
				});

				_previouslySelectedControl = null;
				if (_model.CloseRequested())
				{
					Invoke(CloseCurrentProject);
				}
				return DialogResult.OK;
			});
		}

		internal void OnSettingsButton_Click(object sender, EventArgs e)
		{
			DialogResult result =  _settingsLauncherHelper.LaunchSettingsIfAppropriate(() =>
																	{
																		using (var dlg = _settingsDialogFactory())
																		{
																			return dlg.ShowDialog();
																		}
																	});
			if(result==DialogResult.Yes)
			{
				Invoke(ReopenCurrentProject);
			}
		}

		private void SelectPage(Control view)
		{
			CurrentTabView = view as IBloomTabArea;
			// Warn the user if we're starting to use too much memory.
			SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(false, "switched page in workspace", true);

			if(_previouslySelectedControl !=null)
				_containerPanel.Controls.Remove(_previouslySelectedControl);

			view.Dock = DockStyle.Fill;
			_containerPanel.Controls.Add(view);

			_toolSpecificPanel.Controls.Clear();

			_panelHoldingToolStrip.BackColor = CurrentTabView.TopBarControl.BackColor = _tabStrip.BackColor;

			if (SIL.PlatformUtilities.Platform.IsMono)
			{
				BackgroundColorsForLinux(CurrentTabView);
			}

			CurrentTabView.TopBarControl.Dock = DockStyle.Left;
			if(CurrentTabView!=null)//can remove when we get rid of info view
				_toolSpecificPanel.Controls.Add(CurrentTabView.TopBarControl);

			_selectedTabAboutToChangeEvent.Raise(new TabChangedDetails()
			{
				From = _previouslySelectedControl,
				To = view
			});

			_selectedTabChangedEvent.Raise(new TabChangedDetails()
											{
												From = _previouslySelectedControl,
												To = view
											});

			_previouslySelectedControl = view;

			var zoomManager = CurrentTabView as IZoomManager;
			if (zoomManager != null)
			{
				if (!_toolStrip.Items.Contains(_zoomWwrapper))
					_toolStrip.Items.Add(_zoomWwrapper);
				_zoomControl.Zoom = zoomManager.Zoom;
				_zoomControl.ZoomChanged += (sender, args) => zoomManager.Zoom = _zoomControl.Zoom;
				// Todo: set up revese communication.
			}
			else
			{
				if (_toolStrip.Items.Contains(_zoomWwrapper))
					_toolStrip.Items.Remove(_zoomWwrapper);
			}
		}

		private void BackgroundColorsForLinux(IBloomTabArea currentTabView) {

			if (currentTabView.ToolStripBackground == null)
			{
				var bmp = new Bitmap(_toolStrip.Width, _toolStrip.Height);
				using (var g = Graphics.FromImage(bmp))
				{
					using (var b = new SolidBrush(_panelHoldingToolStrip.BackColor))
					{
						g.FillRectangle(b, 0, 0, bmp.Width, bmp.Height);
					}
				}
				currentTabView.ToolStripBackground = bmp;
			}

			_toolStrip.BackgroundImage = currentTabView.ToolStripBackground;
		}

		protected IBloomTabArea CurrentTabView { get; set; }

		private void _tabStrip_SelectedTabChanged(object sender, SelectedTabChangedEventArgs e)
		{
			TabStripButton btn = (TabStripButton)e.SelectedTab;
			_tabStrip.BackColor = btn.BarColor;
			_toolSpecificPanel.BackColor = _panelHoldingToolStrip.BackColor = _tabStrip.BackColor;
			Logger.WriteEvent("Selecting Tab Page: " + e.SelectedTab.Name);
			SelectPage((Control) e.SelectedTab.Tag);
			AdjustTabStripDisplayForScreenSize();
		}

		private void _tabStrip_BackColorChanged(object sender, EventArgs e)
		{
			//_topBarButtonTable.BackColor = _toolSpecificPanel.BackColor =  _tabStrip.BackColor;
		}

		private void OnAboutBoxClick(object sender, EventArgs e)
		{
			string path = FileLocator.GetFileDistributedWithApplication(true,"infoPages","aboutBox-"+LocalizationManager.UILanguageId+".htm");
			if (String.IsNullOrEmpty(path))
			{
				path = FileLocator.GetFileDistributedWithApplication(false,"infoPages","aboutBox.htm");
			}
			using(var dlg = new SIL.Windows.Forms.Miscellaneous.SILAboutBox(path))
			{
				dlg.ShowDialog();
			}
		}


		private void toolStripMenuItem3_Click(object sender, EventArgs e)
		{
			HelpLauncher.Show(this, CurrentTabView.HelpTopicUrl);
		}

		private void _webSiteMenuItem_Click(object sender, EventArgs e)
		{
			Process.Start(UrlLookup.LookupUrl(UrlType.LibrarySite));
		}

		private void _releaseNotesMenuItem_Click(object sender, EventArgs e)
		{
			var path = FileLocator.GetFileDistributedWithApplication("ReleaseNotes.md");
			using (var dlg = new ShowReleaseNotesDialog(global::Bloom.Properties.Resources.BloomIcon, path))
			{
				dlg.ShowDialog();
			}
		}

		private void _makeASuggestionMenuItem_Click(object sender, EventArgs e)
		{
			using (var x = _feedbackDialogFactory())
			{
				x.Show();
			}
		}

		private void OnHelpButtonClick(object sender, MouseEventArgs e)
		{
			HelpLauncher.Show(this, CurrentTabView.HelpTopicUrl);
		}

		private void _showLogMenuItem_Click(object sender, EventArgs e)
		{
			try
			{
				Logger.ShowUserTheLogFile();// Process.Start(Logger.LogPath);
			}
			catch (Exception)
			{
			}
		}

		private void WorkspaceView_Resize(object sender, EventArgs e)
		{
			if (this.ParentForm != null && this.ParentForm.WindowState != FormWindowState.Minimized)
			{
				AdjustTabStripDisplayForScreenSize();
			}
		}

		private void WorkspaceView_Load(object sender, EventArgs e)
		{
			CheckDPISettings();
			_originalToolStripPanelWidth = 0;
			_viewInitialized = true;
		}

		private void OnRegistrationMenuItem_Click(object sender, EventArgs e)
		{
			using (var dlg = new RegistrationDialog(true))
			{
				dlg.ShowDialog();
			}
		}

		private void CheckDPISettings()
		{
			Graphics g = this.CreateGraphics();
			try
			{

				var dx = g.DpiX;
				DPIOfThisAccount = dx;
				var dy = g.DpiY;
				if(dx!=96 || dy!=96)
				{
					SIL.Reporting.ErrorReport.NotifyUserOfProblem(
						"The \"text size (DPI)\" or \"Screen Magnification\" of the display on this computer is set to a special value, {0}. With that setting, some thing won't look right in Bloom. Possibly books won't lay out correctly. If this is a problem, change the DPI back to 96 (the default on most computers), using the 'Display' Control Panel.", dx);
				}
			}
			finally
			{
				g.Dispose();
			}
		}

		private void _checkForNewVersionMenuItem_Click(object sender, EventArgs e)
		{
			if (ApplicationUpdateSupport.BloomUpdateInProgress)
			{
				//enhance: ideally, what this would do is show a toast of whatever it is squirrel is doing: checking, downloading, waiting for a restart.
				MessageBox.Show(this,
					LocalizationManager.GetString("CollectionTab.UpdateCheckInProgress",
						"Bloom is already working on checking for updates."));
				return;
			}
			if (Debugger.IsAttached)
			{
				MessageBox.Show(this, "Sorry, you cannot check for updates from the debugger.");
			}
			else if (InstallerSupport.SharedByAllUsers())
			{
				MessageBox.Show(this, LocalizationManager.GetString("CollectionTab.AdminManagesUpdates",
						"Your system administrator manages Bloom updates for this computer."));
			}
			else
			{
				ApplicationUpdateSupport.CheckForASquirrelUpdate(ApplicationUpdateSupport.BloomUpdateMessageVerbosity.Verbose,
					newInstallDir => RestartBloom(newInstallDir), Settings.Default.AutoUpdate);
			}
		}

		private void RestartBloom(string newInstallDir)
		{
			Control ancestor = Parent;
			while (ancestor != null && !(ancestor is Shell))
				ancestor = ancestor.Parent;
			if (ancestor == null)
				return;
			var shell = (Shell) ancestor;
			var pathToNewExe = Path.Combine(newInstallDir, Path.ChangeExtension(Application.ProductName, ".exe"));
			if (!RobustFile.Exists(pathToNewExe))
				return; // aargh!
			shell.QuitForVersionUpdate = true;
			Process.Start(pathToNewExe);
			Thread.Sleep(2000);
			shell.Close();
		}

		private static void OpenInfoFile(string fileName)
		{
			Process.Start(FileLocator.GetFileDistributedWithApplication("infoPages", fileName));
		}

		private void keyBloomConceptsMenuItem_Click(object sender, EventArgs e)
		{
			OpenInfoFile("KeyBloomConcepts.pdf");
		}

		private void buildingReaderTemplatesMenuItem_Click(object sender, EventArgs e)
		{
			OpenInfoFile("Building and Distributing Reader Templates in Bloom.pdf");
		}

		private void usingReaderTemplatesMenuItem_Click(object sender, EventArgs e)
		{
			OpenInfoFile("Using Bloom Reader Templates.pdf");
		}

		private void _reportAProblemMenuItem_Click(object sender, EventArgs e)
		{
			// Screen shots were showing the menu still open on Linux, so delay a bit by starting the
			// dialog on the next idle loop.  Also allow one repaint event to be handled immediately.
			// (This method has to return for the menu to fully hide itself on Linux.)
			// See https://silbloom.myjetbrains.com/youtrack/issue/BL-3792.
			Application.DoEvents();
			Application.Idle += StartProblemReport;
		}

		private void StartProblemReport(object sender, EventArgs e)
		{
			Application.Idle -= StartProblemReport;
			using (var dlg = _problemReportDialogFactory(this))
			{
				dlg.SetDefaultIncludeBookSetting(true);
				dlg.ShowDialog();
			}
		}

		public void SetStateOfNonPublishTabs(bool enable)
		{
			_collectionTab.Enabled = enable;
			_editTab.Enabled = enable;
		}

		private void _trainingVideosMenuItem_Click(object sender, EventArgs e)
		{
			var path = BloomFileLocator.GetBestLocalizableFileDistributedWithApplication(false, "infoPages", "TrainingVideos-en.md");
			//enhance: change the name of this class in SIL.Windows.Forms to just "MarkDownDialog"
			using(var dlg = new ShowReleaseNotesDialog(global::Bloom.Properties.Resources.BloomIcon, path))
			{
				dlg.Text = LocalizationManager.GetString("HelpMenu.trainingVideos", "Training Videos");
				dlg.ShowDialog();
			}
		}

#region Responsive Toolbar

		enum Shrinkage { FullSize, Stage1, Stage2, Stage3 }
		private Shrinkage _currentShrinkage = Shrinkage.FullSize;
		private ToolStripControlHost _zoomWwrapper;

		private int STAGE_1
		{
			get
			{
				if (_editTab.IsSelected)
				{
					return TabButtonSectionWidth + _editingView.TopBarControl.Width + _originalToolStripPanelWidth;
				}
				if (_publishTab.IsSelected)
				{
					return TabButtonSectionWidth + _publishView.TopBarControl.Width + PUBLISH_PANEL_FUDGE + _originalToolStripPanelWidth;
				}
				return TabButtonSectionWidth + _originalToolStripPanelWidth;
			}
		}
		private int STAGE_2
		{
			get { return STAGE_1 - _stage1SpaceSaved; }
		}
		private int STAGE_3
		{
			get { return STAGE_2 - _stage2SpaceSaved;}
		}
		private const int PANEL_TOOLSTRIP_SMALLWIDTH = 66;
		private const int PANEL_VERTICAL_SPACER = 10; // Used to center the shrunk icons vertically in the Tool Strip Panel
		private const int PUBLISH_PANEL_FUDGE = 21; // Somehow Publish view TopBarControl's width isn't right
		private const string SPACE = " ";

		private void AdjustTabStripDisplayForScreenSize()
		{
			if (!_viewInitialized)
				return;

			if (_originalToolStripPanelWidth == 0)
			{
				SaveOriginalWidthValues();
				SaveOriginalButtonTexts();
			}

			switch (_currentShrinkage)
			{
				default:
					// Shrinkage.FullSize
					if (Width < STAGE_1)
					{
						// shrink to stage 1
						_currentShrinkage = Shrinkage.Stage1;
						ShrinkToStage1();

						// It is possible that we are jumping from FullScreen to a 'remembered'
						// smaller screen size, so test for all of them!
						if (Width < STAGE_2)
						{
							_currentShrinkage = Shrinkage.Stage2;
							ShrinkToStage2();

							if (Width < STAGE_3)
							{
								_currentShrinkage = Shrinkage.Stage3;
								ShrinkToStage3();
							}
						}
					}
					break;
				case Shrinkage.Stage1:
					if (Width >= STAGE_1)
					{
						// grow back to unshrunk
						_currentShrinkage = Shrinkage.FullSize;
						GrowToFullSize();
						break;
					}
					if (Width < STAGE_2)
					{
						// shrink to stage 2
						_currentShrinkage = Shrinkage.Stage2;
						ShrinkToStage2();
					}
					break;
				case Shrinkage.Stage2:
					if (Width >= STAGE_2)
					{
						// grow back to stage 1
						_currentShrinkage = Shrinkage.Stage1;
						GrowToStage1();
						break;
					}
					if (Width < STAGE_3)
					{
						// shrink to stage 3
						_currentShrinkage = Shrinkage.Stage3;
						ShrinkToStage3();
					}
					break;
				case Shrinkage.Stage3:
					if (Width >= STAGE_3)
					{
						// grow back to stage 2
						_currentShrinkage = Shrinkage.Stage2;
						GrowToStage2();
					}
					break;
			}
		}

		private void SaveOriginalWidthValues()
		{
			_originalToolStripPanelWidth = _panelHoldingToolStrip.Width;
			_originalToolSpecificPanelHorizPos = _toolSpecificPanel.Location.X;
			_originalUiMenuWidth = _uiLanguageMenu.Width;
			_stage1SpaceSaved = 0;
			_stage2SpaceSaved = 0;
		}

		private void SaveOriginalButtonTexts()
		{
			_originalHelpText = _helpMenu.Text;
			_originalUiLanguageSelection = _uiLanguageMenu.Text;
		}

		private void ShrinkToStage1()
		{
			// Calculate right edge of tabs and move _toolSpecificPanel over to it
			var rightEdge = _publishTab.Bounds.Right + 5;
			if (_originalToolSpecificPanelHorizPos <= rightEdge)
				return;
			_stage1SpaceSaved = _originalToolSpecificPanelHorizPos - rightEdge;
			var currentToolPanelVert = _toolSpecificPanel.Location.Y;
			_toolSpecificPanel.Location = new Point(rightEdge, currentToolPanelVert);
		}

		private void GrowToFullSize()
		{
			// revert _toolSpecificPanel to its original location
			_toolSpecificPanel.Location = new Point(_originalToolSpecificPanelHorizPos, _toolSpecificPanel.Location.Y);
			_stage1SpaceSaved = 0;
		}

		/// <summary>
		/// Adjust buttons for the current Locale. In particular the Help button may be an icon or a translation.
		/// </summary>
		void AdjustButtonTextsForLocale()
		{
			if (_originalHelpImage == null)
				_originalHelpImage = _helpMenu.Image;
			var helpText = LocalizationManager.GetString("HelpMenu.Help Menu", "?");
			if (helpText == "?" || new[] {"en", "fr", "de", "es"}.Contains(LocalizationManager.UILanguageId))
			{
				_helpMenu.Text = "";
				_helpMenu.Image = _originalHelpImage;
			}
			else
			{
				_helpMenu.Text = helpText;
				_helpMenu.Image = null;
			}
		}

		private void ShrinkToStage2()
		{
			//var panelLocation = _panelHoldingToolStrip.Location;
			_stage2SpaceSaved = 0; // currently no way to save space at stage 2
			//_panelHoldingToolStrip.Width = PANEL_TOOLSTRIP_SMALLWIDTH;
			//_panelHoldingToolStrip.Height -= PANEL_VERTICAL_SPACER;
			//// move the whole panel to the right edge
			//_panelHoldingToolStrip.Location =
			//	new Point(panelLocation.X + _stage2SpaceSaved, panelLocation.Y + PANEL_VERTICAL_SPACER);
			// otherwise as we keep shrinking the right side of the tool specific panel blanks us out
			_panelHoldingToolStrip.BringToFront();
		}

		private void GrowToStage1()
		{
			//_panelHoldingToolStrip.Width = _originalToolStripPanelWidth;
			//_panelHoldingToolStrip.Height += PANEL_VERTICAL_SPACER;
			//_panelHoldingToolStrip.Location =
			//	new Point(this.Width - _originalToolStripPanelWidth, _panelHoldingToolStrip.Location.Y - PANEL_VERTICAL_SPACER);
			_stage2SpaceSaved = 0;
		}

		private void ShrinkToStage3()
		{
			// Extreme measures for really small screens
			_panelHoldingToolStrip.Visible = false;
		}

		private void GrowToStage2()
		{
			_panelHoldingToolStrip.Visible = true;
		}


#endregion

	}

	public class NoBorderToolStripRenderer : ToolStripProfessionalRenderer
	{
		protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

		protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
		{
			// this is needed, especially on Linux
			e.SizeTextRectangleToText();
			AdjustToolStripLocationIfNecessary(e);
			base.OnRenderItemText(e);
		}

		/// <summary>
		/// A toolstrip with one item embedded in a panel embedded in a TableLayoutPanel does not display well
		/// on Linux/Mono.  The text display can be truncated and moves around the panel horizontally.  The
		/// sizing calculation is carried out properly, but the ensuing horizontal location seems almost random.
		/// Rather than try to fix possibly several layers of Mono libary code, we calculate the desired location
		/// here to prevent the text from being truncated if possible.
		/// </summary>
		/// <remarks>
		/// See http://issues.bloomlibrary.org/youtrack/issue/BL-4409.
		/// </remarks>
		private void AdjustToolStripLocationIfNecessary(ToolStripItemTextRenderEventArgs e)
		{
			if (SIL.PlatformUtilities.Platform.IsUnix &&
				e.ToolStrip != null &&
				e.ToolStrip.Items.Count == 1 &&
				e.ToolStrip.Parent != null &&
				e.ToolStrip.Parent.Parent is TableLayoutPanel)
			{
				var delta = (e.ToolStrip.Location.X + e.ToolStrip.Width) - e.ToolStrip.Parent.Width;
				// Try to leave a pixel of margin.
				if (delta >= 0)
				{
					e.ToolStrip.Location = new Point(Math.Max(e.ToolStrip.Location.X - (delta + 1), 1), e.ToolStrip.Location.Y);
				}
			}
		}
	}
}
