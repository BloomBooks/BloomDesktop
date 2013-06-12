using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;
using NetSparkle;
using Bloom.Collection;
using Bloom.Properties;
using Bloom.Workspace;


namespace Bloom
{
	public partial class Shell : Form
	{
		private readonly CollectionSettings _collectionSettings;
		private readonly LibraryClosing _libraryClosingEvent;
		private readonly WorkspaceView _workspaceView;


		public Shell(Func<WorkspaceView> projectViewFactory, CollectionSettings collectionSettings, LibraryClosing libraryClosingEvent, QueueRenameOfCollection queueRenameOfCollection, Sparkle _sparkle)
		{
			queueRenameOfCollection.Subscribe(newName => _nameToChangeCollectionUponClosing = newName);
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

			_sparkle.AboutToExitForInstallerRun += ((x, cancellable) =>
			{
				cancellable.Cancel = false;
				QuitForVersionUpdate = true;
				Close();
			});

			_workspaceView.ReopenCurrentProject += ((x, y) =>
			{
				UserWantsToOpeReopenProject = true;
				Close();
			});

			SystemEvents.SessionEnding += ((x,y)=>
			{
				QuitForSystemShutdown=true;
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
			//get everything saved (under the old collection name, if we are changing the name and restarting)
			_libraryClosingEvent.Raise(null);

			//change the collection name now, when it's safe
			try
			{
				if (!string.IsNullOrEmpty(_nameToChangeCollectionUponClosing) && _nameToChangeCollectionUponClosing != _collectionSettings.CollectionName)
					_collectionSettings.AttemptSaveAsToNewName(_nameToChangeCollectionUponClosing);
			}
			catch (Exception error)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, "Sorry, Bloom could not rename the project to '{0}'", _nameToChangeCollectionUponClosing);
			}

			Settings.Default.MruProjects.AddNewPath(_collectionSettings.SettingsFilePath);

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

		/// <summary>
		/// used when the user does an in-app installer download; after we close down, Program will read this and return control to Sparkle
		/// </summary>
		public bool QuitForVersionUpdate;

		public bool QuitForSystemShutdown;

		private string _nameToChangeCollectionUponClosing;


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

		/// <summary>
		/// we let the Program call this after it closes the splash screen
		/// </summary>
		public void ReallyComeToFront()
		{
			//try really hard to become top most. See http://stackoverflow.com/questions/5282588/how-can-i-bring-my-application-window-to-the-front
			TopMost = true;
			Focus();
			BringToFront();
			TopMost = false;
		}

	}
}
