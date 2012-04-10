using System;
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
		private readonly SelectedTabChangedEvent _selectedTabChangedEvent;
		private readonly FeedbackDialog.Factory _feedbackDialogFactory;
		private Control _libraryView;
		private EditingView _editingView;
		private PublishView _publishView;
		private InfoView _infoView;
		private Control _previouslySelectedControl;

		public event EventHandler CloseCurrentProject;

		public delegate WorkspaceView Factory(Control libraryView);

//autofac uses this

		public WorkspaceView(WorkspaceModel model,
							 Control libraryView,
							 EditingView.Factory editingViewFactory,
							 PublishView.Factory pdfViewFactory,
							 InfoView.Factory infoViewFactory,
							 SettingsDialog.Factory settingsDialogFactory,
							 EditBookCommand editBookCommand,
							 SelectedTabChangedEvent selectedTabChangedEvent,
							 FeedbackDialog.Factory feedbackDialogFactory
			)
		{
			_model = model;
			_settingsDialogFactory = settingsDialogFactory;
			_selectedTabChangedEvent = selectedTabChangedEvent;
			_feedbackDialogFactory = feedbackDialogFactory;
			_model.UpdateDisplay += new System.EventHandler(OnUpdateDisplay);
			InitializeComponent();

			_settingsLauncherHelper.CustomSettingsControl = _settingsButton;

			editBookCommand.Subscribe(OnEditBook);

			//Cursor = Cursors.AppStarting;
			Application.Idle += new EventHandler(Application_Idle);
			Text = _model.ProjectName;

			//SetupTabIcons();

			//
			// _libraryView
			//
			this._libraryView = libraryView;
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


			//
			// info view
			//
			this._infoView = infoViewFactory();
			this._infoView.Dock = System.Windows.Forms.DockStyle.Fill;

			_libraryTab.Tag = _libraryView;
			_publishTab.Tag = _publishView;
			_editTab.Tag = _editingView;
			_infoTab.Tag = _infoView;

			if (!Program.StartUpWithFirstOrNewVersionBehavior)
				SetTabVisibility(_infoTab, false);
			SetTabVisibility(_publishTab, false);
			SetTabVisibility(_editTab, false);

			if (Program.StartUpWithFirstOrNewVersionBehavior)
			{
				_tabStrip.SelectedTab = _infoTab;
				SelectPage(_infoView);
			}
			else
			{
				_tabStrip.SelectedTab = _libraryTab;
				SelectPage(_libraryView);
			}

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
			if (_model.CloseRequested())
			{
				Invoke(CloseCurrentProject);
			}
		}

		private void OnInfoButton_Click(object sender, EventArgs e)
		{
			SetTabVisibility(_infoTab, true);
			_tabStrip.SelectedTab = _infoTab;
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

		private void _feedbackButton_Click(object sender, EventArgs e)
		{
			using(var x = _feedbackDialogFactory())
			{
				x.Show();
			}
		}


		private void SelectPage(Control view)
		{
			SetTabVisibility(_infoTab, false); //we always hide this after it is used

			if(_previouslySelectedControl !=null)
				_containerPanel.Controls.Remove(_previouslySelectedControl);

			view.Dock = DockStyle.Fill;
			_containerPanel.Controls.Add(view);
			_previouslySelectedControl = view;

			_toolSpecificPanel.Controls.Clear();

			if (view is PublishView)
			{
				_publishView.TopBarControl.BackColor = _tabStrip.BackColor;
				_publishView.TopBarControl.Dock = DockStyle.Left;
				_toolSpecificPanel.Controls.Add(_publishView.TopBarControl);
			}
			else if (view is EditingView)
			{
				_editingView.TopBarControl.BackColor = _tabStrip.BackColor;
				_editingView.TopBarControl.Dock = DockStyle.Left;
				_toolSpecificPanel.Controls.Add(_editingView.TopBarControl);
			}

			_selectedTabChangedEvent.Raise(new TabChangedDetails()
											{
												From = _previouslySelectedControl,
												To = view
											});
		}

		private void _tabStrip_SelectedTabChanged(object sender, SelectedTabChangedEventArgs e)
		{
			TabStripButton btn = (TabStripButton)e.SelectedTab;
			_tabStrip.BackColor = btn.BarColor;

			SelectPage((Control) e.SelectedTab.Tag);
		}

		private void _tabStrip_BackColorChanged(object sender, EventArgs e)
		{
			_toolSpecificPanel.BackColor =  _tabStrip.BackColor;
		}
	}
}