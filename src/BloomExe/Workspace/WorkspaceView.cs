using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Bloom.Edit;
using Bloom.Library;
using Bloom.Publish;
using Messir.Windows.Forms;
using Palaso.IO;

namespace Bloom.Workspace
{
	public partial class WorkspaceView : UserControl
	{
		private readonly WorkspaceModel _model;
		private readonly SettingsDialog.Factory _settingsDialogFactory;
		private readonly SelectedTabAboutToChangeEvent _selectedTabAboutToChangeEvent;
		private readonly SelectedTabChangedEvent _selectedTabChangedEvent;
		private readonly FeedbackDialog.Factory _feedbackDialogFactory;
		private LibraryView _libraryView;
		private EditingView _editingView;
		private PublishView _publishView;
		private Control _previouslySelectedControl;

		public event EventHandler CloseCurrentProject;

		public delegate WorkspaceView Factory(Control libraryView);

//autofac uses this

		public WorkspaceView(WorkspaceModel model,
							 Control libraryView,
							 EditingView.Factory editingViewFactory,
							 PublishView.Factory pdfViewFactory,
							 SettingsDialog.Factory settingsDialogFactory,
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
			_settingsLauncherHelper.CustomSettingsControl = _toolStrip;

			editBookCommand.Subscribe(OnEditBook);

			//Cursor = Cursors.AppStarting;
			Application.Idle += new EventHandler(Application_Idle);
			Text = _model.ProjectName;

			//SetupTabIcons();

			//
			// _libraryView
			//
			this._libraryView = (LibraryView) libraryView;
			this._libraryView.Dock = System.Windows.Forms.DockStyle.Fill;

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




			_libraryTab.Tag = _libraryView;
			_publishTab.Tag = _publishView;
			_editTab.Tag = _editingView;


			this._libraryTab.Text = _libraryView.LibraryTabLabel;

			SetTabVisibility(_publishTab, false);
			SetTabVisibility(_editTab, false);

//			if (Program.StartUpWithFirstOrNewVersionBehavior)
//			{
//				_tabStrip.SelectedTab = _infoTab;
//				SelectPage(_infoView);
//			}
//			else
//			{
				_tabStrip.SelectedTab = _libraryTab;
				SelectPage(_libraryView);
//			}

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
			_settingsLauncherHelper.LaunchSettingsIfAppropriate(() =>
																	{
																		using (var dlg = _settingsDialogFactory())
																		{
																			return dlg.ShowDialog();
																		}
																	});
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



	}

	public class NoBorderToolStripRenderer : ToolStripProfessionalRenderer
	{
		protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
	}
}