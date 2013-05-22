using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Bloom.Book;
using Palaso.Reporting;

namespace Bloom.Collection
{
	public class BookCollection
	{
		public enum CollectionType
		{
			TheOneEditableCollection,
			SourceCollection
		}
		public delegate BookCollection Factory(string path, CollectionType collectionType);//autofac uses this

		public EventHandler CollectionChanged;

		private readonly string _path;
		private List<BookInfo> _bookInfos;

		private readonly BookSelection _bookSelection;


		private Color[] kCoverColors = new Color[] { Color.FromArgb(228, 140, 132), Color.FromArgb(176,222,228), Color.FromArgb(152, 208, 185), Color.FromArgb(194, 166, 191) };
		private int _coverColorIndex = 0;


		//for moq only
		public BookCollection()
		{
		}

		public BookCollection(string path, CollectionType collectionType,
			BookSelection bookSelection)
		{
			_path = path;
			_bookSelection = bookSelection;

			Type = collectionType;

			if (collectionType == CollectionType.TheOneEditableCollection)
			{
				MakeCollectionCSSIfMissing();
			}
		}

		private void MakeCollectionCSSIfMissing()
		{
			string path = Path.Combine(_path, "customCollectionStyles.css");
			if(File.Exists(path))
				return;
			File.Copy(BloomFileLocator.GetFileDistributedWithApplication("root","collection styles override template.css"),path);
		}

		public CollectionType Type { get; private set; }


		private void NotifyCollectionChanged()
		{
			if (CollectionChanged != null)
				CollectionChanged.Invoke(this, null);
		}

		public void DeleteBook(Book.BookInfo book)
		{
			var didDelete = Bloom.ConfirmRecycleDialog.Recycle(book.FolderPath);
			if (!didDelete)
				return;

			Logger.WriteEvent("After BookStorage.DeleteBook({0})", book.FolderPath);
			ListOfBooksIsOutOfDate();
			if (CollectionChanged != null)
				CollectionChanged.Invoke(this, null);
			if (_bookSelection != null)
			{
				_bookSelection.SelectBook(null);
			}
		}

		public virtual string Name
		{
			get { return Path.GetFileName(_path); }
		}

		public string PathToDirectory
		{
			get { return _path; }

		}


		private void ListOfBooksIsOutOfDate()
		{
			_bookInfos = null;
		}

		public virtual IEnumerable<Book.BookInfo> GetBookInfos()
		{
			if (_bookInfos == null)
			{
				LoadBooks();
			}

			return _bookInfos;
		}

		private void LoadBooks()
		{
			_bookInfos = new List<Book.BookInfo>();
			foreach(string path in Directory.GetDirectories(_path))
			{
				if (Path.GetFileName(path).StartsWith("."))//as in ".hg"
					continue;
				AddBookInfo(path);
			}
		}

		public void AddBookInfo(BookInfo bookInfo)
		{
			_bookInfos.Add(bookInfo);
			NotifyCollectionChanged();
		}

		private void AddBookInfo(string path)
		{
			try
			{
				//this is handy when windows explorer won't let go of the thumbs.db file, but we want to delete the folder
				if (Directory.GetFiles(path, "*.htm").Length == 0)
					return;
				var bookInfo = new BookInfo(path)
					{
						CoverColor = NextBookColor()
					};
				_bookInfos.Add(bookInfo);
			}
			catch (Exception e)
			{
				if (e.InnerException != null)
				{
					e = e.InnerException;
				}
				//_books.Add(new ErrorBook(e, path, Type == CollectionType.TheOneEditableCollection));
				_bookInfos.Add(new ErrorBookInfo(path, e){});
			}
		}

		protected Color CoverColor
		{
			get { throw new NotImplementedException(); }
			set { throw new NotImplementedException(); }
		}

		public Color NextBookColor()
		{
			return kCoverColors[_coverColorIndex++ % kCoverColors.Length];
		}


	}
}