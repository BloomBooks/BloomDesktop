using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Bloom.Book;
using SIL.IO;
using SIL.Reporting;
using SIL.Windows.Forms.FileSystem;

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
		private string _factoryDir;

		private readonly BookSelection _bookSelection;

		//for moq only
		public BookCollection()
		{
		}

		// For unit tests only.
		internal BookCollection(List<BookInfo> state)
		{
			_bookInfos = state;
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
			if(RobustFile.Exists(path))
				return;
			RobustFile.Copy(BloomFileLocator.GetBrowserFile("bookLayout", "collection styles override template.css"), path);
		}

		public CollectionType Type { get; private set; }


		private void NotifyCollectionChanged()
		{
			if (CollectionChanged != null)
				CollectionChanged.Invoke(this, null);
		}

		public void DeleteBook(Book.BookInfo bookInfo)
		{
			var didDelete = ConfirmRecycleDialog.Recycle(bookInfo.FolderPath);
			if (!didDelete)
				return;

			Logger.WriteEvent("After BookStorage.DeleteBook({0})", bookInfo.FolderPath);
			//Debug.Assert(_bookInfos.Contains(bookInfo)); this will occur if we delete a book from the BloomLibrary section
			_bookInfos.Remove(bookInfo);

			if (CollectionChanged != null)
				CollectionChanged.Invoke(this, null);
			if (_bookSelection != null)
			{
				_bookSelection.SelectBook(null);
			}
		}

		public virtual string Name
		{
			get
			{
				var dirName = Path.GetFileName(_path);
				//the UI and existing Localizations want to see "templates", but on disk, "templates" is ambiguous, so the name there is "template books".
				return dirName == "template books" ? "Templates" : dirName;
			}
		}

		public string PathToDirectory
		{
			get { return _path; }

		}

		public virtual IEnumerable<Book.BookInfo> GetBookInfos()
		{
			if (_bookInfos == null)
			{
				_watcherIsDisabled = true;
				LoadBooks();
				_watcherIsDisabled = false;
			}

			return _bookInfos;
		}

		private void LoadBooks()
		{
			_bookInfos = new List<Book.BookInfo>();
			var bookFolders =  new DirectoryInfo(_path).GetDirectories();//although NTFS may already sort them, that's an implementation detail
			//var orderedBookFolders = bookFolders.OrderBy(f => f.Name);
			var orderedBookFolders = bookFolders.OrderBy(f => f.Name, new NaturalSortComparer<string>());
			foreach (var folder in orderedBookFolders)
			{
				if (Path.GetFileName(folder.FullName).StartsWith("."))//as in ".hg"
					continue;
				if (Path.GetFileName(folder.FullName).ToLowerInvariant().Contains("xmatter"))
					continue;
				if(RobustFile.Exists(Path.Combine(folder.FullName, ".bloom-ignore")))
					continue;
				AddBookInfo(folder.FullName);
			}
		}

		public void AddBookInfo(BookInfo bookInfo)
		{
			_bookInfos.Add(bookInfo);
			NotifyCollectionChanged();
		}

		/// <summary>
		/// Insert a book into the appropriate place. If there is already a book with the same FolderPath, replace it.
		/// </summary>
		/// <param name="bookInfo"></param>
		public void InsertBookInfo(BookInfo bookInfo)
		{
			IComparer<string> comparer = new NaturalSortComparer<string>();
			for (int i = 0; i < _bookInfos.Count; i++)
			{
				var compare = comparer.Compare(_bookInfos[i].FolderPath, bookInfo.FolderPath);
				if (compare == 0)
				{
					_bookInfos[i] = bookInfo; // Replace
					return;
				}
				if (compare > 0)
				{
					_bookInfos.Insert(i, bookInfo);
					return;
				}
			}
			_bookInfos.Add(bookInfo);
		}

		private void AddBookInfo(string folderPath)
		{
			try
			{
				//this is handy when windows explorer won't let go of the thumbs.db file, but we want to delete the folder
				if (Directory.GetFiles(folderPath, "*.htm").Length == 0 && Directory.GetFiles(folderPath, "*.html").Length == 0)
					return;
				var bookInfo = new BookInfo(folderPath, Type == CollectionType.TheOneEditableCollection);

				_bookInfos.Add(bookInfo);
			}
			catch (Exception e)
			{
				if (e.InnerException != null)
				{
					e = e.InnerException;
				}
				var jsonPath = Path.Combine(folderPath, BookInfo.MetaDataFileName);
				Logger.WriteError("Reading "+ jsonPath, e);
				try
				{
					Logger.WriteEvent(jsonPath +" Contents: " +System.Environment.NewLine+ RobustFile.ReadAllText(jsonPath));
				}
				catch(Exception readError)
				{
					Logger.WriteError("Error reading "+ jsonPath, readError);
				}
				
				//_books.Add(new ErrorBook(e, path, Type == CollectionType.TheOneEditableCollection));
				_bookInfos.Add(new ErrorBookInfo(folderPath, e){});
			}
		}

		protected Color CoverColor
		{
			get { throw new NotImplementedException(); }
			set { throw new NotImplementedException(); }
		}

		public const string DownloadedBooksCollectionNameInEnglish = "Books From BloomLibrary.org";

		public bool ContainsDownloadedBooks { get { return Name == DownloadedBooksCollectionNameInEnglish; } }

		/// <summary>
		/// This includes everything in "factoryCollections" (i.e. Templates folder AND Sample Shells:Vaccinations folder)
		/// </summary>
		public bool IsFactoryInstalled { get { return BloomFileLocator.IsInstalledFileOrDirectory(PathToDirectory); } }

		private FileSystemWatcher _watcher;
		/// <summary>
		/// Watch for changes to your directory (currently just additions). Raise CollectionChanged if you see anything.
		/// </summary>
		public void WatchDirectory()
		{
			_watcher = new FileSystemWatcher();
			_watcher.Path = PathToDirectory;
			// The default filter, LastWrite|FileName|DirectoryName, is probably OK.
			// Watch everything for now.
			// watcher.Filter = "*.txt";
			_watcher.Created += WatcherOnChange;
			_watcher.Changed += WatcherOnChange;

			// Begin watching.
			_watcher.EnableRaisingEvents = true;
		}

		/// <summary>
		/// This could plausibly be a Dispose(), but I don't want to make BoolCollection Disposable, as most of them don't need it.
		/// </summary>
		public void StopWatchingDirectory()
		{
			if (_watcher != null)
			{
				_watcher.Dispose();
				_watcher = null;
			}
		}

		public event EventHandler<ProjectChangedEventArgs> FolderContentChanged;
		private bool _watcherIsDisabled = false;

		private void WatcherOnChange(object sender, FileSystemEventArgs fileSystemEventArgs)
		{
			if (_watcherIsDisabled)
				return;
			_bookInfos = null; // Possibly obsolete; next request will update it.
			if (FolderContentChanged != null)
				FolderContentChanged(this, new ProjectChangedEventArgs() { Path = fileSystemEventArgs.FullPath });
		}
	}

	public class ProjectChangedEventArgs : EventArgs
	{
		public string Path { get; set; }
	}
}