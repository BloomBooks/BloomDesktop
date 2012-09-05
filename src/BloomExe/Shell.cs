using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Bloom.Collection;
using Bloom.Properties;
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

#if DEBUG
			WindowState = FormWindowState.Normal;
			//this.FormBorderStyle = FormBorderStyle.None;  //fullscreen

			Size = new Size(1024,720);
#endif
			_workspaceView = projectViewFactory();
			_workspaceView.CloseCurrentProject += ((x, y) =>
													{
														UserWantsToOpenADifferentProject = true;
														Close();
													});
			_workspaceView.ReopenCurrentProject += ((x, y) =>
			{
				Settings.Default.MruProjects.AddNewPath(_collectionSettings.SettingsFilePath);
				UserWantsToOpeReopenProject = true;
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

		public bool UserWantsToOpeReopenProject;


		private void Shell_Activated(object sender, EventArgs e)
		{

		}

		private void Shell_Deactivate(object sender, EventArgs e)
		{
			Debug.WriteLine("Shell Deactivated");
		}

		private void On800x600Click(object sender, EventArgs e)
		{
			Size = new Size(800,600);
		}

		private void On1024x600Click(object sender, EventArgs e)
		{
			Size = new Size(1024, 600);
		}

		private void On1024x768(object sender, EventArgs e)
		{
			Size = new Size(1024, 768);
		}

		private void On1024x586(object sender, EventArgs e)
		{
			Size = new Size(1024, 586);
		}

	}
}
