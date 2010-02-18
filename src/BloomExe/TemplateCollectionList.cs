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

		public TemplateCollectionList(Book.Factory bookFactory)
		{
			_bookFactory = bookFactory;
		}

		public IEnumerable<string> ReposistoryFolders
		{
			get;
			set;
		}

		public Book FindTemplateBook(string key)
		{
			foreach (var root in ReposistoryFolders)
			{
				foreach (var collection in Directory.GetDirectories(root))
				{
					foreach (var templateDir in Directory.GetDirectories(collection))
					{
						if (Path.GetFileName(templateDir) == key)
							return _bookFactory(templateDir);
								//review: this is loading the book both in the librarymodel, and here
					}
				}
			}
			return null;
		}
	}
}