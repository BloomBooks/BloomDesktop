using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Bloom.Book;

namespace Bloom
{
	public interface ITemplateFinder
	{
		Book.Book FindTemplateBook(string key);
	}

	public class SourceCollectionsList : ITemplateFinder
	{
		private readonly Book.Book.Factory _bookFactory;
		private readonly BookStorage.Factory _storageFactory;
		private readonly BookCollection.Factory _bookCollectionFactory;
		private readonly string _editableCollectionDirectory;

		//for moq'ing
		public SourceCollectionsList(){}

		public SourceCollectionsList(Book.Book.Factory bookFactory, BookStorage.Factory storageFactory, BookCollection.Factory bookCollectionFactory, string editableCollectionDirectory)
		{
			_bookFactory = bookFactory;
			_storageFactory = storageFactory;
			_bookCollectionFactory = bookCollectionFactory;
			_editableCollectionDirectory = editableCollectionDirectory;
		}

		public IEnumerable<string> RepositoryFolders
		{
			get;
			set;
		}

		public Book.Book FindTemplateBook(string key)
		{
			foreach (var root in RepositoryFolders)
			{
				if (!Directory.Exists(root))
					continue;
				foreach (var collectionDir in Directory.GetDirectories(root))
				{
					foreach (var templateDir in Directory.GetDirectories(collectionDir))
					{
						if (Path.GetFileName(templateDir) == key)
							return _bookFactory(_storageFactory(templateDir), false);
								//review: this is loading the book both in the librarymodel, and here
					}
				}

				//dereference shortcuts to folders living elsewhere

				foreach (var shortcut in Directory.GetFiles(root, "*.lnk", SearchOption.TopDirectoryOnly))
				{
					var collectionDir = ResolveShortcut.Resolve(shortcut);
					if (Directory.Exists(collectionDir))
					{
						foreach (var templateDir in Directory.GetDirectories(collectionDir))
						{
							if (Path.GetFileName(templateDir) == key)
								return _bookFactory(_storageFactory(templateDir), false);
							//review: this is loading the book both in the librarymodel, and here
						}
					}
				}
			}
			return null;
		}

		public virtual IEnumerable<BookCollection> GetSourceCollections()
		{
			foreach (var root in RepositoryFolders)
			{
				if (!Directory.Exists(root))
					continue;

				foreach (var dir in Directory.GetDirectories(root))
				{
					if (dir == _editableCollectionDirectory || Path.GetFileName(dir).StartsWith(".")) //skip thinks like .idea, .hg, etc.
						continue;
					yield return _bookCollectionFactory(dir, BookCollection.CollectionType.SourceCollection);
				}

				//follow shortcuts
				foreach (var shortcut in Directory.GetFiles(root, "*.lnk", SearchOption.TopDirectoryOnly))
				{
					var path = ResolveShortcut.Resolve(shortcut);
					if (path!=_editableCollectionDirectory && Directory.Exists(path))
						yield return _bookCollectionFactory(path, BookCollection.CollectionType.SourceCollection);
				}
			}
		}
	}
}