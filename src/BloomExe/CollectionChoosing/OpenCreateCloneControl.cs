using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Bloom.Collection;
using Palaso.i18n;

namespace Bloom.CollectionChoosing
{
	public partial class OpenCreateCloneControl: UserControl
	{
		private string _defaultParentDirectoryForLibrarys;
		private  MostRecentPathsList _mruList;
		private Func<string, bool> _looksLikeValidLibraryPredicate;
		private string _createNewLibraryButtonLabel;
		private Func<string> _createNewLibraryAndReturnPath;
		private string _browseLabel;
		private string _filterString;

		public event EventHandler DoneChoosingOrCreatingLibrary;

		public OpenCreateCloneControl()
		{
			Font = SystemFonts.MessageBoxFont;//use the default OS UI font
			InitializeComponent();
		}

		public void Init(MostRecentPathsList mruList,
			string createNewLibraryButtonLabel,
			string browseForOtherLibrarysLabel,
			string filterString,
			Func<string, bool> looksLikeValidLibraryPredicate,
			Func<string> createNewLibraryAndReturnPath)
		 {
			_filterString = filterString;
			_createNewLibraryAndReturnPath = createNewLibraryAndReturnPath;
			_browseLabel = browseForOtherLibrarysLabel;
			_createNewLibraryButtonLabel = createNewLibraryButtonLabel;
			_mruList = mruList;
			_looksLikeValidLibraryPredicate = looksLikeValidLibraryPredicate;
			//this.pictureBox1.Image = headerImage;
		  }

		/// <summary>
		/// Client should use this, at least, to set the icon (the image) for mru items
		/// </summary>
		public Button TemplateButton { get { return _templateButton; } }


		private void LoadButtons()
		{
			_templateButton.Parent.Controls.Remove(_templateButton);

//            var createAndGetGroup = new TableLayoutPanel();
//            createAndGetGroup.AutoSize = true;
			//AddCreateChoices(createAndGetGroup);
		  //  AddGetChoices(createAndGetGroup);



			//AddSection("Open", openChoices);
			AddOpenChoices();
			//flowLayoutPanel1.Controls.AddRange(new Control[] { createAndGetGroup, openChoices });
		}

//        private void AddSection(string sectionName, TableLayoutPanel panel)
//        {
//             panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
//            panel.RowCount++;
//            var label = new Label();
//            label.Font = new Font(StringCatalog.LabelFont.FontFamily, _templateLabel.Font.Size, _templateLabel.Font.Style);
//            label.ForeColor = _templateLabel.ForeColor;
//            label.Text = sectionName;
//            label.Margin = new Padding(0, 20, 0, 0);
//            panel.Controls.Add(label);
//        }

		private void AddFileChoice(string path,int index)
		{
			const int kRowOffsetForMRUChoices = 1;
			var button = AddChoice(Path.GetFileNameWithoutExtension(path), path, "template", true, openRecentLibrary_LinkClicked, index+kRowOffsetForMRUChoices);
			button.Tag = path;
		}


		private Button AddChoice(string localizedLabel, string localizedTooltip, string imageKey, bool enabled,
   EventHandler clickHandler, int row)
		{

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
				//button.ImageList = _imageList;
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
			tableLayoutPanel2.Controls.Add(button);
			tableLayoutPanel2.SetRow(button, row);
			return button;
		}



//        private void AddGetChoices(TableLayoutPanel panel)
//        {
//          //  AddSection("Get", panel);
//            //nb: we want these always enabled, so that we can give a message explaining about hg if needed
//
//            var usbButton = AddChoice("Get From USB drive", "Get a library from a Chorus repository on a USB flash drive", "getFromUsb", true, OnGetFromUsb, panel);
//            var internetButton = AddChoice("Get from Internet", "Get a library from a Chorus repository which is hosted on the internet (e.g. languagedepot.org) and put it on this computer",
//                "getFromInternet", true, OnGetFromInternet, panel);
////            if (!string.IsNullOrEmpty(Chorus.VcsDrivers.Mercurial.HgRepository.GetEnvironmentReadinessMessage("en")))
////            {
////                usbButton.ForeColor = Color.Gray;
////                internetButton.ForeColor = Color.Gray;
////            }
//        }

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

		private void AddOpenChoices()
		{
			int count = 0;

			if (_mruList != null)
			{

				foreach (string path in _mruList.Paths)
				{
					AddFileChoice(path, count);
					++count;
					if (count > 3)
						break;

				}
			}
			else
			{
				//AddChoice("MRU list must be set at runtime", string.Empty, "blah blah", true, null, panel);
			}
			AddChoice(_browseLabel, string.Empty, "browse", true, OnBrowseForExistingLibraryClick, count);
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
				dlg.Title = "Open Collection";

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
			var settings = new CollectionSettings(desiredOrExistingSettingsFilePath);
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



	}


}