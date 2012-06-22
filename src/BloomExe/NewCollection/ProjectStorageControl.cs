using System;
using System.IO;
using System.Windows.Forms;
using Bloom.Collection;

namespace Bloom.NewCollection
{
	public partial class ProjectStorageControl : UserControl, IPageControl
	{
		private Action<UserControl, bool> _setNextButtonState;
		private  string _destinationDirectory;
		private NewCollectionInfo _collectionInfo;

		public ProjectStorageControl()
		{
			InitializeComponent();
		}
		public void Init(Action<UserControl, bool> SetButtonState, NewCollectionInfo collectionInfo, string destinationDirectory)
		{
			_setNextButtonState = SetButtonState;
			_collectionInfo = collectionInfo;
			_destinationDirectory = destinationDirectory;
		}
		protected void _textLibraryName_TextChanged(object sender, EventArgs e)
		{
			bool nameIsOK = GetNameIsOk();
			_setNextButtonState(this, nameIsOK);
			if (nameIsOK)
			{
				string[] dirs = Path.GetDirectoryName(_collectionInfo.PathToSettingsFile).Split(Path.DirectorySeparatorChar);
				if (dirs.Length > 2)
				{
					htmlLabel1.ColorName = "gray";
					string root = Path.Combine(dirs[dirs.Length - 3], dirs[dirs.Length - 2]);
					htmlLabel1.HTML = String.Format("Collection will be created at: {0}",
													Path.Combine(root, dirs[dirs.Length - 1]));
				}
			}
			else
			{
				if (_collectionNameControl.Text.Length > 0)
				{
					htmlLabel1.ColorName = "red";
					if (DestinationAlreadyExists)
					{
						htmlLabel1.HTML = string.Format("There is already a collection with that name, at <a href='file://{0}'>{0}</a>.\r\nPlease pick a unique name.", Path.GetDirectoryName(_collectionInfo.PathToSettingsFile));
					}
					else
					{
						htmlLabel1.HTML = "Unable to create a new collection using that name.";
					}
				}
				else
				{
					htmlLabel1.HTML  = "";
				}
			}
		}

		private bool GetNameIsOk()
		{
				if (_collectionNameControl.Text.Trim().Length < 1)
				{
					return false;
				}
				if (_collectionNameControl.Text.IndexOfAny(Path.GetInvalidFileNameChars()) > -1)
				{
					return false;
				}

				_collectionInfo.PathToSettingsFile = CollectionSettings.GetPathForNewSettings(_destinationDirectory, _collectionNameControl.Text);
				if (DestinationAlreadyExists)
					return false;
				return true;
		}

		private bool DestinationAlreadyExists
		{
			get
			{
				return (Directory.Exists(Path.GetDirectoryName(_collectionInfo.PathToSettingsFile))
					|| File.Exists(_collectionInfo.PathToSettingsFile));
			}
		}


		public void NowVisible()
		{
			if(!string.IsNullOrEmpty(_collectionInfo.PathToSettingsFile))
			{
				_collectionNameControl.Text = Path.GetFileNameWithoutExtension(_collectionInfo.PathToSettingsFile);
			}
			_setNextButtonState(this, GetNameIsOk());
		}
	}
}
