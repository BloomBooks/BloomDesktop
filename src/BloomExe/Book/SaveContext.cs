using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.Book
{
    /// <summary>
    /// This interface provides contextual information for a book to enable it to determine
    /// what it can't decide from internal knowledge about whether changes to it can currently
    /// be saved.
    /// </summary>
    /// <remarks>We may extend it with functions for requesting to be put into a saveable state.</remarks>
    public interface ISaveContext
    {
        bool CanSaveChanges(BookInfo info);
        bool CanChangeBookInstanceId(BookInfo info);
    }

    /// <summary>
    /// Implementation of ISaveContext appropriate for collections that are not TheOneEditableCollection.
    /// Saving changes is never allowed.
    /// </summary>
    class NoEditSaveContext : ISaveContext
    {
        public bool CanSaveChanges(BookInfo info)
        {
            return false;
        }

        public bool CanChangeBookInstanceId(BookInfo info)
        {
            return false;
        }

        public static readonly NoEditSaveContext Singleton = new NoEditSaveContext();
    }

    /// <summary>
    /// Implementation of ISaveContext appropriate for TheOneEditableCollection when it is not
    /// a team collection. As far as the collection is concerned, saving is always allowed.
    /// </summary>
    class AlwaysEditSaveContext : ISaveContext
    {
        public bool CanSaveChanges(BookInfo info)
        {
            return true;
        }

        public bool CanChangeBookInstanceId(BookInfo info)
        {
            return true;
        }

        public static readonly AlwaysEditSaveContext Singleton = new AlwaysEditSaveContext();
    }

    // (The other implementations are kinds of TeamCollections, where changes can be saved
    // only if the book is checked out.)
}
