using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Bloom.Collection;
using Bloom.Workspace;
using Palaso.Reporting;

namespace Bloom
{
	public partial class Shell : Form
	{
		private readonly CollectionSettings _collectionSettings;
		private readonly LibraryClosing _libraryClosingEvent;
		private readonly WorkspaceView _workspaceView;

		public Shell(Func<WorkspaceView> projectViewFactory, CollectionSettings collectionSettings, LibraryClosing libraryClosingEvent)
		{
			_collectionSettings = collectionSettings;
			_libraryClosingEvent = libraryClosingEvent;
			InitializeComponent();

			_workspaceView = projectViewFactory();
			_workspaceView.CloseCurrentProject += ((x, y) =>
													{
														UserWantsToOpenADifferentProject = true;
														Close();
													});

			_workspaceView.BackColor =
				System.Drawing.Color.FromArgb(64,64,64);
										_workspaceView.Dock = System.Windows.Forms.DockStyle.Fill;

			this.Controls.Add(this._workspaceView);

			SetWindowText();
		}

		protected override void OnClosing(CancelEventArgs e)
		{
			_libraryClosingEvent.Raise(null);
			base.OnClosing(e);
		}

		private void SetWindowText()
		{
			Text = string.Format("{0} - Bloom {1}", _workspaceView.Text, GetVersionInfo());
			if(_collectionSettings.IsSourceCollection)
			{
				Text += "SOURCE COLLECTION";
			}
		}

		public static string GetVersionInfo()
		{
			var asm = Assembly.GetExecutingAssembly();
			var ver = asm.GetName().Version;
			var file = asm.CodeBase.Replace("file:", string.Empty);
			file = file.TrimStart('/');
			var fi = new FileInfo(file);

			return string.Format("Version {0}.{1}.{2} Built on {3}", ver.Major, ver.Minor,
				ver.Build, fi.CreationTime.ToString("dd-MMM-yyyy"));
		}

		public bool UserWantsToOpenADifferentProject { get; set; }

	}
}
