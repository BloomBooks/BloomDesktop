using System;
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
        private Action<bool, bool> _setNextButtonState;
        private string _destinationDirectory;
        private NewCollectionSettings _collectionInfo;

        public CollectionNameControl()
        {
            InitializeComponent();
        }

        public void Init(
            Action<bool, bool> SetButtonState,
            NewCollectionSettings collectionInfo,
            string destinationDirectory
        )
        {
            _setNextButtonState = SetButtonState;
            _collectionInfo = collectionInfo;
            _destinationDirectory = destinationDirectory;
        }

        protected void _textCollectionName_TextChanged(object sender, EventArgs e)
        {
            bool nameIsOK = GetNameIsOk();
            _setNextButtonState(false, nameIsOK);
            if (nameIsOK)
            {
                _collectionInfoLabel.ForeColor = Color.Gray;
                _collectionInfoLabel.Text = String.Format(
                    LocalizationManager.GetString(
                        "NewCollectionWizard.CollectionWillBeCreatedAt",
                        "Collection will be created at: {0}"
                    ),
                    _collectionInfo.PathToSettingsFile
                );
            }
            else
            {
                if (_collectionNameControl.Text.Length > 0)
                {
                    _collectionInfoLabel.ForeColor = Color.Red;
                    if (DestinationAlreadyExists)
                    {
                        _collectionInfoLabel.Text = string.Format(
                            LocalizationManager.GetString(
                                "NewCollectionWizard.AlreadyCollectionWithThatName.V2",
                                "There is already a collection with that name, at {0}.\r\nPlease pick a unique name."
                            ),
                            Path.GetDirectoryName(_collectionInfo.PathToSettingsFile)
                        );
                    }
                    else
                    {
                        _collectionInfoLabel.Text = LocalizationManager.GetString(
                            "NewCollectionWizard.UnableToCreateANewCollectionUsingThatName",
                            "Unable to create a new collection using that name."
                        );
                    }
                }
                else
                {
                    _collectionInfoLabel.Text = "";
                }
            }
        }

        private bool GetNameIsOk()
        {
            // Judge the name we will really use: GetPathForNewSettings drops trailing periods and
            // spaces, because Windows drops them when it creates the folder (see BL-16679). A name
            // made up of nothing else would leave us with no folder name at all, and checking the
            // untrimmed text would let "templates." through to create the reserved "templates" folder.
            var effectiveName = _collectionNameControl.Text.Trim().TrimEnd('.', ' ');
            if (effectiveName.Length < 1)
            {
                return false;
            }
            // Check the raw text for characters a folder name can't contain. Not the trimmed name:
            // tab, newline and the other control characters are both invalid in a file name and
            // stripped by Trim(), so checking the trimmed name would pass a pasted "Foo<tab>" that
            // then fails when we try to create the folder -- the folder is built from the raw text.
            if (_collectionNameControl.Text.IndexOfAny(Path.GetInvalidFileNameChars()) > -1)
            {
                return false;
            }

            _collectionInfo.PathToSettingsFile = CollectionSettings.GetPathForNewSettings(
                _destinationDirectory,
                _collectionNameControl.Text
            );
            return !DestinationAlreadyExists && effectiveName.ToLowerInvariant() != "templates";
        }

        private bool DestinationAlreadyExists
        {
            get
            {
                return (
                    Directory.Exists(Path.GetDirectoryName(_collectionInfo.PathToSettingsFile))
                    || RobustFile.Exists(_collectionInfo.PathToSettingsFile)
                );
            }
        }

        public void NowVisible()
        {
            if (!string.IsNullOrEmpty(_collectionInfo.PathToSettingsFile))
            {
                _collectionNameControl.Text = Path.GetFileNameWithoutExtension(
                    _collectionInfo.PathToSettingsFile
                );
            }
            _setNextButtonState(false, GetNameIsOk());
        }
    }
}
