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
	/// The CollectionFilter class allows passing an IFilter to a folder compression routine for an
	/// entire collection by combining the book filters of all the books in it.
	/// </summary>
	internal class CollectionFilter: IFilter
	{
		private string _rootFolder;
		// Each filter must be in the same collection.
		// key is the folder name, under _rootFolder, for the filter for files in that subfolder.
		private Dictionary<string, BookFileFilter> _bookFilters = new Dictionary<string, BookFileFilter>();

		public bool Filter(string fullPath)
		{
			if (_rootFolder == null) return false; // can't accept anything without at least one book so this gets initialized.
			var relativePath = fullPath.Substring(_rootFolder.Length + 1);
			var index = relativePath.IndexOf(Path.DirectorySeparatorChar);
			if (index < 0)
			{
				// file in the root folder
				return Path.GetExtension(relativePath).ToLowerInvariant() == ".css";
			}
			var folder = relativePath.Substring(0, index);
			if (_bookFilters.TryGetValue(folder, out BookFileFilter bookFilter))
				return bookFilter.Filter(fullPath);
			return false;
		}

		public void AddBookFilter(BookFileFilter bookFilter)
		{
			var root = Path.GetDirectoryName(bookFilter.BookFolderPath);
			if (_rootFolder == null)
			{
				_rootFolder = root;
			}
			Debug.Assert(_rootFolder == root, "CollectionFilter requires all books to be in the same parent collection");
			var folder = Path.GetFileName(bookFilter.BookFolderPath);
			_bookFilters[folder] = bookFilter;
		}
	}
}
