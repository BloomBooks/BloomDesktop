using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Palaso.i18n;


namespace Bloom.ToPalaso
{
	public partial class WelcomeControl: UserControl
	{
		private string _defaultParentDirectoryForLibrarys;
		private  MostRecentPathsList _mruList;
		private Func<string, bool> _looksLikeValidLibraryPredicate;
		private string _createNewLibraryButtonLabel;
		private Func<NewLibraryInfo> _createNewLibraryAndReturnPath;
		private string _browseLabel;
		private string _filterString;

		public event EventHandler DoneChoosingOrCreatingLibrary;

		public WelcomeControl()
		{
			Font = SystemFonts.MessageBoxFont;//use the default OS UI font
			InitializeComponent();
		}

		public void Init(MostRecentPathsList mruList,
			string defaultParentDirectoryForLibrarys,
			string createNewLibraryButtonLabel,
			string browseForOtherLibrarysLabel,
			string filterString,
			Func<string, bool> looksLikeValidLibraryPredicate,
			Func<NewLibraryInfo> createNewLibraryAndReturnPath)
		 {
			_filterString = filterString;
			_createNewLibraryAndReturnPath = createNewLibraryAndReturnPath;
			_browseLabel = browseForOtherLibrarysLabel;
			_defaultParentDirectoryForLibrarys = defaultParentDirectoryForLibrarys;
			_createNewLibraryButtonLabel = createNewLibraryButtonLabel;
			_mruList = mruList;
			_looksLikeValidLibraryPredicate = looksLikeValidLibraryPredicate;
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
		  //  AddGetChoices(createAndGetGroup);

			var openChoices = new TableLayoutPanel();
			openChoices.AutoSize = true;
			AddSection("Open", openChoices);
			AddOpenLibraryChoices(openChoices);
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
			var button = AddChoice(Path.GetFileNameWithoutExtension(path), path, "template", true, openRecentLibrary_LinkClicked, panel);
			button.Tag = path;
		}


		private Button AddChoice(string localizedLabel, string localizedTooltip, string imageKey, bool enabled,
   EventHandler clickHandler, TableLayoutPanel panel)
		{
			panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			panel.RowCount++;
			var button = new Button();
			button.Anchor = AnchorStyles.Top | AnchorStyles.Left;

			button.Width = _templateButton.Width;
			button.Height = _templateButton.Height;
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
			AddChoice(_createNewLibraryButtonLabel, string.Empty, "newLibrary", true, CreateNewLibrary_LinkClicked, panel);
		   // For wesay, we can add a method to allow the client dialog to add ones like this:
			//AddChoice("Create new project from FLEx LIFT export", string.Empty, "flex", true, OnCreateLibraryFromFLEx_LinkClicked, panel);
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

			if (!Directory.Exists(_defaultParentDirectoryForLibrarys))
			{
				//e.g. mydocuments/wesay
				Directory.CreateDirectory(_defaultParentDirectoryForLibrarys);
			}
//			using (var dlg = new Chorus.UI.Clone.GetCloneFromInternetDialog(_defaultParentDirectoryForLibrarys))
//            {
//                if (DialogResult.Cancel == dlg.ShowDialog())
//                    return;
//				SelectLibraryAndClose(dlg.PathToNewLibrary);
//            }
		}

		private void OnGetFromUsb(object sender, EventArgs e)
		{
			if (!Directory.Exists(_defaultParentDirectoryForLibrarys))
			{
				//e.g. mydocuments/wesay
				Directory.CreateDirectory(_defaultParentDirectoryForLibrarys);
			}
//			using (var dlg = new Chorus.UI.Clone.GetCloneFromUsbDialog(_defaultParentDirectoryForLibrarys))
//            {
//            	dlg.Model.LibraryFilter = dir => _looksLikeValidLibraryPredicate(dir);
//                if (DialogResult.Cancel == dlg.ShowDialog())
//                    return;
//				SelectLibraryAndClose(dlg.PathToNewLibrary);
//            }
		}

//        private static bool GetLooksLikeWeSayLibrary(string directoryPath)
//        {
//            return Directory.GetFiles(directoryPath, "*.WeSayConfig").Length > 0;
//        }

		private void AddOpenLibraryChoices(TableLayoutPanel panel)
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
			AddChoice(_browseLabel, string.Empty, "browse", true, OnBrowseForExistingLibraryClick, panel);
		}

		private void openRecentLibrary_LinkClicked(object sender, EventArgs e)
		{
			SelectLibraryAndClose(((Button) sender).Tag as string);
		}

		public string SelectedPath
		{
			get; private set;
		}

		private void OnBrowseForExistingLibraryClick(object sender, EventArgs e)
		{
			if(!Directory.Exists(_defaultParentDirectoryForLibrarys))
			{
				Directory.CreateDirectory(_defaultParentDirectoryForLibrarys);
			}

			using (var dlg = new OpenFileDialog())
			{
				dlg.Title = "Open Library";

				dlg.Filter = _filterString;
				//dlg.InitialDirectory = _defaultParentDirectoryForLibrarys;
				dlg.CheckFileExists = true;
				dlg.CheckPathExists = true;
				if (dlg.ShowDialog(this) == DialogResult.Cancel)
					return;

				SelectLibraryAndClose(dlg.FileName);
			}
		}

		private void CreateNewLibrary_LinkClicked(object sender, EventArgs e)
		{
			var desiredOrExistingSettingsFilePath = _createNewLibraryAndReturnPath();
			if (desiredOrExistingSettingsFilePath == null)
				return;
			var settings = new LibrarySettings(desiredOrExistingSettingsFilePath);
			SelectLibraryAndClose(settings.SettingsFilePath);
		}

		public void SelectLibraryAndClose(string path)
		{
			SelectedPath = path;
			if(!string.IsNullOrEmpty(path))
			{
				_mruList.AddNewPath(path);
				Invoke(DoneChoosingOrCreatingLibrary);
			}
		}

		private void OnLoad(object sender, EventArgs e)
		{
			LoadButtons();
		}

//        private void OnCreateLibraryFromFLEx_LinkClicked(object sender, EventArgs e)
//        {
//            if (NewLibraryFromFlexClicked != null)
//            {
//                NewLibraryFromFlexClicked.Invoke(this, null);
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