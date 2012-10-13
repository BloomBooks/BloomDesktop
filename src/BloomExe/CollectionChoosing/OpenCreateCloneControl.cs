using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Bloom.Collection;
using Bloom.CollectionCreating;
using Bloom.Properties;
using Palaso.i18n;
using Palaso.Extensions;

namespace Bloom.CollectionChoosing
{
	public partial class OpenCreateCloneControl : UserControl
	{
		private MostRecentPathsList _mruList;
		private Func<string> _createNewLibraryAndReturnPath;
		private string _filterString;
		private Func<string, bool> _looksLikeValidLibraryPredicate;

		public event EventHandler DoneChoosingOrCreatingLibrary;

		public string SelectedPath { get; private set; }

		public OpenCreateCloneControl()
		{
			Font = SystemFonts.MessageBoxFont; //use the default OS UI font
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
			_mruList = mruList;
			_looksLikeValidLibraryPredicate = looksLikeValidLibraryPredicate;
			//this.pictureBox1.Image = headerImage;
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

			int count = 0;
			foreach (string path in _mruList.Paths)
			{
				AddFileChoice(path, count);
				++count;
				if (count > 3)
					break;
			}

			foreach (Control control in tableLayoutPanel2.Controls)
			{
				if (control.Tag == "sendreceive")
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
			//            if (!Chorus.UI.Misc.ReadinessDialog.ChorusIsReady)
			//            {
			//                using (var dlg = new Chorus.UI.Misc.ReadinessDialog())
			//                {
			//                    dlg.ShowDialog();
			//                    return;
			//                }
			//            }

			if (!Directory.Exists(NewCollectionWizard.DefaultParentDirectoryForCollections))
			{
				//e.g. mydocuments/wesay
				Directory.CreateDirectory(NewCollectionWizard.DefaultParentDirectoryForCollections);
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
			if (!Directory.Exists(NewCollectionWizard.DefaultParentDirectoryForCollections))
			{
				//e.g. mydocuments/wesay
				Directory.CreateDirectory(NewCollectionWizard.DefaultParentDirectoryForCollections);
			}
			//			using (var dlg = new Chorus.UI.Clone.GetCloneFromUsbDialog(_defaultParentDirectoryForLibrarys))
			//            {
			//            	dlg.Model.LibraryFilter = dir => _looksLikeValidLibraryPredicate(dir);
			//                if (DialogResult.Cancel == dlg.ShowDialog())
			//                    return;
			//				SelectLibraryAndClose(dlg.PathToNewLibrary);
			//            }
		}

		private void OnGetFromChorusHub(object sender, EventArgs e)
		{

		}

		//        private static bool GetLooksLikeWeSayLibrary(string directoryPath)
		//        {
		//            return Directory.GetFiles(directoryPath, "*.WeSayConfig").Length > 0;
		//        }


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
				_mruList.AddNewPath(path);
				Invoke(DoneChoosingOrCreatingLibrary);
			}
		}
	}
}