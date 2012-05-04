using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.WritingSystems;

namespace Bloom.NewCollection
{
	public partial class NewCollectionDialog: Form
	{
		private readonly string _destinationDirectory;
		public string Iso639Code;
		public string LanguageName;

		public NewCollectionDialog(string destinationDirectory)
		{
			_destinationDirectory = destinationDirectory;
			InitializeComponent();
			Icon = Application.OpenForms[0].Icon;
			_okButton.Enabled = false;
			_pathLabel.Text = "";
			_kindOfCollectionControl1.Left = _chooseLanguageButton.Left;
			_kindOfCollectionControl1.Width = btnCancel.Right - _kindOfCollectionControl1.Left;
			_kindOfCollectionControl1._nextButton.Click += new EventHandler(_nextButton_Click);
		}

		void _nextButton_Click(object sender, EventArgs e)
		{
			_kindOfCollectionControl1.Visible = false;
		}

		protected virtual bool EnableOK
		{
			get { return NameLooksOk && !string.IsNullOrEmpty(Iso639Code); }
		}

		protected void _textLibraryName_TextChanged(object sender, EventArgs e)
		{
			_okButton.Enabled = EnableOK;
			if (_okButton.Enabled)
			{
				string[] dirs = PathToNewLibraryDirectory.Split(Path.DirectorySeparatorChar);
				if (dirs.Length > 1)
				{
					string root = Path.Combine(dirs[dirs.Length - 3], dirs[dirs.Length - 2]);
					_pathLabel.Text = String.Format("Collection will be created at: {0}",
													Path.Combine(root, dirs[dirs.Length - 1]));
				}

				_pathLabel.Invalidate();
				Debug.WriteLine(_pathLabel.Text);
			}
			else
			{
				if (_textLibraryName.Text.Length > 0)
				{
					_pathLabel.Text = "Unable to create a new collection there.";
				}
				else
				{
					_pathLabel.Text = "";
				}
			}
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

				if (Directory.Exists(PathToNewLibraryDirectory) || File.Exists(PathToNewLibraryDirectory))
				{
					return false;
				}
				return true;
			}
		}



		public string PathToNewLibraryDirectory
		{
			get { return Path.Combine(_destinationDirectory, _textLibraryName.Text); }
		}

		protected void btnOK_Click(object sender, EventArgs e)
		{
			LibraryName = _textLibraryName.Text.Trim();
			DialogResult = DialogResult.OK;
			Close();
			if(IsShellMakingLibrary)
				UsageReporter.SendNavigationNotice("NewShellLibrary");
			else
				UsageReporter.SendNavigationNotice("NewLibrary");
		}

		public string LibraryName
		{
			get; private set;
		}

		public bool IsShellMakingLibrary
		{
			get
			{
				return _kindOfCollectionControl1._radioSourceCollection.Checked;
			}

		}

		protected void btnCancel_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
			Close();
		}

		private void _chooseLanguageButton_Click(object sender, EventArgs e)
		{
			using(var dlg = new LookupISOCodeDialog())
			{
				if(DialogResult.OK != dlg.ShowDialog())
				{
					return;
				}
				_languageInfoLabel.Text = string.Format("{0} ({1})", dlg.ISOCodeAndName.Name, dlg.ISOCode);
				Iso639Code = dlg.ISOCodeAndName.Code;
				LanguageName= dlg.ISOCodeAndName.Name;

				_textLibraryName.Text = dlg.ISOCodeAndName.Name + " Books";
			}
		}
	}
}