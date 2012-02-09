using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Book;
using Palaso.IO;

namespace Bloom
{
	public class BloomFileLocator : FileLocator
	{
		private readonly LibrarySettings _librarySettings;
		private readonly XMatterPackFinder _xMatterPackFinder;

		public BloomFileLocator(LibrarySettings librarySettings, XMatterPackFinder xMatterPackFinder, IEnumerable<string> searchPaths)
			: base(searchPaths)
		{
			_librarySettings = librarySettings;
			_xMatterPackFinder = xMatterPackFinder;
		}

		protected override IEnumerable<string> GetSearchPaths()
		{
			var xMatterFolder = _xMatterPackFinder.FindByKey(_librarySettings.XMatterPackName);
			if(null == xMatterFolder)
				xMatterFolder = _xMatterPackFinder.FindByKey("Factory");

			//this is a bit weird... we include the parent, in case they're looking for the xmatter *folder*, and the folder
			//itself, in case they're looking for something inside it
			return base.GetSearchPaths().Concat(new[] { Path.GetDirectoryName(xMatterFolder.PathToFolder), xMatterFolder.PathToFolder });
		}

		public BloomFileLocator CloneAndCustomize(IEnumerable<string> addedSearchPaths)
		{
			return new BloomFileLocator(_librarySettings, _xMatterPackFinder, new List<string>(_searchPaths.Concat(addedSearchPaths)));
		}

	}
}
