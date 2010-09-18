using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Palaso.I8N;


namespace Bloom.ToPalaso
{
	public partial class WelcomeControl: UserControl
	{
		private string _defaultParentDirectoryForProjects;
		private  MostRecentPathsList _mruList;
		private Func<string, bool> _looksLikeValidProjectPredicate;
		private string _createNewProjectButtonLabel;
		private Func<string> _createNewProjectAndReturnPath;
		private string _browseLabel;

		public event EventHandler DoneChoosingOrCreatingProject;

		public WelcomeControl()
		{
			Font = SystemFonts.MessageBoxFont;//use the default OS UI font
			InitializeComponent();
		}

		public void Init(MostRecentPathsList mruList,
			string defaultParentDirectoryForProjects,
			string createNewProjectButtonLabel,
			string browseForOtherProjectsLabel,
			Func<string, bool> looksLikeValidProjectPredicate,
			Func<string> createNewProjectAndReturnPath)
		 {
			_createNewProjectAndReturnPath = createNewProjectAndReturnPath;
			_browseLabel = browseForOtherProjectsLabel;
			_defaultParentDirectoryForProjects = defaultParentDirectoryForProjects;
			_createNewProjectButtonLabel = createNewProjectButtonLabel;
			_mruList = mruList;
			_looksLikeValidProjectPredicate = looksLikeValidProjectPredicate;
			//this.pictureBox1.Image = headerImage;
		  }

		/// <summary>
		/// use this to change the format of the group labels
		/// </summary>
		public Label TemplateLabel { get { return _templateLabel;}}

		/// <summary>
		/// Client should use this, at least, to set the icon (the image) for mru items
		/// </summary>
		public Button TemplateButton { get { return _templateButton; } }


		private void LoadButtons()
		{
			flowLayoutPanel1.Controls.Clear();
			var createAndGetGroup = new TableLayoutPanel();
			createAndGetGroup.AutoSize = true;
			AddCreateChoices(createAndGetGroup);
			AddGetChoices(createAndGetGroup);

			var openChoices = new TableLayoutPanel();
			openChoices.AutoSize = true;
			AddSection("Open", openChoices);
			AddOpenProjectChoices(openChoices);
			flowLayoutPanel1.Controls.AddRange(new Control[] { createAndGetGroup, openChoices });
		}

		private void AddSection(string sectionName, TableLayoutPanel panel)
		{
			 panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			panel.RowCount++;
			var label = new Label();
			label.Font = new Font(StringCatalog.LabelFont.FontFamily, _templateLabel.Font.Size, _templateLabel.Font.Style);
			label.ForeColor = _templateLabel.ForeColor;
			label.Text = sectionName;
			label.Margin = new Padding(0, 20, 0, 0);
			panel.Controls.Add(label);
		}

		private void AddFileChoice(string path, TableLayoutPanel panel)
		{
			var button = AddChoice(Path.GetFileNameWithoutExtension(path), path, "template", true, openRecentProject_LinkClicked, panel);
			button.Tag = path;
		}


		private Button AddChoice(string localizedLabel, string localizedTooltip, string imageKey, bool enabled,
   EventHandler clickHandler, TableLayoutPanel panel)
		{
			panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			panel.RowCount++;
			var button = new Button();
			button.Anchor = AnchorStyles.Top | AnchorStyles.Left;

			button.Width = _templateButton.Width;//review
			button.Font = new Font(StringCatalog.LabelFont.FontFamily, _templateButton.Font.Size, _templateButton.Font.Style);
			if (imageKey == "template") // this is used for the MRU list, the client can set it by changing the template
			{
				button.Image = _templateButton.Image;
			}
			else
			{
				button.ImageKey = imageKey;
				button.ImageList = _imageList;
			}
			button.ImageAlign = ContentAlignment.MiddleLeft;
			button.Click += clickHandler;
			button.Text = "  "+localizedLabel;

			button.FlatAppearance.BorderSize = _templateButton.FlatAppearance.BorderSize;
			button.FlatStyle = _templateButton.FlatStyle;
			button.ImageAlign = _templateButton.ImageAlign;
			button.TextImageRelation = _templateButton.TextImageRelation ;
			button.UseVisualStyleBackColor = _templateButton.UseVisualStyleBackColor;
			button.Enabled = enabled;

			toolTip1.SetToolTip(button, localizedTooltip);
			panel.Controls.Add(button);
			return button;
		}

		private void AddCreateChoices(TableLayoutPanel panel)
		{
			AddSection("Create", panel);
			AddChoice(_createNewProjectButtonLabel, string.Empty, "newProject", true, CreateNewProject_LinkClicked, panel);
		   // For wesay, we can add a method to allow the client dialog to add ones like this:
			//AddChoice("Create new project from FLEx LIFT export", string.Empty, "flex", true, OnCreateProjectFromFLEx_LinkClicked, panel);
		}

		private void AddGetChoices(TableLayoutPanel panel)
		{
			AddSection("Get", panel);
			//nb: we want these always enabled, so that we can give a message explaining about hg if needed

			var usbButton = AddChoice("Get From USB drive", "Get a library from a Chorus repository on a USB flash drive", "getFromUsb", true, OnGetFromUsb, panel);
			var internetButton = AddChoice("Get from Internet", "Get a library from a Chorus repository which is hosted on the internet (e.g. languagedepot.org) and put it on this computer",
				"getFromInternet", true, OnGetFromInternet, panel);
//            if (!string.IsNullOrEmpty(Chorus.VcsDrivers.Mercurial.HgRepository.GetEnvironmentReadinessMessage("en")))
//            {
//                usbButton.ForeColor = Color.Gray;
//                internetButton.ForeColor = Color.Gray;
//            }
		}

		private void OnGetFromInternet(object sender, EventArgs e)
		{
//            if (!Chorus.UI.Misc.ReadinessDialog.ChorusIsReady)
//            {
//                using (var dlg = new Chorus.UI.Misc.ReadinessDialog())
//                {
//                    dlg.ShowDialog();
//                    return;
//                }
//            }

			if (!Directory.Exists(_defaultParentDirectoryForProjects))
			{
				//e.g. mydocuments/wesay
				Directory.CreateDirectory(_defaultParentDirectoryForProjects);
			}
			using (var dlg = new Chorus.UI.Clone.GetCloneFromInternetDialog(_defaultParentDirectoryForProjects))
			{
				if (DialogResult.Cancel == dlg.ShowDialog())
					return;
				SelectProjectAndClose(dlg.PathToNewProject);
			}
		}

		private void OnGetFromUsb(object sender, EventArgs e)
		{
			if (!Directory.Exists(_defaultParentDirectoryForProjects))
			{
				//e.g. mydocuments/wesay
				Directory.CreateDirectory(_defaultParentDirectoryForProjects);
			}
			using (var dlg = new Chorus.UI.Clone.GetCloneFromUsbDialog(_defaultParentDirectoryForProjects))
			{
				dlg.Model.ProjectFilter = dir => _looksLikeValidProjectPredicate(dir);
				if (DialogResult.Cancel == dlg.ShowDialog())
					return;
				SelectProjectAndClose(dlg.PathToNewProject);
			}
		}

//        private static bool GetLooksLikeWeSayProject(string directoryPath)
//        {
//            return Directory.GetFiles(directoryPath, "*.WeSayConfig").Length > 0;
//        }

		private void AddOpenProjectChoices(TableLayoutPanel panel)
		{
			if (_mruList != null)
			{
				int count = 0;
				foreach (string path in _mruList.Paths)
				{
					AddFileChoice(path, panel);
					++count;
					if (count > 2)
						break;

				}
			}
			else
			{
				AddChoice("MRU list must be set at runtime", string.Empty, "blah blah", true, null, panel);
			}
			AddChoice(_browseLabel, string.Empty, "browse", true, OnBrowseForExistingProjectClick, panel);
		}

		private void openRecentProject_LinkClicked(object sender, EventArgs e)
		{
			SelectProjectAndClose(((Button) sender).Tag as string);
		}

		public string SelectedPath
		{
			get; private set;
		}

		private void OnBrowseForExistingProjectClick(object sender, EventArgs e)
		{
			if(!Directory.Exists(_defaultParentDirectoryForProjects))
			{
				Directory.CreateDirectory(_defaultParentDirectoryForProjects);
			}

			using (var dlg = new FolderBrowserDialog())
			{
				dlg.ShowNewFolderButton =false;

				//dlg. = "Open Project";

//				var prjFilterText = LocalizationManager.LocalizeString(
//					"WelcomeDialog.ProjectFileType", "SayMore Project (*.sprj)",
//					locExtender.LocalizationGroup);

				//dlg.Filter = prjFilterText + "|*.sprj";
				dlg.SelectedPath = _defaultParentDirectoryForProjects;
				//dlg.InitialDirectory = _defaultParentDirectoryForProjects;
				//dlg.CheckFileExists = true;
				//dlg.CheckPathExists = true;
				if (dlg.ShowDialog(this) == DialogResult.Cancel)
					return;

				SelectProjectAndClose(dlg.SelectedPath);
			}
		}

		private void CreateNewProject_LinkClicked(object sender, EventArgs e)
		{
			SelectProjectAndClose(_createNewProjectAndReturnPath());
		}

		public void SelectProjectAndClose(string path)
		{
			SelectedPath = path;
			if(!string.IsNullOrEmpty(path))
			{
				_mruList.AddNewPath(path);
				Invoke(DoneChoosingOrCreatingProject);
			}
		}

		private void OnLoad(object sender, EventArgs e)
		{
			LoadButtons();
		}

//        private void OnCreateProjectFromFLEx_LinkClicked(object sender, EventArgs e)
//        {
//            if (NewProjectFromFlexClicked != null)
//            {
//                NewProjectFromFlexClicked.Invoke(this, null);
//            }
//        }

		public string GetVersionInfo(string fmt)
		{
			Version ver = Assembly.GetExecutingAssembly().GetName().Version;

			// The build number is just the number of days since 01/01/2000
			DateTime bldDate = new DateTime(2000, 1, 1).AddDays(ver.Build);

			return string.Format(fmt, ver.Major, ver.Minor,
				ver.Revision, bldDate.ToString("dd-MMM-yyyy"));
		}
	}
}