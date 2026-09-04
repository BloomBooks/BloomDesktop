using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.TeamCollection;
using Bloom.Utils;
using Bloom.Workspace;

namespace Bloom
{
    public interface IEvent { } //hack for our autofact registration because of the generic I don't know how to select by actual event<>

    public class Event<TPayload> : IEvent
    {
        private readonly string _nameForLogging;

        protected enum LoggingLevel
        {
            Minor,
            Major,
        };

        private LoggingLevel _loggingLevel;

        protected Event(string nameForLogging, LoggingLevel loggingLevel)
        {
            _nameForLogging = nameForLogging;
            _loggingLevel = loggingLevel;
        }

        private readonly List<Action<TPayload>> _subscribers = new List<Action<TPayload>>();
        private readonly object _subscriberLock = new object();

        public void Subscribe(Action<TPayload> action)
        {
            lock (_subscriberLock)
            {
                if (!_subscribers.Contains(action))
                {
                    _subscribers.Add(action);
                }
            }
        }

        public void Unsubscribe(Action<TPayload> action)
        {
            lock (_subscriberLock)
            {
                _subscribers.Remove(action);
            }
        }

        public virtual void Raise(TPayload descriptor)
        {
            Action<TPayload>[] subscribers;
            lock (_subscriberLock)
            {
                subscribers = _subscribers.ToArray();
            }

            SIL.Reporting.Logger.WriteMinorEvent("Event: " + _nameForLogging);
            using (
                PerformanceMeasurement.Global?.MeasureMaybe(
                    _loggingLevel == LoggingLevel.Major,
                    _nameForLogging
                )
            )
            {
                foreach (Action<TPayload> subscriber in subscribers)
                {
                    ((Action<TPayload>)subscriber)(descriptor);
                }
            }
        }

        public bool HasSubscribers
        {
            get
            {
                lock (_subscriberLock)
                {
                    return _subscribers.Count > 0;
                }
            }
        }
    }

    public class TabChangedDetails
    {
        public WorkspaceTab? FromTab;
        public WorkspaceTab? ToTab;

        // How a subscriber hands the tab change back to us: it calls CompleteTheChange, either
        // before returning if it had nothing to do first, or once it has saved.
        //
        // There used to be a second action, StartTheChangeOver, for a subscriber that could neither
        // proceed nor finish because it was waiting on a save begun by something else -- an earlier
        // click on a tab whose save was still out with the browser (BL-16766). Saving no longer
        // waits for anything, so a subscriber is never in that position, and the case is gone.
        //
        // This works partly because there is currently only one subscriber, so there is no ambiguity
        // about who should call it, or about how we know all the subscribers are done. If we ever
        // have more than one, we'll need something more sophisticated.

        // Actually switches the tab: everything WorkspaceView.ChangeTab held back until a
        // subscriber said it was safe. Call this EXACTLY ONCE — it raises the tab-changed event
        // and records the new tab as current.
        public Action CompleteTheChange;
    }

    /// <summary>
    /// called before the actual change
    /// </summary>
    public class SelectedTabAboutToChangeEvent : Event<TabChangedDetails>
    {
        public SelectedTabAboutToChangeEvent()
            : base("SelectedTabAboutToChangeEvent", LoggingLevel.Minor) { }
    }

    /// <summary>
    /// Indicates tab change completion
    /// </summary>
    public class SelectedTabChangedEvent : Event<TabChangedDetails>
    {
        public SelectedTabChangedEvent()
            : base("SelectedTabChangedEvent", LoggingLevel.Major) { }
    }

    public class CreateFromSourceBookCommand : Event<Book.Book>
    {
        public CreateFromSourceBookCommand()
            : base("CreateFromSourceBookCommand", LoggingLevel.Major) { }
    }

    /// <summary>
    /// Nothing to say beyond "the collection is closing". This used to carry a PostponedWork /
    /// Delayed / FailureAction protocol so that a subscriber could say "I will finish this later,
    /// you carry on" — its own comment called it a bit of a kludge — and it existed for exactly
    /// one subscriber, EditingModel, because saving the page being edited meant asking the browser
    /// and waiting. That save is synchronous now (see PageSnapshot), so subscribers simply do their
    /// work and return.
    /// </summary>
    public class CollectionClosingArgs { }

    /// <summary>
    /// called when the user is quiting or changing to another collection
    /// </summary>
    public class CollectionClosing : Event<CollectionClosingArgs>
    {
        public CollectionClosing()
            : base("CollectionClosing", LoggingLevel.Major) { }
    }

    public class EditBookCommand : Event<Book.Book>
    {
        public EditBookCommand()
            : base("EditBookCommand", LoggingLevel.Major) { }
    }

    //	public class BookCollectionChangedEvent : Event<BookCollection>
    //	{ }

    // descriptor is true if page list needs to be fully regenerated,
    // e.g., because style definitions changed.
    public class PageListChangedEvent : Event<Boolean>
    {
        public PageListChangedEvent()
            : base("PageListChangedEvent", LoggingLevel.Minor) { }
    }

    /// <summary>
    /// This is used to purge the BloomServer cache, so solve the problem of "My Book/image3" (for example)
    /// leading to a picture from the previous book we worked on, back when *it* was named simple "My Book"
    /// The pair here is from, to paths.
    /// </summary>
    public class BookRenamedEvent : Event<KeyValuePair<string, string>>
    {
        public BookRenamedEvent()
            : base("BookRenamedEvent", LoggingLevel.Major) { }
    }

    public class BookDownloadStartingEvent : Event<object>
    {
        public BookDownloadStartingEvent()
            : base("BookDownloadStartingEvent", LoggingLevel.Major) { }
    }

    /// <summary>
    /// ANything displaying the book should re-load it.
    /// </summary>
    public class BookRefreshEvent : Event<Book.Book>
    {
        public BookRefreshEvent()
            : base("BookRefreshEvent", LoggingLevel.Minor) { }
    }

    /// <summary>
    /// Accessibility Checker uses this... not exactly semantic, but it does give us the hook at the right time
    /// </summary>
    public class BookSavedEvent : Event<Book.Book>
    {
        public BookSavedEvent()
            : base("BookSavedEvent", LoggingLevel.Minor) { }
    }

    /// <summary>
    /// Anything displaying a book should re-load it the current page
    /// </summary>
    public class PageRefreshEvent : Event<PageRefreshEvent.SaveBehavior>
    {
        public enum SaveBehavior
        {
            SaveBeforeRefresh,
            SaveBeforeRefreshFullSave,
            JustRedisplay,
        }

        public PageRefreshEvent()
            : base("PageRefreshEvent", LoggingLevel.Minor) { }
    }

    public class RelocatePageInfo
    {
        public IPage Page;
        public int IndexOfPageAfterMove;
        public bool Cancel;

        public RelocatePageInfo(IPage page, int indexOfPageAfterMove)
        {
            Page = page;
            IndexOfPageAfterMove = indexOfPageAfterMove;
        }
    }

    public class RelocatePageEvent : Event<RelocatePageInfo>
    {
        public RelocatePageEvent()
            : base("RelocatePageEvent", LoggingLevel.Minor) { }
    }

    /// <summary>
    /// It's tricky to change the collection folder while a book is open,
    /// so we just queue it and have the project do the rename when we close/reopen
    /// </summary>
    public class QueueRenameOfCollection : Event<string>
    {
        public QueueRenameOfCollection()
            : base("QueueRenameOfCollection", LoggingLevel.Major) { }
    }

    /// <summary>
    /// fired when its possible that string should update from the localization manager
    /// </summary>
    public class LocalizationChangedEvent : Event<object>
    {
        public LocalizationChangedEvent()
            : base("LocalizationChangedEvent", LoggingLevel.Major) { }
    }

    public class ControlKeyEvent : Event<object>
    {
        public readonly Keys Keys;

        public ControlKeyEvent()
            : base("ControlKeyEvent", LoggingLevel.Minor) { }
    }

    // An event that signals that the status of a book in a Team Collection has changed.
    // This could be that it has been checked in or out (here or elsewhere), or some
    // other remote change like a modification to the book itself (checksum changed).
    public class BookStatusChangeEvent : Event<BookStatusChangeEventArgs>
    {
        public BookStatusChangeEvent()
            : base("TeamCollectionBookStatusChange", LoggingLevel.Minor) { }
    }
}
