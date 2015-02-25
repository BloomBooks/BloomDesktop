using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Collection;
using Bloom.CollectionTab;
using Bloom.Edit;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.Publish;
using Bloom.Registration;
using Chorus;
using Chorus.UI.Sync;
using L10NSharp;
using Messir.Windows.Forms;
using Palaso.IO;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.ReleaseNotes;
using Palaso.UI.WindowsForms.SettingProtection;
using Squirrel;

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
		private readonly ChorusSystem _chorusSystem;
		private LibraryView _collectionView;
		private EditingView _editingView;
		private PublishView _publishView;
		private Control _previouslySelectedControl;
		public event EventHandler CloseCurrentProject;
		public event EventHandler ReopenCurrentProject;
		private readonly LocalizationManager _localizationManager;
		public static float DPIOfThisAccount;
		private static bool _squirrelUpdateRunning;

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
							ChorusSystem chorusSystem,
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
			_chorusSystem = chorusSystem;
			_localizationManager = localizationManager;
			_model.UpdateDisplay += new System.EventHandler(OnUpdateDisplay);
			InitializeComponent();

			if (Palaso.PlatformUtilities.Platform.IsWindows)
			{
				Application.Idle += CheckForUpdatesOnFirstIdle;
			}
			else
			{
			_checkForNewVersionMenuItem.Visible = false;
			}

			_toolStrip.Renderer = new NoBorderToolStripRenderer();

			//we have a number of buttons which don't make sense for the remote (therefore vulnerable) low-end user
			//_settingsLauncherHelper.CustomSettingsControl = _toolStrip;

			_settingsLauncherHelper.ManageComponent(_settingsButton);

			//NB: the rest of these aren't really settings, but we're using that feature to simplify this menu down to what makes sense for the easily-confused user
			_settingsLauncherHelper.ManageComponent(_openCreateCollectionButton);
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

			if (Palaso.PlatformUtilities.Platform.IsMono)
			{
				// Without this adjustment, we lose some controls on smaller resolutions.
				var location = _toolSpecificPanel.Location;
				location.X = _tabStrip.Items.Cast<TabStripButton>().Sum(tab => tab.Width) + 10;
				_toolSpecificPanel.Location = location;

				// in mono auto-size causes the height of the tab strip to be too short
				_tabStrip.AutoSize = false;
			}

			SetupUILanguageMenu();
		}

		private void CheckForUpdatesOnFirstIdle(object sender, EventArgs eventArgs)
		{
			Application.Idle -= CheckForUpdatesOnFirstIdle;
			if (!Debugger.IsAttached)
			{
				if (Settings.Default.AutoUpdate)
					InitiateSquirrelUpdate();
				else
					InitiateSquirrelNotifyUpdatesAvailable();
			}
		}

		private void OnSendReceive(object obj)
		{
			using (SyncDialog dlg = (SyncDialog) _chorusSystem.WinForms.CreateSynchronizationDialog())
			{
				dlg.ShowDialog();
				if(dlg.SyncResult.DidGetChangesFromOthers)
				{
					Invoke(ReopenCurrentProject);
				}
			}
		}

		void OnSettingsProtectionChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			//when we need to use Ctrl+Shift to display stuff, we don't want it also firing up the localization dialog (which shouldn't be done by a user under settings protection anyhow)

			LocalizationManager.EnableClickingOnControlToBringUpLocalizationDialog = !SettingsProtectionSettings.Default.NormallyHidden;
		}

		private void SetupUILanguageMenu()
		{
			_uiLanguageMenu.DropDownItems.Clear();
			foreach (var lang in L10NSharp.LocalizationManager.GetUILanguages(true))
			{
				string englishName="";
				var langaugeNamesRecognizableByOtherLatinScriptReaders = new List<string> {"en","fr","es","it","tpi"};
				if((lang.EnglishName != lang.NativeName) && !(langaugeNamesRecognizableByOtherLatinScriptReaders.Contains(lang.Name)))
				{
					englishName = " (" + lang.EnglishName + ")";
				}
				var item = _uiLanguageMenu.DropDownItems.Add(lang.NativeName + englishName);
				item.Tag = lang;
				item.Click += new EventHandler((a, b) =>
												{
													L10NSharp.LocalizationManager.SetUILanguage(((CultureInfo)item.Tag).IetfLanguageTag, true);
													Settings.Default.UserInterfaceLanguage = ((CultureInfo)item.Tag).IetfLanguageTag;
													item.Select();
													_uiLanguageMenu.Text = ((CultureInfo) item.Tag).NativeName;
													_localizationChangedEvent.Raise(null);
												});
				if (((CultureInfo)item.Tag).IetfLanguageTag == Settings.Default.UserInterfaceLanguage)
				{
					//doesn't do anything item.Select();

					_uiLanguageMenu.Text = ((CultureInfo) item.Tag).NativeName;
				}
			}

			_uiLanguageMenu.DropDownItems.Add(new ToolStripSeparator());
			var menu = _uiLanguageMenu.DropDownItems.Add(LocalizationManager.GetString("CollectionTab.menuToBringUpLocalizationDialog","More..."));
			menu.Click += new EventHandler((a, b) =>
			{
				_localizationManager.ShowLocalizationDialogBox(false);
				SetupUILanguageMenu();
				LocalizationManager.ReapplyLocalizationsToAllObjectsInAllManagers(); //review: added this based on its name... does it help?
				_localizationChangedEvent.Raise(null);
			});
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

		private void OnOpenCreateLibrary_Click(object sender, EventArgs e)
		{
			OpenCreateLibrary();
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

		private void OnSettingsButton_Click(object sender, EventArgs e)
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

			if(_previouslySelectedControl !=null)
				_containerPanel.Controls.Remove(_previouslySelectedControl);

			view.Dock = DockStyle.Fill;
			_containerPanel.Controls.Add(view);

			_toolSpecificPanel.Controls.Clear();

			_panelHoldingToolStrip.BackColor = CurrentTabView.TopBarControl.BackColor = _tabStrip.BackColor;

			if (Palaso.PlatformUtilities.Platform.IsMono)
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
			using(var dlg = new Palaso.UI.WindowsForms.SIL.SILAboutBox(path))
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
			Process.Start("http://bloomlibrary.org");
		}

		private void _releaseNotesMenuItem_Click(object sender, EventArgs e)
		{
			var path = FileLocator.GetFileDistributedWithApplication("ReleaseNotes.md");
			using (var dlg = new ShowReleaseNotesDialog(global::Bloom.Properties.Resources.Bloom, path))
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
			//when doing videos at this really low resolution, there's just no room for this
			_panelHoldingToolStrip.Visible = this.Width > 820;
		}

		private void WorkspaceView_Load(object sender, EventArgs e)
		{
			CheckDPISettings();
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
					Palaso.Reporting.ErrorReport.NotifyUserOfProblem(
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
			if (_squirrelUpdateRunning)
			{
				MessageBox.Show(this,
					LocalizationManager.GetString("CollectionTab.UpdateCheckInProgress",
						"Bloom is already working on checking for updates (perhaps automatically)"));
				return;
			}
			InitiateSquirrelUpdate();
		}

		/// <summary>
		/// See if any updates are available and if so do them. Once they are done a notification
		/// pops up and the user can restart Bloom to run the new version.
		/// </summary>
		private async void InitiateSquirrelUpdate()
		{
			if (Palaso.PlatformUtilities.Platform.IsWindows)
			{
				string updateUrl;
				string rootDirectory = null; // null default causes squirrel to figure out the version actually running.
				if (Debugger.IsAttached)
				{
					// update'Url' can actually also just be a path to where the deltas and RELEASES file are found.
					// When debugging this function we want this to be the directory where we build installers.
					var location = Assembly.GetExecutingAssembly().Location; // typically in output\debug
					var output = Path.GetDirectoryName(Path.GetDirectoryName(location));
					updateUrl = Path.Combine(output, "installer");

					// For testing we will force it to look in the standard local data folder, even though we are not running there.
					// Tester should ensure that the version we want to pretent to upgrade is installed there (under Bloom)...the critical thing
					// seems to be the version of Bloom/packages/RELEASES in this folder which indicates what is already installed.
					rootDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
				}
				else
				{
					updateUrl = Program.SquirrelUpdateUrl;
				}
				if (updateUrl == null)
					return; // For some reason we couldn't get one...possibly not online so can't get to UpdateVersionTable
				string newInstallDir;
				_squirrelUpdateRunning = true;
				using (var mgr = new UpdateManager(updateUrl, "Bloom", FrameworkVersion.Net45, rootDirectory))
				{
					// At this point the method returns(!) and no longer blocks anything.
					newInstallDir = await UpdateApp(mgr, null);
				}
				// Since this is in the async method _after_ the await we know the UpdateApp has finished.
				_squirrelUpdateRunning = false;
				if (newInstallDir == null)
					return;
				string version = Path.GetFileName(newInstallDir).Substring("app-".Length); // version folders always start with this
				var msg = string.Format(LocalizationManager.GetString("CollectionTab.UpdateInstalled", "Bloom version {0} is ready"), version);
				var action = string.Format(LocalizationManager.GetString("CollectionTab.RestartNow", "Restart Bloom to Update"));
				// Unfortunately, there's no good time to dispose of this object...according to its own comments
				// it's not even safe to close it. It moves itself out of sight eventually if ignored.
				var notifier = new ToastNotifier();
				notifier.Image.Image = Resources.Bloom.ToBitmap();
				notifier.ToastClicked += (sender, args) => RestartBloom(newInstallDir);
				notifier.Show(msg, action, 8);
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
			var pathToNewExe = Path.Combine(newInstallDir, "Bloom.exe");
			if (!File.Exists(pathToNewExe))
				return; // aargh!
			shell.QuitForVersionUpdate = true;
			Process.Start(pathToNewExe);
			Thread.Sleep(2000);
			shell.Close();
		}

		// Adapted from Squirrel's EasyModeMixin.UpdateApp, but this version yields the new directory.
		public static async Task<string> UpdateApp(IUpdateManager manager, Action<int> progress = null)
		{
			progress = progress ?? (_ => { });

			bool ignoreDeltaUpdates = false;

		retry:
			var updateInfo = default(UpdateInfo);
			string newInstallDirectory = null;

			try
			{
				updateInfo = await manager.CheckForUpdate(ignoreDeltaUpdates, x => progress(x / 3));
				if (updateInfo == null)
					return null; // none available.

				await manager.DownloadReleases(updateInfo.ReleasesToApply, x => progress(x / 3 + 33));

				newInstallDirectory = await manager.ApplyReleases(updateInfo, x => progress(x / 3 + 66));

				await manager.CreateUninstallerRegistryEntry();
			}
			catch (Exception ex)
			{
				if (ignoreDeltaUpdates == false)
				{
					// I think the idea here is that if something goes wrong applying deltas we
					// just download and install whatever the update url says is the latest version,
					// as a complete package.
					// Thus we can even recover if the executing program and the package that created
					// it are not part of the sequence on the web site at all, or even if there's
					// some sort of discontinuity in the sequence of deltas.
					ignoreDeltaUpdates = true;
					goto retry;
				}

				throw;
			}

			return newInstallDirectory;
		}

		private async void InitiateSquirrelNotifyUpdatesAvailable()
		{
			if (Palaso.PlatformUtilities.Platform.IsWindows)
			{
				var updateUrl = Program.SquirrelUpdateUrl;
				if (updateUrl == null)
					return;
				_squirrelUpdateRunning = true;
				UpdateInfo info;
				using (var mgr = new UpdateManager(updateUrl, "Bloom", FrameworkVersion.Net45))
				{
					// At this point the method returns(!) and no longer blocks anything.
					info = await mgr.CheckForUpdate();
				}
				// Since this is in the async method _after_ the await we know the CheckForUpdate has finished.
				_squirrelUpdateRunning = false;
				if (info == null)
					return; // none available.
				var msg = LocalizationManager.GetString("CollectionTab.UpdatesAvailable", "A new version of Bloom is available");
				var action = LocalizationManager.GetString("CollectionTab.UpdateNow", "Update Now");
				// Unfortunately, there's no good time to dispose of this object...according to its own comments
				// it's not even safe to close it. It moves itself out of sight eventually if ignored.
				var notifier = new ToastNotifier();
				notifier.Image.Image = Resources.Bloom.ToBitmap();
				notifier.ToastClicked += (sender, args) => InitiateSquirrelUpdate();
				notifier.Show(msg, action, 10);
			}
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
			using (var dlg = _problemReportDialogFactory(this))
			{
				dlg.ShowDialog();
			}
		}
	}

	public class NoBorderToolStripRenderer : ToolStripProfessionalRenderer
	{
		protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

		protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
		{
			// this is needed, especially on Linux
			e.SizeTextRectangleToText();
			base.OnRenderItemText(e);
		}
	}
}