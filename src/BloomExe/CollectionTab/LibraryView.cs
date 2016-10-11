using System;
using System.Windows.Forms;
using Bloom.Properties;
//using Bloom.SendReceive;
using Bloom.Workspace;
using L10NSharp;
using SIL.Reporting;
using System.Drawing;

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
			_collectionListView.MakeBloomPack(false);
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

		public Bitmap ToolStripBackground { get; set; }
	}
}
