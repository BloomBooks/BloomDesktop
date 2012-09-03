using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Bloom.Collection;
using Bloom.Edit;
using Bloom.Library;
using Bloom.Properties;
using Bloom.Publish;
using Localization;
using Messir.Windows.Forms;
using Palaso.IO;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.SettingProtection;

namespace Bloom.Workspace
{
	public partial class WorkspaceView : UserControl
	{
		private readonly WorkspaceModel _model;
		private readonly CollectionSettingsDialog.Factory _settingsDialogFactory;
		private readonly SelectedTabAboutToChangeEvent _selectedTabAboutToChangeEvent;
		private readonly SelectedTabChangedEvent _selectedTabChangedEvent;
		private readonly FeedbackDialog.Factory _feedbackDialogFactory;
		private LibraryView _collectionView;
		private EditingView _editingView;
		private PublishView _publishView;
		private Control _previouslySelectedControl;

		public event EventHandler CloseCurrentProject;
		public event EventHandler ReopenCurrentProject;

		public delegate WorkspaceView Factory(Control libraryView);

//autofac uses this

		public WorkspaceView(WorkspaceModel model,
							 Control libraryView,
							 EditingView.Factory editingViewFactory,
							 PublishView.Factory pdfViewFactory,
							 CollectionSettingsDialog.Factory settingsDialogFactory,
							 EditBookCommand editBookCommand,
							 SelectedTabAboutToChangeEvent selectedTabAboutToChangeEvent,
							SelectedTabChangedEvent selectedTabChangedEvent,
							 FeedbackDialog.Factory feedbackDialogFactory
			)
		{
			_model = model;
			_settingsDialogFactory = settingsDialogFactory;
			_selectedTabAboutToChangeEvent = selectedTabAboutToChangeEvent;
			_selectedTabChangedEvent = selectedTabChangedEvent;
			_feedbackDialogFactory = feedbackDialogFactory;
			_model.UpdateDisplay += new System.EventHandler(OnUpdateDisplay);
			InitializeComponent();

			_toolStrip.Renderer = new NoBorderToolStripRenderer();
			//we have a number of buttons which don't make sense for the remote (therefore vulnerable) low-end user
			//_settingsLauncherHelper.CustomSettingsControl = _toolStrip;

			_settingsLauncherHelper.ManageComponent(_settingsButton);

			//NB: the rest of these aren't really settings, but we're using that feature to simplify this menu down to what makes sense for the easily-confused user
			_settingsLauncherHelper.ManageComponent(_openCreateCollectionButton);
			_settingsLauncherHelper.ManageComponent(deepBloomPaperToolStripMenuItem);
			_settingsLauncherHelper.ManageComponent(_makeASuggestionMenuItem);
			_settingsLauncherHelper.ManageComponent(_webSiteMenuItem);

#if DEBUG //this would turn it on when we want it hidden until localization is more complete
			_settingsLauncherHelper.ManageComponent(_uiLanguageMenu);
#endif

			OnSettingsProtectionChanged(this, null);//initial setup
			SettingsProtectionSettings.Default.PropertyChanged += new System.ComponentModel.PropertyChangedEventHandler(OnSettingsProtectionChanged);


			_uiLanguageMenu.Visible = false; // not ready for users
#if DEBUG
			_uiLanguageMenu.Visible = true;
#endif
			editBookCommand.Subscribe(OnEditBook);

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

			SetupUILanguageMenu();
		}

		void OnSettingsProtectionChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			//when we need to use Ctrl+Shift to display stuff, we don't want it also firing up the localization dialog (which shouldn't be done by a user under settings protection anyhow)

			LocalizationManager.EnableClickingOnControlToBringUpLocalizationDialog =
				!SettingsProtectionSettings.Default.NormallyHidden;
		}

		private void SetupUILanguageMenu()
		{
			_uiLanguageMenu.DropDownItems.Clear();
			foreach (var lang in Localization.LocalizationManager.GetUILanguages(true))
			{
				var item = _uiLanguageMenu.DropDownItems.Add(lang.NativeName);
				item.Tag = lang;
				item.Click += new EventHandler((a, b) =>
												{
													Localization.LocalizationManager.SetUILanguage(((CultureInfo)item.Tag).IetfLanguageTag, true);
													Settings.Default.UserInterfaceLanguage = ((CultureInfo)item.Tag).IetfLanguageTag;
													item.Select();
													_uiLanguageMenu.Text = ((CultureInfo) item.Tag).NativeName;
												});
				if (((CultureInfo)item.Tag).IetfLanguageTag == Settings.Default.UserInterfaceLanguage)
				{
					//doesn't do anything item.Select();

					_uiLanguageMenu.Text = ((CultureInfo) item.Tag).NativeName;
				}
			}


			_uiLanguageMenu.DropDownItems.Add(new ToolStripSeparator());
			var menu = _uiLanguageMenu.DropDownItems.Add(LocalizationManager.GetString("menuToBringUpLocalizationDialog","More..."));
			menu.Click += new EventHandler((a, b) =>
											{
												Localization.LocalizationManager.ShowLocalizationDialogBox();
												SetupUILanguageMenu();
											});
		}


		private void OnEditBook(Book.Book book)
		{
			_tabStrip.SelectedTab = _editTab;
		}

		private void Application_Idle(object sender, EventArgs e)
		{
			//this didn't work... we got to idle when the lists were still populating
			Application.Idle -= new EventHandler(Application_Idle);
			//            Cursor = Cursors.Default;
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
			_settingsLauncherHelper.LaunchSettingsIfAppropriate(() =>
			{
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
			//SetTabVisibility(_infoTab, false); //we always hide this after it is used

			if(_previouslySelectedControl !=null)
				_containerPanel.Controls.Remove(_previouslySelectedControl);

			view.Dock = DockStyle.Fill;
			_containerPanel.Controls.Add(view);

			_toolSpecificPanel.Controls.Clear();

			_panelHoldingToolStrip.BackColor = CurrentTabView.TopBarControl.BackColor = _tabStrip.BackColor;
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

		protected IBloomTabArea CurrentTabView { get; set; }

		private void _tabStrip_SelectedTabChanged(object sender, SelectedTabChangedEventArgs e)
		{
			TabStripButton btn = (TabStripButton)e.SelectedTab;
			_tabStrip.BackColor = btn.BarColor;
			_toolSpecificPanel.BackColor = _panelHoldingToolStrip.BackColor = _tabStrip.BackColor;
			SelectPage((Control) e.SelectedTab.Tag);
		}

		private void _tabStrip_BackColorChanged(object sender, EventArgs e)
		{
			//_topBarButtonTable.BackColor = _toolSpecificPanel.BackColor =  _tabStrip.BackColor;
		}

		private void toolStripMenuItem1_Click(object sender, EventArgs e)
		{
			Process.Start(FileLocator.GetFileDistributedWithApplication("infoPages", "1 About.htm"));
		}


		private void toolStripMenuItem3_Click(object sender, EventArgs e)
		{
			HelpLauncher.Show(this, CurrentTabView.HelpTopicUrl);
		}

		private void _webSiteMenuItem_Click(object sender, EventArgs e)
		{
			Process.Start("http://bloom.palaso.org");
		}

		private void _releaseNotesMenuItem_Click(object sender, EventArgs e)
		{
			Process.Start(FileLocator.GetFileDistributedWithApplication("infoPages","0 Release Notes.htm"));
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

		private void deepBloomPaperToolStripMenuItem_Click(object sender, EventArgs e)
		{
			Process.Start(FileLocator.GetFileDistributedWithApplication("infoPages", "Deep Bloom.pdf"));
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
	}

	public class NoBorderToolStripRenderer : ToolStripProfessionalRenderer
	{
		protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
	}
}