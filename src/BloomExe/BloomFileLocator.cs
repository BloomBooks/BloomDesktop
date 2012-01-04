using System;
using System.Collections.Generic;
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
			return base.GetSearchPaths().Concat(new[] { xMatterFolder.PathToFolder });
		}
	}
}
