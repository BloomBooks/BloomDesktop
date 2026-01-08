using System;
using System.Windows.Forms;
using Bloom.CollectionCreating;
using Bloom.Properties;

namespace Bloom.CollectionChoosing
{
    public partial class OpenAndCreateCollectionDialog : Form
    {
        public OpenAndCreateCollectionDialog(MostRecentPathsList mruList)
        {
            InitializeComponent();
            //_welcomeControl.TemplateLabel.ForeColor = Color.FromArgb(0x61, 0x94, 0x38);//0xa0, 0x3c, 0x50);
            _openAndCreateControl.TemplateButton.Image = Resources.collection32x32;
            _openAndCreateControl.TemplateButton.Image.Tag = "testfrombloom";

            _openAndCreateControl.Init(
                mruList,
                Bloom.Collection.CollectionSettings.GetFileDialogFilterString(),
                (IWin32Window owner) =>
                    NewCollectionWizard.CreateNewCollection(
                        () => _openAndCreateControl.UpdateUiLanguageMenuSelection(),
                        owner
                    )
            );

            _openAndCreateControl.DoneChoosingOrCreatingCollection += (x, y) =>
            {
                DialogResult = DialogResult.OK;
                Close();
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // BL-552, BL-779: a bug in Mono requires us to wait to set Icon until handle created.
            this.Icon = global::Bloom.Properties.Resources.BloomIcon;
        }

        public string SelectedPath
        {
            get { return _openAndCreateControl.SelectedPath; }
        }

        private void OpenAndCreateCollectionDialog_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void OpenAndCreateCollectionDialog_DragDrop(object sender, DragEventArgs e)
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length == 1)
            {
                _openAndCreateControl.SelectCollectionAndClose(files[0]);
            }
        }
    }
}
