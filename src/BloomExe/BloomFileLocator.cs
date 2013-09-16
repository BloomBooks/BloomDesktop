using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Book;
using Bloom.Collection;
using Palaso.IO;

namespace Bloom
{
	public class BloomFileLocator : FileLocator
	{
		private readonly CollectionSettings _collectionSettings;
		private readonly XMatterPackFinder _xMatterPackFinder;

		public BloomFileLocator(CollectionSettings collectionSettings, XMatterPackFinder xMatterPackFinder, IEnumerable<string> searchPaths)
			: base(searchPaths)
		{
			_collectionSettings = collectionSettings;
			_xMatterPackFinder = xMatterPackFinder;
		}

		protected override IEnumerable<string> GetSearchPaths()
		{
			//review: if we knew what the xmatter pack they wanted, we could limit to that. for now, we just iterate over all of
			//them and rely (reasonably) on the names being unique
			var paths = new List<string>();
			paths.Add(_collectionSettings.FolderPath);
			foreach (var xMatterInfo in _xMatterPackFinder.All)
			{
				//this is a bit weird... we include the parent, in case they're looking for the xmatter *folder*, and the folder
				//itself, in case they're looking for something inside it
				paths.Add(xMatterInfo.PathToFolder);
				paths.Add(Path.GetDirectoryName(xMatterInfo.PathToFolder));
			}

			return base.GetSearchPaths().Concat(paths);
		}

		public override IFileLocator CloneAndCustomize(IEnumerable<string> addedSearchPaths)
		{
			return new BloomFileLocator(_collectionSettings, _xMatterPackFinder, new List<string>(_searchPaths.Concat(addedSearchPaths)));
		}
	}
}
