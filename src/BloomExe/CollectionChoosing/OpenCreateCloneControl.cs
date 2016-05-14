using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Bloom.Collection;
using Bloom.CollectionCreating;
using Bloom.Properties;
using Chorus.UI.Clone;
using SIL.Windows.Forms.Extensions;
using SIL.i18n;
using System.Collections.Generic;
using System.Linq;

namespace Bloom.CollectionChoosing
{
	public partial class OpenCreateCloneControl : UserControl
	{
		private MostRecentPathsList _mruList;
		private Func<string> _createNewLibraryAndReturnPath;
		private string _filterString;

		public event EventHandler DoneChoosingOrCreatingLibrary;

		public string SelectedPath { get; private set; }

		public OpenCreateCloneControl()
		{
			Font = SystemFonts.MessageBoxFont; //use the default OS UI font
			InitializeComponent();
		}

		public void Init(MostRecentPathsList mruList,
						 string filterString,
						 Func<string> createNewLibraryAndReturnPath)
		{
			_filterString = filterString;
			_createNewLibraryAndReturnPath = createNewLibraryAndReturnPath;
			_mruList = mruList;
		}

		/// <summary>
		/// Client should use this, at least, to set the icon (the image) for mru items
		/// </summary>
		public Button TemplateButton
		{
			get { return _templateButton; }
		}

		private void OnLoad(object sender, EventArgs e)
		{
			if (this.DesignModeAtAll())
			{
				return;
			}

			_templateButton.Parent.Controls.Remove(_templateButton);

			const int maxMruItems = 3;
			var collectionsToShow = _mruList.Paths.Take(maxMruItems).ToList();
			if (collectionsToShow.Count() < maxMruItems && Directory.Exists(NewCollectionWizard.DefaultParentDirectoryForCollections))
			{
				collectionsToShow.AddRange(Directory.GetDirectories(NewCollectionWizard.DefaultParentDirectoryForCollections)
					.Select(d => Path.Combine(d, Path.ChangeExtension(Path.GetFileName(d),"bloomCollection")))
					.Where(c => File.Exists(c) && !collectionsToShow.Contains(c))
					.OrderBy(c => Directory.GetLastWriteTime(Path.GetDirectoryName(c)))
					.Reverse()
					.Take(maxMruItems - collectionsToShow.Count()));
			}
			var count = 0;
			foreach (var path in collectionsToShow)
			{
				AddFileChoice(path, count);
				++count;
				if (count > maxMruItems)
					break;
			}

			foreach (Control control in tableLayoutPanel2.Controls)
			{
				if (control.Tag != null && control.Tag.ToString() == "sendreceive")
					control.Visible = Settings.Default.ShowSendReceive;
			}
		}


		private void AddFileChoice(string path, int index)
		{
			const int kRowOffsetForMRUChoices = 1;
			var button = AddChoice(Path.GetFileNameWithoutExtension(path), path, true, OnOpenRecentCollection,
								   index + kRowOffsetForMRUChoices);
			button.Tag = path;
		}

		private Button AddChoice(string localizedLabel, string localizedTooltip, bool enabled, EventHandler clickHandler,
								 int row)
		{
			var button = new Button();
			button.Anchor = AnchorStyles.Top | AnchorStyles.Left;

			button.Width = _templateButton.Width;
			button.Height = _templateButton.Height;
			button.Font = new Font(StringCatalog.LabelFont.FontFamily, _templateButton.Font.Size,
								   _templateButton.Font.Style);
			button.Image = _templateButton.Image;

			button.ImageAlign = ContentAlignment.MiddleLeft;
			button.Click += clickHandler;
			button.Text = "  " + localizedLabel;

			button.FlatAppearance.BorderSize = _templateButton.FlatAppearance.BorderSize;
			button.ForeColor = _templateButton.ForeColor;
			button.FlatStyle = _templateButton.FlatStyle;
			button.ImageAlign = _templateButton.ImageAlign;
			button.TextImageRelation = _templateButton.TextImageRelation;
			button.UseVisualStyleBackColor = _templateButton.UseVisualStyleBackColor;
			button.Enabled = enabled;

			toolTip1.SetToolTip(button, localizedTooltip);
			tableLayoutPanel2.Controls.Add(button);
			tableLayoutPanel2.SetRow(button, row);
			tableLayoutPanel2.SetColumn(button, 0);
			return button;
		}

		private void OnGetFromInternet(object sender, EventArgs e)
		{
			using (var dlg = new Chorus.UI.Clone.GetCloneFromInternetDialog(NewCollectionWizard.DefaultParentDirectoryForCollections))
			{
				SelectAndCloneProject(dlg);
			}
		}

		private void OnGetFromUsb(object sender, EventArgs e)
		{
			using (var dlg = new Chorus.UI.Clone.GetCloneFromUsbDialog(NewCollectionWizard.DefaultParentDirectoryForCollections))
			{
				SelectAndCloneProject(dlg);
			}
		}
		private void OnGetFromChorusHub(object sender, EventArgs e)
		{
			using (var dlg = new Chorus.UI.Clone.GetCloneFromChorusHubDialog(new GetCloneFromChorusHubModel(NewCollectionWizard.DefaultParentDirectoryForCollections)))
			{
				SelectAndCloneProject(dlg);
			}
		}

		private void SelectAndCloneProject(ICloneSourceDialog dlg)
		{
			try
			{
				if (!Directory.Exists(NewCollectionWizard.DefaultParentDirectoryForCollections))
				{
					Directory.CreateDirectory(NewCollectionWizard.DefaultParentDirectoryForCollections);
				}
				dlg.SetFilePatternWhichMustBeFoundInHgDataFolder("*.bloom_collection.i");

				if (DialogResult.Cancel == ((Form)dlg).ShowDialog())
					return;

				SelectCollectionAndClose(CollectionSettings.FindSettingsFileInFolder(dlg.PathToNewlyClonedFolder));
			}
			catch (Exception error)
			{
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(error, "Bloom ran into a problem:\r\n{0}",
																 error.Message);
			}
		}

		private void OnOpenRecentCollection(object sender, EventArgs e)
		{
			SelectCollectionAndClose(((Button) sender).Tag as string);
		}

		private void OnBrowseForExistingLibraryClick(object sender, EventArgs e)
		{
			if (!Directory.Exists(NewCollectionWizard.DefaultParentDirectoryForCollections))
			{
				Directory.CreateDirectory(NewCollectionWizard.DefaultParentDirectoryForCollections);
			}

			using (var dlg = new OpenFileDialog())
			{
				dlg.Title = "Open Collection";

				dlg.Filter = _filterString;
				dlg.InitialDirectory = NewCollectionWizard.DefaultParentDirectoryForCollections;
				dlg.CheckFileExists = true;
				dlg.CheckPathExists = true;
				if (dlg.ShowDialog(this) == DialogResult.Cancel)
					return;

				SelectCollectionAndClose(dlg.FileName);
			}
		}

		private void CreateNewLibrary_LinkClicked(object sender, EventArgs e)
		{
			var desiredOrExistingSettingsFilePath = _createNewLibraryAndReturnPath();
			if (desiredOrExistingSettingsFilePath == null)
				return;
			var settings = new CollectionSettings(desiredOrExistingSettingsFilePath);
			SelectCollectionAndClose(settings.SettingsFilePath);
		}

		public void SelectCollectionAndClose(string path)
		{
			SelectedPath = path;
			if (!string.IsNullOrEmpty(path))
			{
				if (ReportIfInvalidCollectionToEdit(path)) return;
				CheckForBeingInDropboxFolder(path);
				_mruList.AddNewPath(path);
				Invoke(DoneChoosingOrCreatingLibrary);
			}
		}

		public static bool ReportIfInvalidCollectionToEdit(string path)
		{
			if (IsInvalidCollectionToEdit(path))
			{
				var msg = L10NSharp.LocalizationManager.GetString("OpenCreateCloneControl.InSourceCollectionMessage",
					"This collection is part of your 'Sources for new books' which you can see in the bottom left of the Collections tab. It cannot be opened for editing.");
				MessageBox.Show(msg);
				return true;
			}
			return false;
		}

		public static bool IsInvalidCollectionToEdit(string path)
		{
			return path.StartsWith(ProjectContext.GetInstalledCollectionsDirectory())
				|| path.StartsWith(BloomFileLocator.FactoryTemplateBookDirectory);
		}

		/// <summary>
		/// Path(s) to the user's Dropbox folder(s).  It is static because we only want to look these up once.
		/// </summary>
		private static List<string> _dropboxFolders;

		/// <summary>
		/// This method checks 'path' for being in a Dropbox folder.  If so, it displays a warning message.
		/// </summary>
		public static void CheckForBeingInDropboxFolder(string path)
		{
			if (string.IsNullOrEmpty(path)) return;

			try
			{
				if (_dropboxFolders == null)
				{
					_dropboxFolders = new List<string>();
					string dropboxInfoFile;
					// On Windows, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) returns
					// the path of the user's AppData/Roaming subdirectory.  I know the name looks like it should
					// return the AppData directory itself, but it returns the Roaming subdirectory (although
					// there seems to be some confusion about this on stackoverflow.)  MSDN has this to say to
					// describe this enumeration value:
					//    The directory that serves as a common repository for application-specific data for the
					//    current roaming user.
					// My tests on Windows 7/.Net 4.0 empirically show the return value looks something like
					//    C:\Users\username\AppData\Roaming
					// On Linux/Mono 3, the return value looks something like
					//    /home/username/.config
					// but Dropbox places its .dropbox folder in the user's home directory so we need to strip
					// one directory level from that return value.
					var baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
					if (SIL.PlatformUtilities.Platform.IsWindows)
						dropboxInfoFile = Path.Combine(baseFolder, @"Dropbox\info.json");
					else
						dropboxInfoFile = Path.Combine(Path.GetDirectoryName(baseFolder), @".dropbox/info.json");

					//on my windows 10 box, the file we want is in AppData\Local\Dropbox
					if (!File.Exists(dropboxInfoFile))
					{
						baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
						if (SIL.PlatformUtilities.Platform.IsWindows)
							dropboxInfoFile = Path.Combine(baseFolder, @"Dropbox\info.json");
						else
							dropboxInfoFile = Path.Combine(Path.GetDirectoryName(baseFolder), @".dropbox/info.json");
						if (!File.Exists(dropboxInfoFile))
							return; // User appears to not have Dropbox installed
					}

					var info = File.ReadAllText(dropboxInfoFile);
					var matches = Regex.Matches(info, @"{""path"": ""([^""]+)"",");
					foreach (Match match in matches)
					{
						var folder = match.Groups[1].Value;
						if (SIL.PlatformUtilities.Platform.IsWindows)
						{
							folder = folder.Replace("\\\\", "\\");
							folder = folder.ToLowerInvariant();
						}
						_dropboxFolders.Add(folder + Path.DirectorySeparatorChar);
					}
				}

				if (_dropboxFolders.Count == 0)
					return; // User appears to not have Dropbox installed

				if (SIL.PlatformUtilities.Platform.IsWindows)
					path = path.ToLowerInvariant(); // We do a case-insensitive compare on Windows.

				foreach (var folder in _dropboxFolders)
				{
					if (path.StartsWith(folder))
					{
						var msg = L10NSharp.LocalizationManager.GetString("OpenCreateCloneControl.InDropboxMessage",
							"Bloom detected that this collection is located in your Dropbox folder. This can cause problems as Dropbox sometimes locks Bloom out of its own files. If you have problems, we recommend that you move your collection somewhere else or disable Dropbox while using Bloom.",
							"");
						SIL.Reporting.ErrorReport.NotifyUserOfProblem(msg);
						return;
					}
				}
			}
			catch (Exception e)
			{
				// To help fix BL-1246, we enable this:
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(e,
					"For some reason Bloom could not check your Dropbox settings. This should not cause you any problems, but please report it so we can fix it.");
				SIL.Reporting.Logger.WriteEvent("*** In CheckForBeingInDropboxFolder(), got "+e.Message+Environment.NewLine+e.StackTrace);
				Debug.Fail(e.Message);
			}
		}

		private void _readMoreLabel_Click(object sender, LinkLabelLinkClickedEventArgs e)
		{
			HelpLauncher.Show(null, "Chorus_Help.chm", "Chorus/Chorus_overview.htm");
		}
	}
}