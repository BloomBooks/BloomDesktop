using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.TeamCollection;
using Bloom.Utils;

namespace Bloom
{
    public interface IEvent { } //hack for our autofact registration because of the generic I don't know how to select by actual event<>

    public class Event<TPayload> : IEvent
    {
        private readonly string _nameForLogging;

        protected enum LoggingLevel
        {
            Minor,
            Major
        };

        private LoggingLevel _loggingLevel;

        protected Event(string nameForLogging, LoggingLevel loggingLevel)
        {
            _nameForLogging = nameForLogging;
            _loggingLevel = loggingLevel;
        }

        private readonly List<Action<TPayload>> _subscribers = new List<Action<TPayload>>();

        public void Subscribe(Action<TPayload> action)
        {
            if (!_subscribers.Contains(action))
            {
                _subscribers.Add(action);
            }
        }

        public virtual void Raise(TPayload descriptor)
        {
            SIL.Reporting.Logger.WriteMinorEvent("Event: " + _nameForLogging);
            using (
                PerformanceMeasurement.Global?.MeasureMaybe(
                    _loggingLevel == LoggingLevel.Major,
                    _nameForLogging
                )
            )
            {
                foreach (Action<TPayload> subscriber in _subscribers)
                {
                    ((Action<TPayload>)subscriber)(descriptor);
                }
            }
        }

        public bool HasSubscribers
        {
            get { return _subscribers.Count > 0; }
        }
    }

    public class TabChangedDetails
    {
        public Control From;
        public Control To;

        // Must be executed by the subscriber when it is safe to do so, possibly after returning
        // from the event handler.
        // This is a bit of a kludge. It works partly because there is currently only one subscriber,
        // so there is no ambiguity about who should do this, or how we know when all the subscribers
        // are done. If we ever have more than one subscriber, we'll need to do something more sophisticated.
        public Action PostponedWork;
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
    /// Gives the first control in the tab
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

    public class CollectionClosingArgs
    {
        // May be executed by one subscriber when it is safe to do so, typically in response to an event
        // after returning from the event handler. A subscriber that wants to do this should set Delayed to true.
        // This is a bit of a kludge. It works partly because there is currently only one subscriber
        // that needs to postpone work until after the event handler has returned,
        // so there is no ambiguity about who should do this. The tricky thing was to make sure
        // all subscribers get to do their thing when we don't need to delay (when not in Edit tab).
        // Hence Delayed and the override of Raise.
        public Action PostponedWork;

        // If a subscriber sets this to true, it takes responsibility for calling PostponedWork
        // at some later time. If no one does, Raise() will call it after all subscribers have been called.
        // At most one subscriber should set this to true. Review: should we enforce this?
        public bool Delayed;
    }

    /// <summary>
    /// called when the user is quiting or changing to another collection
    /// </summary>
    public class CollectionClosing : Event<CollectionClosingArgs>
    {
        public CollectionClosing()
            : base("CollectionClosing", LoggingLevel.Major) { }

        public override void Raise(CollectionClosingArgs descriptor)
        {
            base.Raise(descriptor);
            if (!descriptor.Delayed)
            {
                descriptor.PostponedWork?.Invoke();
            }
        }
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
            JustRedisplay
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
