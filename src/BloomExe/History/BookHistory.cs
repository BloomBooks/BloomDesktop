using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.TeamCollection;
using SIL.Code;
using SQLite;

namespace Bloom.History
{
    [Table("books")]
    public class BookHistoryBook
    {
        [PrimaryKey]
        [Column("id")]
        public string Id { get; set; }

        // todo: books have many names... could stick them all in here, or make a new table for names, or?
        [Column("name")]
        public string Name { get; set; }

        [Column("pendingCheckinMessage")]
        public string PendingCheckinMessage { get; set; }
    }

    [Table("events")]
    public class BookHistoryEvent : HistoryEvent
    {
        // we don't put this in the book database. We always show the current title.
        public string Title;
        public string ThumbnailPath;

        public static BookHistoryEvent FromCollectionBookHistoryEvent(CollectionBookHistoryEvent ch)
        {
            return new BookHistoryEvent()
            {
                Title = ch.Title,
                BookId = ch.BookId,
                Message = ch.Message,
                UserId = ch.UserId,
                UserName = ch.UserName,
                Type = ch.Type,
                When = ch.When,
                BloomVersion = ch.BloomVersion
            };
        }
    }

    public static class BookHistory
    {
        public static List<BookHistoryEvent> GetHistory(BookInfo book)
        {
            if (SIL.PlatformUtilities.Platform.IsLinux)
            {
                // SQLiteConnection never works on Linux.
                return new List<BookHistoryEvent>();
            }
            else
            {
                using (var db = GetConnection(book.FolderPath))
                {
                    var events = db.Table<BookHistoryEvent>().ToList();
                    db.Close();
                    FixEventTypesForEnumerationChange(events);
                    return events;
                }
            }
        }

        /// <summary>
        /// The BookHistoryEventType enumeration was changed in Bloom 5.6 by adding a new value at
        /// the beginning instead of the end.  This broke all of the existing history event types.
        /// This method attempts to repair that breakage while reading the data from the history
        /// database.
        /// </summary>
        /// <remarks>See BL-13140.</remarks>
        public static void FixEventTypesForEnumerationChange(List<BookHistoryEvent> events)
        {
            events.ForEach(ev =>
            {
                if (
                    String.IsNullOrEmpty(ev.BloomVersion)
                    || Regex.IsMatch(
                        ev.BloomVersion,
                        "^5\\.[0-5]\\.",
                        RegexOptions.CultureInvariant
                    )
                    ||
                    // The new BookHistoryEventType value was introduced in BloomAlpha 5.6.1055 before it went beta.
                    // So we need to fix the event types for all versions of 5.6 before 5.6.1055.  Note that version
                    // numbers for the alpha start at 5.6.1000, version numbers for the beta start at 5.6.100, and
                    // version numbers for the release start at 5.6.1. So we need to verify the alpha range overall
                    // before we can make a simple string comparison to check for alpha prior to 5.6.1055.
                    (
                        Regex.IsMatch(
                            ev.BloomVersion,
                            "^5\\.6\\.1[0-9][0-9][0-9]",
                            RegexOptions.CultureInvariant
                        )
                        && ev.BloomVersion.CompareTo("5.6.1055") < 0
                    )
                )
                {
                    switch (ev.Type)
                    {
                        case BookHistoryEventType.CheckOut:
                            ev.Type = BookHistoryEventType.CheckIn;
                            break;
                        case BookHistoryEventType.CheckIn:
                            ev.Type = BookHistoryEventType.Created;
                            break;
                        case BookHistoryEventType.Created:
                            ev.Type = BookHistoryEventType.Renamed;
                            break;
                        case BookHistoryEventType.Renamed:
                            ev.Type = BookHistoryEventType.Uploaded;
                            break;
                        case BookHistoryEventType.Uploaded:
                            ev.Type = BookHistoryEventType.ForcedUnlock;
                            break;
                        case BookHistoryEventType.ForcedUnlock:
                            ev.Type = BookHistoryEventType.ImportSpreadsheet;
                            break;
                        case BookHistoryEventType.ImportSpreadsheet:
                            ev.Type = BookHistoryEventType.SyncProblem;
                            break;
                        case BookHistoryEventType.SyncProblem:
                            ev.Type = BookHistoryEventType.Deleted;
                            break;
                    }
                }
            });
        }

        public static void SetPendingCheckinMessage(Book.Book book, string message)
        {
            if (SIL.PlatformUtilities.Platform.IsLinux)
                return; // SQLiteConnection never works on Linux.
            try
            {
                using (var db = GetConnection(book.FolderPath))
                {
                    var bookRecord = GetOrMakeBookRecord(book, db);
                    bookRecord.PendingCheckinMessage = message;
                    db.Update(bookRecord);
                    db.Close();
                }
            }
            catch (Exception e)
            {
                NonFatalProblem.Report(
                    ModalIf.None,
                    PassiveIf.All,
                    "Problem writing book history",
                    $"folder={book.FolderPath}",
                    e
                );
                // swallow... we don't want to prevent whatever was about to happen.
            }
        }

        public static string GetPendingCheckinMessage(Book.Book book)
        {
            if (SIL.PlatformUtilities.Platform.IsLinux)
                return ""; // SQLiteConnection never works on Linux.
            try
            {
                using (var db = GetConnection(book.FolderPath))
                {
                    var bookRecord = GetBookRecord(book, db);
                    return bookRecord?.PendingCheckinMessage ?? "";
                }
            }
            catch (Exception e)
            {
                NonFatalProblem.Report(
                    ModalIf.None,
                    PassiveIf.All,
                    "Problem reading book history",
                    $"folder={book.FolderPath}",
                    e
                );
                // swallow... we don't want to prevent whatever was about to happen.
            }

            return "";
        }

        public static void AddEvent(
            Book.Book book,
            BookHistoryEventType eventType,
            string message = ""
        )
        {
            AddEvent(book.FolderPath, book.NameBestForUserDisplay, book.ID, eventType, message);
        }

        public static void AddEvent(
            Book.BookInfo bookInfo,
            BookHistoryEventType eventType,
            string message = ""
        )
        {
            AddEvent(bookInfo.FolderPath, bookInfo.FolderName, bookInfo.Id, eventType, message);
        }

        public static void AddEvent(
            string folderPath,
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
                var sQLiteExceptionSet = new HashSet<Type>() { typeof(SQLiteException) };

                // Note: it's really hard to know from the sdk we have at the moment if a sqllite connection is
                // opened read only because some other process has it open. So we do the retry around the whole
                // operation instead of just the opening of the connection like you'd expect.
                RetryUtility.Retry(
                    () =>
                    {
                        using (var db = GetConnection(folderPath))
                        {
                            GetOrMakeBookRecord(bookName, bookId, db);

                            var evt = new BookHistoryEvent()
                            {
                                BookId = bookId,
                                Message = message,
                                UserId = TeamCollectionManager.CurrentUser,
                                UserName = TeamCollectionManager.CurrentUserFirstName,
                                Type = eventType,
                                // Be sure to use UTC, otherwise, order will not be preserved properly.
                                When = DateTime.UtcNow,
                                BloomVersion = Application.ProductVersion
                            };

                            db.Insert(evt);
                            db.Close();
                        }
                    },
                    exceptionTypesToRetry: sQLiteExceptionSet,
                    memo: "opening history db for writing a book record"
                );
            }
            catch (Exception e)
            {
                NonFatalProblem.Report(
                    ModalIf.None,
                    PassiveIf.All,
                    "Problem writing book history",
                    $"folder={folderPath}",
                    e
                );
                // swallow... we don't want to prevent whatever was about to happen.
            }
            // Instance should only be null in unit tests
            BloomWebSocketServer.Instance?.SendEvent("bookHistory", "eventAdded");
        }

        private static BookHistoryBook GetOrMakeBookRecord(Book.Book book, SQLiteConnection db)
        {
            return GetOrMakeBookRecord(book.NameBestForUserDisplay, book.ID, db);
        }

        private static BookHistoryBook GetOrMakeBookRecord(
            string bookName,
            string bookId,
            SQLiteConnection db
        )
        {
            var bookRecord = GetBookRecord(bookId, db);
            if (bookRecord == null)
            {
                bookRecord = new BookHistoryBook
                {
                    Id = bookId,
                    // TODO: update Name every time because it can change? Add an event if we notice that it changed?
                    Name = bookName
                };
                db.Insert(bookRecord);
            }

            return bookRecord;
        }

        private static BookHistoryBook GetBookRecord(Book.Book book, SQLiteConnection db)
        {
            return GetBookRecord(book.ID, db);
        }

        private static BookHistoryBook GetBookRecord(string bookId, SQLiteConnection db)
        {
            return db.Table<BookHistoryBook>().FirstOrDefault(b => b.Id == bookId);
        }

        private static SQLiteConnection GetConnection(string bookFolderPath)
        {
            SQLiteConnection db = null;
            var path = GetDatabasePath(bookFolderPath);
            RetryUtility.Retry(() => db = new SQLiteConnection(path));
            if (db == null)
                throw new ApplicationException("Could not open book history db for" + path);

            // For now, we're keeping things simple by just not assuming *anything*, even that the tables are there.
            // If we find that something is slow or sqllite dislikes this approach, we can do something more complicated.

            db.CreateTable<BookHistoryBook>();
            db.CreateTable<BookHistoryEvent>();
            return db;
        }

        private static string GetDatabasePath(string bookFolder)
        {
            return Path.Combine(bookFolder, "history.db");
        }
    }
}
