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
	    private readonly IEnumerable<string> _factorySearchPaths;
        private readonly List<string> _bookSpecificSearchPaths;
        private readonly IEnumerable<string> _userInstalledSearchPaths;

	    public BloomFileLocator(CollectionSettings collectionSettings, XMatterPackFinder xMatterPackFinder, IEnumerable<string> factorySearchPaths, IEnumerable<string> userInstalledSearchPaths)
			: base(factorySearchPaths.Concat( userInstalledSearchPaths))//review: is this even used, since we override GetSearchPaths()?
	    {
	        _bookSpecificSearchPaths = new List<string>();
			_collectionSettings = collectionSettings;
			_xMatterPackFinder = xMatterPackFinder;
		    _factorySearchPaths = factorySearchPaths;
	        _userInstalledSearchPaths = userInstalledSearchPaths;;
		}

	    public override void AddPath(string path)
	    {
            _bookSpecificSearchPaths.Add(path);
	    }

	    /// <summary>
        /// This is Bloom's achilles heel.
        /// </summary>
        /// <returns></returns>
		protected override IEnumerable<string> GetSearchPaths()
		{
            //The versions of the files that come with the program should always win out.
            //NB: This should not include any sample books.
            foreach (var searchPath in _factorySearchPaths)
            {
                yield return searchPath;
            }
            
            //Note: the order here has major ramifications, as it's quite common to have mutliple copies of the same file around
            //in several of our locations.
            //For example, if do this:
            //    return base.GetSearchPaths().Concat(paths);
            //Then we will favor the paths known to the base class over those we just compiled in the lines above.
            //One particular bug that came out of that was when a custom xmatter (because of a previous bug) snuck into the
            //Sample "Vaccinations" book, then *that* copy of the xmatter was always used, becuase it was found first.

            foreach (var xMatterInfo in _xMatterPackFinder.All)
		    {
                //NB: if we knew what the xmatter pack they wanted, we could limit to that. for now, we just iterate over all of
                //them and rely (reasonably) on the names being unique

                //this is a bit weird... we include the parent, in case they're looking for the xmatter *folder*, and the folder
                //itself, in case they're looking for something inside it
                yield return xMatterInfo.PathToFolder;
                yield return Path.GetDirectoryName(xMatterInfo.PathToFolder);
		    }

            //REVIEW: this one is just a big grab bag of all folders we find in their programdata, installed stuff. This could be insufficient.
            foreach (var searchPath in _userInstalledSearchPaths)
		    {
		        yield return searchPath;
		    }

            //Book-specific paths (added by AddPath()) are last because we want people to get the latest stylesheet, 
            //not just the version the had when they made the book.
            //This may seem counter-intuitive. One scenario, which has played out many times, is that the
            //book has been started, and the customer requests some change to the stylesheet, which we deliver just by having them
            //double-click a bloompack.
            //Another scenario is that a new version of Bloom comes out that expects/needs the newer stylesheet
            foreach (var searchPath in _bookSpecificSearchPaths)
            {
                yield return searchPath;
            }

            
            yield return _collectionSettings.FolderPath;
		}

		public override IFileLocator CloneAndCustomize(IEnumerable<string> addedSearchPaths)
		{
			var locator= new BloomFileLocator(_collectionSettings, _xMatterPackFinder,_factorySearchPaths, _userInstalledSearchPaths);
            foreach (var path in _bookSpecificSearchPaths)
            {
                locator.AddPath(path);
            } 
            foreach (var path in addedSearchPaths)
		    {
		        locator.AddPath(path);
		    }
		    return locator;
		}
	}
}
