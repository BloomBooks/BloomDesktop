using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

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
            _dialog.FileOk += (sender, args) =>
            {
                // Truly enforce the filter. See BL-12929 and BL-13552.
                if (!DoubleCheckFileFilter(_dialog.Filter, _dialog.FileName))
                    args.Cancel = true;
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

         public DialogResult ShowDialog()
        {
            return _dialog.ShowDialog();
        }

        public DialogResult ShowDialog(IWin32Window owner)
        {
            return _dialog.ShowDialog(owner);
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
