using System.IO;

namespace Bloom.Collection
{
    /// <summary>
    /// The ReaderToolsBloomPackCollectionFileFilter class allows us to filter out any top-level files we don't want to include in the pack.
    /// </summary>
    internal class ReaderToolsBloomPackCollectionFileFilter : CollectionFileFilter
    {
        public override bool ShouldAllow(string fullPath)
        {
            // Include everything for a normal collection.
            bool baseResult;
            if (baseResult = base.ShouldAllow(fullPath))
                return true;

            // Plus a couple extra files for a Reader Tools BloomPack.
            if (IsFileInRootFolder(fullPath, out var _))
                return Path.GetExtension(fullPath).ToLowerInvariant() == ".json"
                    && Path.GetFileName(fullPath).StartsWith("ReaderTools");

            return baseResult;
        }
    }
}
