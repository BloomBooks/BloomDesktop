using System;
using Bloom.Book;

namespace Bloom.Edit
{
    /// <summary>
    /// Keeps track of the current page being edited.
    /// Earlier versions had about-to-change and changed events, but only EditingModel used them, and they
    /// did not play well with the new async way of handling Save.
    /// Review: EditingStateMachine also keeps track of the current page. Possibly we should expose that somehow
    /// and get rid of this class?
    /// </summary>
    public class PageSelection
    {
        /// <summary>
        /// Set the current page.
        /// </summary>
        public bool SelectPage(IPage page)
        {
            CurrentSelection = page;
            return true;
        }

        public IPage CurrentSelection { get; private set; }
    }
}
