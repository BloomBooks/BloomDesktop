using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Book;

namespace Bloom.Collection
{
    /// <summary>
    /// The CollectionFileFilter class allows passing an IFilter to a folder compression routine for an
    /// entire collection by combining the book filters of all the books in it.
    /// </summary>
    internal class CollectionFileFilter : IFilter
    {
        private string _rootFolder;

        // Each filter must be in the same collection.
        // key is the folder name, under _rootFolder, for the filter for files in that subfolder.
        private Dictionary<string, BookFileFilter> _bookFilters =
            new Dictionary<string, BookFileFilter>();

        public virtual bool ShouldAllow(string fullPath)
        {
            if (_rootFolder == null)
                return false; // can't accept anything without at least one book so this gets initialized.

            if (IsFileInRootFolder(fullPath, out var folder))
                return Path.GetExtension(fullPath).ToLowerInvariant() == ".css";

            if (folder.ToLowerInvariant() == "sample texts")
                return false;

            if (_bookFilters.TryGetValue(folder, out BookFileFilter bookFilter))
                return bookFilter.ShouldAllow(fullPath);

            return false;
        }

        protected bool IsFileInRootFolder(string fullPath, out string folder)
        {
            folder = null;

            var relativePath = fullPath.Substring(_rootFolder.Length + 1);
            var index = relativePath.IndexOf(Path.DirectorySeparatorChar);
            if (index == -1)
                return true;

            folder = relativePath.Substring(0, index);
            return false;
        }

        public void AddBookFilter(BookFileFilter bookFilter)
        {
            var root = Path.GetDirectoryName(bookFilter.BookFolderPath);
            if (_rootFolder == null)
            {
                _rootFolder = root;
            }
            if (_rootFolder != root)
                throw new ArgumentException(
                    "CollectionFileFilter requires all books to be in the same parent collection"
                );
            var folder = Path.GetFileName(bookFilter.BookFolderPath);
            _bookFilters[folder] = bookFilter;
        }
    }
}
