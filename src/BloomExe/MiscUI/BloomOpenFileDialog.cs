using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using SIL.IO;

namespace Bloom.MiscUI
{
    public class BloomOpenFileDialog : IDisposable
    {
        OpenFileDialog _dialog = new OpenFileDialog();
        private bool _isDisposed;

        public BloomOpenFileDialog()
        {
            Multiselect = false;
            CheckFileExists = true;
            CheckPathExists = true;
            // The Windows file dialog otherwise leaves the process's current working directory
            // wherever the user last browsed, for the rest of the session. Bloom looks for some of
            // the files it ships with relative to that directory, so letting a dialog move it can
            // break those lookups long after the dialog is closed. See BL-16577 and BL-16230.
            // (This only restores the process directory; the dialog still opens where the shell or
            // our InitialDirectory says it should.)
            RestoreDirectory = true;
            _dialog.FileOk += (sender, args) =>
            {
                // Truly enforce the filter. See BL-12929 and BL-13552.
                // When Multiselect is on, check every selected file, not just the first one.
                foreach (var path in _dialog.FileNames)
                {
                    if (!DoubleCheckFileFilter(_dialog.Filter, path))
                    {
                        args.Cancel = true;
                        return;
                    }
                }
            };
        }

        public string Filter
        {
            get { return _dialog.Filter; }
            set { _dialog.Filter = value; }
        }

        public int FilterIndex
        {
            get { return _dialog.FilterIndex; }
            set { _dialog.FilterIndex = value; }
        }

        public bool Multiselect
        {
            get { return _dialog.Multiselect; }
            set { _dialog.Multiselect = value; }
        }

        public string Title
        {
            get { return _dialog.Title; }
            set { _dialog.Title = value; }
        }

        public string InitialDirectory
        {
            get { return _dialog.InitialDirectory; }
            set { _dialog.InitialDirectory = value; }
        }

        public bool RestoreDirectory
        {
            get { return _dialog.RestoreDirectory; }
            set { _dialog.RestoreDirectory = value; }
        }

        public bool CheckFileExists
        {
            get { return _dialog.CheckFileExists; }
            set { _dialog.CheckFileExists = value; }
        }

        public bool CheckPathExists
        {
            get { return _dialog.CheckPathExists; }
            set { _dialog.CheckPathExists = value; }
        }

        public string FileName
        {
            get { return _dialog.FileName; }
            set { _dialog.FileName = value; }
        }

        /// <summary>
        /// The full paths of all files the user selected. When Multiselect is false this
        /// contains just the single selected file (or is empty if none was chosen).
        /// </summary>
        public string[] FileNames
        {
            get { return _dialog.FileNames; }
        }

        /// <summary>
        /// The path the next file or folder chooser should answer with instead of showing a
        /// dialog, or null for the normal behaviour. An e2e test sets this through
        /// e2e/nextFileToChoose, an endpoint that exists only under --e2e; nothing else writes it.
        /// It is taken (and cleared) by the one chooser it answers, so a test arms it once per
        /// dialog it expects. Both this class and BloomFolderChooser consume it, so a test does
        /// not have to know which of the two a feature opens.
        /// </summary>
        private static string _nextPathToChooseInE2eTests;

        /// <summary>
        /// Arm the next file or folder chooser to answer with this path (see
        /// _nextPathToChooseInE2eTests).
        /// </summary>
        public static void SetNextPathToChooseInE2eTests(string path)
        {
            _nextPathToChooseInE2eTests = path;
        }

        /// <summary>
        /// Take the armed path, clearing it, if there is one and this is an e2e run; otherwise
        /// return false. Honoured only under --e2e, so a normal run always shows its dialog even
        /// if something managed to set the path.
        /// </summary>
        public static bool TryTakeNextPathToChooseInE2eTests(out string path)
        {
            path = _nextPathToChooseInE2eTests;
            if (!Program.RunningE2eTests || string.IsNullOrEmpty(path))
            {
                path = null;
                return false;
            }
            _nextPathToChooseInE2eTests = null;
            return true;
        }

        public DialogResult ShowDialog()
        {
            if (TryAnswerForE2eTests(out var result))
                return result;
            return _dialog.ShowDialog();
        }

        public DialogResult ShowDialog(IWin32Window owner)
        {
            if (TryAnswerForE2eTests(out var result))
                return result;
            return _dialog.ShowDialog(owner);
        }

        /// <summary>
        /// Under --e2e, answer with the path a test armed rather than showing the dialog: the
        /// caller then runs its real post-dialog code with FileName(s) set, as though a person
        /// had chosen that file. A native dialog would hang the run (see AUTOMATION-DEBT.md,
        /// "Native OS dialogs hang automation"). The checks a real dialog makes are made here
        /// too, and fail loudly, because a test that arms a missing file or one the filter would
        /// have refused has a bug that must not pass as a chosen file.
        /// </summary>
        private bool TryAnswerForE2eTests(out DialogResult result)
        {
            result = DialogResult.None;
            if (!TryTakeNextPathToChooseInE2eTests(out var path))
                return false;
            if (CheckFileExists && !RobustFile.Exists(path))
                throw new FileNotFoundException(
                    $"The e2e test armed the file chooser with a file that does not exist: {path}",
                    path
                );
            if (!DoubleCheckFileFilter(Filter, path))
                throw new ArgumentException(
                    $"The e2e test armed the file chooser with {path}, which its filter ({Filter}) would not have allowed."
                );
            _dialog.FileName = path;
            result = DialogResult.OK;
            return true;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    _dialog.Dispose();
                }
                _isDisposed = true;
            }
        }

        public void Dispose()
        {
            // This implements the IDisposable pattern, and is needed for the using statements to work correctly.
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Return true if the filePath truly passes the filtering of the filterString.
        /// People can defeat the filter in the file dialog by typing or pasting, so we
        /// need to double check.  (See BL-12929 and BL-13552.)
        /// </summary>
        /// <param name="filterString">filter string like those used in file dialogs</param>
        /// <param name="filePath">file path returned by a file dialog</param>
        public static bool DoubleCheckFileFilter(string filterString, string filePath)
        {
            //Debug.WriteLine($"DoubleCheckFileFilter: filterString = {filterString}, filePath = {filePath}");
            if (string.IsNullOrEmpty(filterString))
                return true; // no filter, so everything passes
            if (string.IsNullOrEmpty(filePath))
                return false; // no file, so nothing passes
            var filterSections = filterString.Split('|');
            if (filterSections.Length < 2)
                return true; // no filter, so everything passes
            var fileName = Path.GetFileName(filePath);
            for (int i = 1; i < filterSections.Length; i += 2)
            {
                if (PassesFilter(filterSections[i], fileName))
                    return true;
            }
            return false;
        }

        private static bool PassesFilter(string filterList, string fileName)
        {
            var parts = filterList.Split(';');
            foreach (var part in parts)
            {
                if (part == "*.*" || part == "*")
                    return true;
                var filter = part.Trim();
                if (filter.StartsWith("*"))
                {
                    filter = filter.Substring(1);
                    if (fileName.EndsWith(filter, StringComparison.InvariantCultureIgnoreCase))
                        return true;
                }
                else if (filter.EndsWith("*"))
                {
                    filter = filter.Substring(0, filter.Length - 1);
                    if (fileName.StartsWith(filter, StringComparison.InvariantCultureIgnoreCase))
                        return true;
                }
                else if (fileName.Equals(filter, StringComparison.InvariantCultureIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
