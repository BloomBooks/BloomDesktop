using System;
using System.Windows.Forms;
using Bloom.Properties;
//using Bloom.SendReceive;
using Bloom.Workspace;
using L10NSharp;
using SIL.Reporting;
using System.Drawing;
using Bloom.MiscUI;
using Bloom.TeamCollection;
using Bloom.ToPalaso;
using Bloom.web;
using SIL.Windows.Forms.SettingProtection;

namespace Bloom.CollectionTab
{
	public partial class LibraryView :  UserControl, IBloomTabArea
	{
		private readonly LibraryModel _model;


		private Control _collectionListView;
		private LibraryBookView _bookView;

		public LibraryView(LibraryModel model, LibraryListView.Factory libraryListViewFactory,
			LibraryBookView.Factory templateBookViewFactory,
			SelectedTabChangedEvent selectedTabChangedEvent,
			SendReceiveCommand sendReceiveCommand,
			TeamCollectionManager tcManager)
		{
			_model = model;
			InitializeComponent();
			splitContainer1.BackColor = Palette.BookListSplitterColor; // controls the left vs. right splitter
			_toolStrip.Renderer = new NoBorderToolStripRenderer();
			_toolStripLeft.Renderer = new NoBorderToolStripRenderer();

			_collectionListView = new ReactControl
			{
				JavascriptBundleName = "collectionTabBundle.js",
				ReactComponentName = "BookListPane",
				Dock = DockStyle.Fill
			};

			splitContainer1.Panel1.Controls.Add(_collectionListView);

			_bookView = templateBookViewFactory();
			_bookView.TeamCollectionMgr = tcManager;
			_bookView.Dock = DockStyle.Fill;
			splitContainer1.Panel2.Controls.Add(_bookView);

			// When going down to Shrink Stage 3 (see WorkspaceView), we want the right-side toolstrip to take precedence
			// (Settings, Other Collection).
			// This essentially makes the TC Status button's zIndex less than the buttons on the right side.
			_toolStripLeft.SendToBack();

			//TODO splitContainer1.SplitterDistance = _collectionListView.PreferredWidth;
			_makeBloomPackButton.Visible = model.IsShellProject;
			_sendReceiveButton.Visible = Settings.Default.ShowSendReceive;

			if (sendReceiveCommand != null)
			{
#if Chorus
				_sendReceiveButton.Click += (x, y) => sendReceiveCommand.Raise(this);
				_sendReceiveButton.Enabled = !SendReceiver.SendReceiveDisabled;
#endif
			}
			else
				_sendReceiveButton.Enabled = false;

			if (SIL.PlatformUtilities.Platform.IsMono)
			{
				BackgroundColorsForLinux();
			}

			selectedTabChangedEvent.Subscribe(c=>
												{
													if (c.To == this)
													{
														Logger.WriteEvent("Entered Collections Tab");
													}
												});
			SetTeamCollectionStatus(tcManager);
			TeamCollectionManager.TeamCollectionStatusChanged += (sender, args) =>
			{
				if (!IsDisposed)
				{
					SafeInvoke.InvokeIfPossible("update TC status", this, false,
						() => SetTeamCollectionStatus(tcManager));
				}
			};
			_tcStatusButton.Click += (sender, args) =>
			{
				// Any messages for which reloading the collection is a useful action?
				var showReloadButton = tcManager.MessageLog.ShouldShowReloadButton;
				// Reinstate this to see messages from before we started up.
				// We think it might be too expensive to show a list as long as this might get.
				// Instead, in the short term we may add a button to show the file.
				// Later we may implement some efficient way to scroll through them.
				// tcManager.CurrentCollection?.MessageLog?.LoadSavedMessages();
				using (var dlg = new ReactDialog("teamCollectionSettingsBundle.js", "TeamCollectionDialog", showReloadButton ? "showReloadButton=true" : ""))
				{
					dlg.ShowDialog(this);
					tcManager.CurrentCollectionEvenIfDisconnected?.MessageLog.WriteMilestone(MessageAndMilestoneType.LogDisplayed);
				}
			};
		}

		internal void ManageSettings(SettingsProtectionHelper settingsLauncherHelper)
		{
			//we have a couple of buttons which don't make sense for the remote (therefore vulnerable) low-end user
			settingsLauncherHelper.ManageComponent(_settingsButton);

			//NB: this isn't really a setting, but we're using that feature to simplify this menu down to what makes sense for the easily-confused user
			settingsLauncherHelper.ManageComponent(_openCreateCollectionButton);
		}

		private void BackgroundColorsForLinux() {

			// Set the background image for Mono because the background color does not paint,
			// and if we override the background paint handler, the default styling of the child
			// controls is changed.

			// We are getting an exception if none of the buttons are visible. The tabstrip is set
			// to Dock.Top which results in the height being zero if no buttons are visible.
			if ((_toolStrip.Height == 0) || (_toolStrip.Width == 0)) return;

			var bmp = new Bitmap(_toolStrip.Width, _toolStrip.Height);
			using (var g = Graphics.FromImage(bmp))
			{
				using (var b = new SolidBrush(_toolStrip.BackColor))
				{
					g.FillRectangle(b, 0, 0, bmp.Width, bmp.Height);
				}
			}
			_toolStrip.BackgroundImage = bmp;
		}

		public string CollectionTabLabel
		{
			get { return LocalizationManager.GetString("CollectionTab.CollectionTabLabel","Collections"); }//_model.IsShellProject ? "Shell Collection" : "Collection"; }

		}


		private void OnMakeBloomPackButton_Click(object sender, EventArgs e)
		{
			// Something in the Mono runtime state machine keeps the GTK filechooser from getting the
			// focus immediately when we invoke _collectionListView.MakeBloomPack() directly at this
			// point.  Waiting for the next idle gets it into a state where the filechooser does receive
			// the focus as desired.  See https://silbloom.myjetbrains.com/youtrack/issue/BL-5809.
			if (SIL.PlatformUtilities.Platform.IsMono)
				Application.Idle += DeferredBloompackFileChooser;
			else
				//_collectionListView.MakeBloomPack(false);
			{
			}
		}

		private void DeferredBloompackFileChooser(object sender, EventArgs e)
		{
			Application.Idle -= DeferredBloompackFileChooser;
			//TODO _collectionListView.MakeBloomPack(false);
		}

		public string HelpTopicUrl
		{
			get
			{
				if (_model.IsShellProject)
				{
					return "/Tasks/Source_Collection_tasks/Source_Collection_tasks_overview.htm";
				}
				else
				{
					return "/Tasks/Vernacular_Collection_tasks/Vernacular_Collection_tasks_overview.htm";
				}
			}
		}

		public Control TopBarControl
		{
			get { return _topBarControl; }
		}

		/// <summary>
		/// TopBarControl.Width is not right here, because (a) the Send/Receive button currently never shows, and
		/// (b) the Make Bloompack button only shows in source collections.
		/// </summary>
		public int WidthToReserveForTopBarControl => _openCreateCollectionButton.Width + _settingsButton.Width +
			(_makeBloomPackButton.Visible ? _makeBloomPackButton.Width : 0) +
		    (_tcStatusButton.Visible ? _tcStatusButton.Width : 0);

		public void PlaceTopBarControl()
		{
			_topBarControl.Dock = DockStyle.Right;
		}

		public Bitmap ToolStripBackground { get; set; }

		private WorkspaceView GetWorkspaceView()
		{
			Control ancestor = Parent;
			while (ancestor != null && !(ancestor is WorkspaceView))
				ancestor = ancestor.Parent;
			return ancestor as WorkspaceView;
		}

		private void _settingsButton_Click(object sender, EventArgs e)
		{
			GetWorkspaceView().OnSettingsButton_Click(sender, e);
		}

		private void _openCreateCollectionButton_Click(object sender, EventArgs e)
		{
			GetWorkspaceView().OpenCreateLibrary();
		}

		/// <summary>
		/// Set a new TC status image. Called at Idle time or startup, on the UI thread.
		/// </summary>
		public void SetTeamCollectionStatus(TeamCollectionManager tcManager)
		{
			_tcStatusButton.Update(tcManager.CollectionStatus);
		}

		private void _tcStatusButton_Click(object sender, EventArgs e)
		{
			// probably will do GetWorkspaceView().OpenTCStatus();
		}
	}
}
