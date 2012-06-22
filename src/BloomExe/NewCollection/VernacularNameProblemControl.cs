using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Collection;

namespace Bloom.NewCollection
{
	public partial class VernacularNameProblemControl : UserControl, IPageControl
	{
		private  Action<bool> _setNextButtonState;
		private  string _destinationDirectory;
		private NewCollectionInfo _collectionInfo;

		public VernacularNameProblemControl()
		{
			InitializeComponent();
		}
		public void Init(Action<bool> SetButtonState, NewCollectionInfo collectionInfo, string destinationDirectory)
		{
			_setNextButtonState = SetButtonState;
			_collectionInfo = collectionInfo;
			_destinationDirectory = destinationDirectory;
		}
		protected void _textLibraryName_TextChanged(object sender, EventArgs e)
		{
			_setNextButtonState(NameLooksOk);
			if (NameLooksOk)
			{
				string[] dirs = PathToNewLibraryDirectory.Split(Path.DirectorySeparatorChar);
				if (dirs.Length > 1)
				{
					string root = Path.Combine(dirs[dirs.Length - 3], dirs[dirs.Length - 2]);
					_pathLabel.Text = String.Format("Collection will be created at: {0}",
													Path.Combine(root, dirs[dirs.Length - 1]));
				}

				_pathLabel.ForeColor = Color.Gray;
				_pathLabel.Invalidate();
				Debug.WriteLine(_pathLabel.Text);
			}
			else
			{
				if (_textLibraryName.Text.Length > 0)
				{
					if (DestinationAlreadyExists)
					{
						_pathLabel.Text = string.Format("There is already a collection using with that name, at {0}", PathToNewLibraryDirectory);
					}
					else
					{
						_pathLabel.Text = "Unable to create a new collection using that name.";
					}
					_pathLabel.ForeColor = Color.Red;
				}
				else
				{
					_pathLabel.Text = "";
				}
			}
		}
		public string PathToNewLibraryDirectory
		{
			get { return Path.Combine(_destinationDirectory, _textLibraryName.Text); }
		}

		private bool NameLooksOk
		{
			get
			{
				//http://regexlib.com/Search.aspx?k=file+name
				//Regex legalFilePattern = new Regex(@"(.*?)");
				//               if (!(legalFilePattern.IsMatch(_textLibraryName.Text)))
				//               {
				//                   return false;
				//               }

				if (_textLibraryName.Text.Trim().Length < 1)
				{
					return false;
				}

				if (_textLibraryName.Text.IndexOfAny(Path.GetInvalidFileNameChars()) > -1)
				{
					return false;
				}

				if (DestinationAlreadyExists)
					return false;
				return true;
			}
		}

		private bool DestinationAlreadyExists
		{
			get
			{
				return (Directory.Exists(PathToNewLibraryDirectory) || File.Exists(PathToNewLibraryDirectory));
			}
		}

		public void NowVisible()
		{
			_setNextButtonState(NameLooksOk);
		}
	}
}
