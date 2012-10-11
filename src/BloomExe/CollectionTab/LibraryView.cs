using System;
using System.Windows.Forms;
using Bloom.Properties;
using Bloom.SendReceive;
using Bloom.Workspace;
using Localization;
using Palaso.Reporting;

namespace Bloom.CollectionTab
{
	public partial class LibraryView :  UserControl, IBloomTabArea
	{
		private readonly LibraryModel _model;


		private LibraryListView _collectionListView;
		private LibraryBookView _bookView;

		public LibraryView(LibraryModel model, LibraryListView.Factory libraryListViewFactory,
			LibraryBookView.Factory templateBookViewFactory,
			SelectedTabChangedEvent selectedTabChangedEvent,
			SendReceiveCommand sendReceiveCommand)
		{
			_model = model;
			InitializeComponent();

			_toolStrip.Renderer = new NoBorderToolStripRenderer();

			_collectionListView = libraryListViewFactory();
			_collectionListView.Dock = DockStyle.Fill;
			splitContainer1.Panel1.Controls.Add(_collectionListView);

			_bookView = templateBookViewFactory();
			_bookView.Dock = DockStyle.Fill;
			splitContainer1.Panel2.Controls.Add(_bookView);

			splitContainer1.SplitterDistance = _collectionListView.PreferredWidth;
			_makeBloomPackButton.Visible = model.IsShellProject;
			_sendReceiveButton.Visible = Settings.Default.ShowSendReceive;

			_sendReceiveButton.Click+=new EventHandler((x,y)=>sendReceiveCommand.Raise(this));
			_sendReceiveButton.Enabled = !SendReceiver.SendReceiveDisabled;
			;
			selectedTabChangedEvent.Subscribe(c=>
												{
													if (c.To == this)
													{
														Logger.WriteEvent("Entered Collections Tab");
														UsageReporter.SendNavigationNotice("Entered Collections Tab");
													}
												});
		}

		public string CollectionTabLabel
		{
			get { return LocalizationManager.GetString("FirstTabLabel","Collections"); }//_model.IsShellProject ? "Shell Collection" : "Collection"; }

		}


		private void OnMakeBloomPackButton_Click(object sender, EventArgs e)
		{
			using(var dlg = new SaveFileDialog())
			{
				dlg.FileName = _model.GetSuggestedBloomPackPath();
				dlg.Filter = "BloomPack|*.BloomPack";
				dlg.RestoreDirectory = true;
				dlg.OverwritePrompt = true;
				if(DialogResult.Cancel == dlg.ShowDialog())
				{
					return;
				}
				_model.MakeBloomPack(dlg.FileName);
			}
		}

		public string HelpTopicUrl
		{
			get { return "/Tasks/Vernacular_Collection_tasks/Vernacular_Collections_tasks_overview.htm"; }
		}

		public Control TopBarControl
		{
			get { return _topBarControl; }
		}

		private void LibraryView_Load(object sender, EventArgs e)
		{

		}
	}
}
