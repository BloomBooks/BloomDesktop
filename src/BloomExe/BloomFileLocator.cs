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


		/// <summary>
		/// This is Bloom's achilles heel.
		/// </summary>
		/// <returns></returns>
		protected override IEnumerable<string> GetSearchPaths()
		{
			//Note: the order here has major ramifications, as it's quite common to have mutliple copies of the same file around
			//in several of our locations.
			//For example, if do this:
			//    return base.GetSearchPaths().Concat(paths);
			//Then we will favor the paths known to the base class over those we just compiled in the lines above.
			//One particular bug that came out of that was when a custom xmatter (because of a previous bug) snuck into the
			//Sample "Vaccinations" book, then *that* copy of the xmatter was always used, becuase it was found first.


			//NOTE also that while this all was working as of Bloom 1.0, there's no reason that it *should always* work to have
			//a single ordering of files that works for all different kinds of files (css we ship, other people ship explicitly, that comes in a shell books, etc.)

			foreach (var xMatterInfo in _xMatterPackFinder.All)
			{
				//NB: if we knew what the xmatter pack they wanted, we could limit to that. for now, we just iterate over all of
				//them and rely (reasonably) on the names being unique

				//this is a bit weird... we include the parent, in case they're looking for the xmatter *folder*, and the folder
				//itself, in case they're looking for something inside it
				yield return xMatterInfo.PathToFolder;
				yield return Path.GetDirectoryName(xMatterInfo.PathToFolder);
			}

			//REVIEW: this one is just a big grab bag of almost all folders we know about. This could really bite us.
			foreach (var searchPath in _searchPaths)
			{
				yield return searchPath;

			}
			yield return _collectionSettings.FolderPath;
		}

		public override IFileLocator CloneAndCustomize(IEnumerable<string> addedSearchPaths)
		{
			return new BloomFileLocator(_collectionSettings, _xMatterPackFinder, new List<string>(_searchPaths.Concat(addedSearchPaths)));
		}
	}
}
