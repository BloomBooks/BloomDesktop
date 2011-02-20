using System.Collections.Generic;
using System.IO;
using Bloom.Library;

namespace Bloom
{
	public interface ITemplateFinder
	{
		Book FindTemplateBook(string key);
	}

	public class TemplateCollectionList : ITemplateFinder
	{
		private readonly Book.Factory _bookFactory;
		private readonly BookStorage.Factory _storageFactory;

		public TemplateCollectionList(Book.Factory bookFactory, BookStorage.Factory storageFactory)
		{
			_bookFactory = bookFactory;
			_storageFactory = storageFactory;
		}

		public IEnumerable<string> RepositoryFolders
		{
			get;
			set;
		}

		public Book FindTemplateBook(string key)
		{
			foreach (var root in RepositoryFolders)
			{
				if (!Directory.Exists(root))
					continue;
				foreach (var collection in Directory.GetDirectories(root))
				{
					//TODO: dereference shortcuts to folders living elsewhere

					foreach (var templateDir in Directory.GetDirectories(collection))
					{
						if (Path.GetFileName(templateDir) == key)
							return _bookFactory(_storageFactory(templateDir), false);
								//review: this is loading the book both in the librarymodel, and here
					}
				}
			}
			return null;
		}
	}
}