using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.TeamCollection;
using SIL.Code;
using SQLite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Bloom.History
{
    // These are very similar events as those in BookHistory.cs but stored in the collection history.
    // Title has to be duplicated because in this table we actually have to store it.
    // We don't have any way to have a thumbnail of a book without actually having the
    // book so that is left out. I'm using a different table name because one day
    // we might want a table of events that affect the collection as a whole (like changing settings).
    [Table("book_events")]
    public class CollectionBookHistoryEvent : HistoryEvent
    {
        [Column("title")]
        public string Title { get; set; }
    }

    /// <summary>
    /// The collection history gives us a place to put events that don't belong to any existing book.
    /// Currently, these are events about books, but which can't be stored in the book's own history,
    /// typically because they involve the deletion of the book.
    /// This also gives us a place to put methods that related to the events of the whole collection.
    /// </summary>
    public static class CollectionHistory
    {
        public static IEnumerable<BookHistoryEvent> GetAllEvents(BookCollection collection)
        {
            var all = collection
                .GetBookInfos()
                .Select(bookInfo =>
                {
                    if (Directory.Exists(bookInfo.FolderPath))
                    {
                        return GetBookEvents(bookInfo);
                    }
                    else
                    {
                        Debug.Fail(
                            $"Trying to get history of folder {bookInfo.FolderPath} but it does not exist"
                        );
                        // In production, if the book doesn't exist we just don't include any history for it.
                        return new List<BookHistoryEvent>();
                    }
                });

            // strip out, if there are no events
            var booksWithHistory = from b in all where b.Any() select b;
            var collectionBookEvents = GetCollectionBookHistory(collection.PathToDirectory);
            return booksWithHistory.SelectMany(e => e).Concat(collectionBookEvents);
        }

        public static void AddBookEvent(
            string collectionPath,
            string bookName,
            string bookId,
            BookHistoryEventType eventType,
            string message = ""
        )
        {
            if (SIL.PlatformUtilities.Platform.IsLinux)
                return; // SQLiteConnection never works on Linux.
            try
            {
                using (var db = GetConnection(collectionPath))
                {
                    var evt = new CollectionBookHistoryEvent()
                    {
                        Title = bookName,
                        BookId = bookId,
                        Message = message,
                        UserId = TeamCollectionManager.CurrentUser,
                        UserName = TeamCollectionManager.CurrentUserFirstName,
                        Type = eventType,
                        BloomVersion = Application.ProductVersion,
                        // Be sure to use UTC, otherwise, order will not be preserved properly.
                        When = DateTime.UtcNow
                    };

                    db.Insert(evt);
                    db.Close();
                }
            }
            catch (Exception e)
            {
                NonFatalProblem.Report(
                    ModalIf.None,
                    PassiveIf.All,
                    "Problem writing book history",
                    $"folder={collectionPath}",
                    e
                );
                // swallow... we don't want to prevent whatever was about to happen.
            }
            // Instance should only be null in unit tests
            BloomWebSocketServer.Instance?.SendEvent("bookHistory", "eventAdded");
        }

        public static List<BookHistoryEvent> GetBookEvents(BookInfo bookInfo)
        {
            var events = BookHistory.GetHistory(bookInfo);
            // add in the title, which isn't in the database (this could done in a way that involves less duplication)
            events.ForEach(e =>
            {
                e.Title = bookInfo.Title;
                e.ThumbnailPath = Path.Combine(bookInfo.FolderPath, "thumbnail.png").ToLocalhost();
            });
            return events;
        }

        /// <summary>
        /// Although these events are stored as CollectionBookHistoryEvents, it is convenient to
        /// retrieve them as BookHistoryEvents, since the main client wants to concatenate the two
        /// lists. (We can't actually store them that way, because the two classes have different
        /// directives about storing Title in the database.)
        /// </summary>
        /// <param name="collectionFolder"></param>
        public static List<BookHistoryEvent> GetCollectionBookHistory(string collectionFolder)
        {
            if (SIL.PlatformUtilities.Platform.IsLinux)
            {
                // SQLiteConnection never works on Linux.
                return new List<BookHistoryEvent>();
            }

            using (var db = GetConnection(collectionFolder))
            {
                var events = db.Table<CollectionBookHistoryEvent>()
                    .Select(ch => BookHistoryEvent.FromCollectionBookHistoryEvent(ch))
                    .ToList();
                db.Close();
                BookHistory.FixEventTypesForEnumerationChange(events);
                return events;
            }
        }

        private static SQLiteConnection GetConnection(string collectionFolder)
        {
            SQLiteConnection db = null;
            var path = GetDatabasePath(collectionFolder);
            RetryUtility.Retry(() => db = new SQLiteConnection(path));
            if (db == null)
                throw new ApplicationException("Could not open collection history db for" + path);

            // For now, we're keeping things simple by just not assuming *anything*, even that the tables are there.
            // If we find that something is slow or sqllite dislikes this approach, we can do something more complicated.

            db.CreateTable<CollectionBookHistoryEvent>();
            return db;
        }

        private static string GetDatabasePath(string collectionFolder)
        {
            return Path.Combine(collectionFolder, "history.db");
        }
    }
}
