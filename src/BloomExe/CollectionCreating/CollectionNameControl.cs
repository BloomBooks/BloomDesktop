﻿using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Bloom.Collection;
using L10NSharp;
using SIL.IO;

namespace Bloom.CollectionCreating
{
	public partial class CollectionNameControl : UserControl, IPageControl
	{
		private Action<UserControl, bool> _setNextButtonState;
		private  string _destinationDirectory;
		private NewCollectionSettings _collectionInfo;

		public CollectionNameControl()
		{
			InitializeComponent();
		}
		public void Init(Action<UserControl, bool> SetButtonState, NewCollectionSettings collectionInfo, string destinationDirectory)
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
//				string[] dirs = Path.GetDirectoryName(_collectionInfo.PathToSettingsFile).Split(Path.DirectorySeparatorChar);
//				if (dirs.Length > 2)
//				{
//					htmlLabel1.ColorName = "gray";
//					string root = Path.Combine(dirs[dirs.Length - 3], dirs[dirs.Length - 2]);
//					htmlLabel1.HTML = String.Format("Collection will be created at: {0}",
//													Path.Combine(root, dirs[dirs.Length - 1]));
//				}

				htmlLabel1.ForeColor = Color.Gray;
				htmlLabel1.HTML = String.Format(LocalizationManager.GetString("NewCollectionWizard.CollectionWillBeCreatedAt","Collection will be created at: {0}"),
								_collectionInfo.PathToSettingsFile);
			}
			else
			{
				if (_collectionNameControl.Text.Length > 0)
				{
					htmlLabel1.ForeColor = Color.Red;
					if (DestinationAlreadyExists)
					{
						htmlLabel1.HTML = string.Format(LocalizationManager.GetString("NewCollectionWizard.AlreadyCollectionWithThatName","There is already a collection with that name, at <a href='file://{0}'>{0}</a>.\r\nPlease pick a unique name."), Path.GetDirectoryName(_collectionInfo.PathToSettingsFile));
					}
					else
					{
						htmlLabel1.HTML = LocalizationManager.GetString("NewCollectionWizard.UnableToCreateANewCollectionUsingThatName","Unable to create a new collection using that name.");
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
				return !DestinationAlreadyExists && _collectionNameControl.Text.ToLowerInvariant() != "templates";
		}

		private bool DestinationAlreadyExists
		{
			get
			{
				return (Directory.Exists(Path.GetDirectoryName(_collectionInfo.PathToSettingsFile))
					|| RobustFile.Exists(_collectionInfo.PathToSettingsFile));
			}
		}


		public void NowVisible()
		{
			_exampleText.Visible = _collectionInfo.IsSourceCollection;//our examples would just confuse someone if they're doing a vernacular collection

			if(!string.IsNullOrEmpty(_collectionInfo.PathToSettingsFile))
			{
				_collectionNameControl.Text = Path.GetFileNameWithoutExtension(_collectionInfo.PathToSettingsFile);
			}
			_setNextButtonState(this, GetNameIsOk());
		}
	}
}
