using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.TeamCollection;
using SIL.Code;
using SQLite;

namespace Bloom.TeamCollection
{

	[Table("books")]
	public class BookHistoryBook
	{
		[PrimaryKey][Column("id")]
		public string Id { get; set; }

		// todo: books have many names... could stick them all in here, or make a new table for names, or?
		[Column("name")] public string Name { get; set; }
		[Column("pendingCheckinMessage")] public string PendingCheckinMessage { get; set; }

	}

	public enum BookHistoryEventType
	{
		CheckIn,
		Created,
		Renamed,
		Uploaded,
		ForcedUnlock,
		ImportSpreadsheet,
		SyncProblem,
		Deleted
		// NB: add them here, too: teamCollection\CollectionHistoryTable.tsx and GetHumanReadableEventType()
	}

	/// <summary>
	/// This class represents the common fields of BookHistoryEvent and CollectionBookHistoryEvent.
	/// Any fields added should also be added to BookHistoryEvent.FromCollectionBookHistoryEvent().
	/// </summary>
	public class HistoryEvent
	{
		[PrimaryKey, AutoIncrement]
		public int Id { get; set; }

		[Column("userid")] public string UserId { get; set; }
		[Column(name: "username")] public string UserName { get; set; }
		[Column("type")] public BookHistoryEventType Type { get; set; }
		[Column("user_message")] public string Message { get; set; }

		// I don't fully understand why, but this gets stored in the underlying table in such a way that
		// the DateTimes that come back are Kind=unspecified. From inspecting the binary data, I _think_
		// they are being stored as a bigint representing ticks. This means the underlying table
		// doesn't inherently know whether the DateTime is local or UTC.
		// Prior to March 10, 2022 we were storing local DateTimes (DateTime.Now), and therefore,
		// sorting by retrieved DateTimes did not reliably put the events in true chronological order
		// if they were created in different time zones. We now store the dates as UTC.
		[Column("date")] public DateTime When { get; set; }
		[Column("version")] public string BloomVersion {get; set; }

		[Indexed]
		[Column("book_id")] public string BookId { get; set; }
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

		public static string GetHumanReadableEventType(BookHistoryEventType type)
		{
			string result;
			switch (type)
			{
				case BookHistoryEventType.CheckIn:
					result = "Check In";
						break;
				case BookHistoryEventType.Created:
					result = "Created";
					break;
				case BookHistoryEventType.Renamed:
					result = "Renamed";
					break;
				case BookHistoryEventType.Uploaded:
					result = "Uploaded";
					break;
				case BookHistoryEventType.ForcedUnlock:
					result = "Forced Unlock";
					break;
				case BookHistoryEventType.ImportSpreadsheet:
					result = "Import from Spreadsheet";
					break;
				case BookHistoryEventType.SyncProblem:
					result = "Sync Problem";
					break;
				case BookHistoryEventType.Deleted:
					result = "Deleted";
					break;
				default:
					throw new ApplicationException("Unknown BookHistoryEventType");
			}
			return result;
		}
	}

	// These are very similar events but stored in the collection history.
	// Title has to be duplicated because in this table we actually have to store it.
	// Don't have any way to have a thumbnail of a book without actually having the
	// book so that is left out. I'm using a different table name because one day
	// we might want a table of events that affect the collection as a whole
	// (like changing settings).
	[Table("book_events")]
	public class CollectionBookHistoryEvent : HistoryEvent
	{
		[Column("title")] public string Title { get; set; }
	}
}

/// <summary>
/// The collection history currently gives us a place to put events that don't belong to
/// any existing book. Currently, these are events about books, but which can't be
/// stored in the book's own history, typically because they involve the deletion of
/// the book. This also gives us a place to put methods that related to the events
/// of the whole collection.
/// </summary>
public class CollectionHistory
{
	public static IEnumerable<BookHistoryEvent> GetAllEvents(BookCollection collection)
	{
		var all = collection.GetBookInfos().Select(bookInfo =>
		{
			if (Directory.Exists(bookInfo.FolderPath))
			{
				return GetBookEvents(bookInfo);
			}
			else
			{
				Debug.Fail($"Trying to get history of folder {bookInfo.FolderPath} but it does not exist");
				// In production, if the book doesn't exist we just don't include any history for it.
				return new List<BookHistoryEvent>();
			}
		});

		// strip out, if there are no events
		var booksWithHistory =  from b in all where b.Any() select b;
		var collectionBookEvents = GetCollectionBookHistory(collection.PathToDirectory);
		return booksWithHistory.SelectMany(e => e).Concat(collectionBookEvents);
	}

	public static void AddBookEvent(string collectionPath, string bookName, string bookId, BookHistoryEventType eventType, string message = "")
	{
		if (SIL.PlatformUtilities.Platform.IsLinux)
			return;     // SQLiteConnection never works on Linux.
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

			NonFatalProblem.Report(ModalIf.None, PassiveIf.All, "Problem writing book history", $"folder={collectionPath}",
				e);
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
	/// <returns></returns>
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
				.Select(ch => BookHistoryEvent.FromCollectionBookHistoryEvent(ch)).ToList();
			db.Close();
			return events;
		}
	}

	private static SQLiteConnection GetConnection(string collectionFolder)
	{
		SQLiteConnection db = null;
		var path = GetDatabasePath(collectionFolder);
		RetryUtility.Retry(
			() => db = new SQLiteConnection(path));
		if (db == null)
			throw new ApplicationException("Could not open history db for" + path);

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

public class BookHistory
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
				return events;
			}
		}
	}

	public static void SetPendingCheckinMessage(Book book, string message)
	{
		if (SIL.PlatformUtilities.Platform.IsLinux)
			return;     // SQLiteConnection never works on Linux.
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

			NonFatalProblem.Report(ModalIf.None, PassiveIf.All, "Problem writing book history", $"folder={book.FolderPath}",
				e);
			// swallow... we don't want to prevent whatever was about to happen.
		}
	}

	public static string GetPendingCheckinMessage(Book book)
	{
		if (SIL.PlatformUtilities.Platform.IsLinux)
			return "";     // SQLiteConnection never works on Linux.
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

			NonFatalProblem.Report(ModalIf.None, PassiveIf.All, "Problem reading book history", $"folder={book.FolderPath}",
				e);
			// swallow... we don't want to prevent whatever was about to happen.
		}

		return "";
	}

	public static void AddEvent(Book book, BookHistoryEventType eventType, string message = "")
	{
		AddEvent(book.FolderPath, book.NameBestForUserDisplay, book.ID, eventType, message);
	}

	public static void AddEvent(string folderPath, string bookName, string bookId,  BookHistoryEventType eventType, string message="")
	{
		if (SIL.PlatformUtilities.Platform.IsLinux)
			return;     // SQLiteConnection never works on Linux.
		try
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
		}
		catch (Exception e)
		{

			NonFatalProblem.Report(ModalIf.None, PassiveIf.All, "Problem writing book history", $"folder={folderPath}",
				 e);
			// swallow... we don't want to prevent whatever was about to happen.
		}
		// Instance should only be null in unit tests
		BloomWebSocketServer.Instance?.SendEvent("bookHistory","eventAdded");
	}

	private static BookHistoryBook GetOrMakeBookRecord(Book book, SQLiteConnection db)
	{
		return GetOrMakeBookRecord(book.NameBestForUserDisplay, book.ID, db);
	}

	private static BookHistoryBook GetOrMakeBookRecord(string bookName, string bookId, SQLiteConnection db)
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

	private static BookHistoryBook GetBookRecord(Book book, SQLiteConnection db)
	{
		return GetBookRecord(book.ID, db);
	}

	private static BookHistoryBook GetBookRecord(string bookId, SQLiteConnection db)
	{
		return db.Table<BookHistoryBook>().FirstOrDefault(b => b.Id == bookId);
	}

	private static SQLiteConnection GetConnection(string folderPath)
	{
		SQLiteConnection db = null;
		var path = GetDatabasePath(folderPath);
		RetryUtility.Retry(
			() => db = new SQLiteConnection(path));
		if (db == null)
			throw new ApplicationException("Could not open history db for" + path);

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



/*
public class BookHistoryDatabaseHandler
{
	private readonly Book _book;
	private readonly string _path;

	//public BookHistoryDatabaseHandler(string bookFolder)
	//{
	//	_path = GetDatabasePath(bookFolder);
	//	using (var db = GetConnection())
	//	{
	//		db.CreateTable<BookHistoryBook>();
	//		db.CreateTable<BookHistoryEvent>();
	//		db.Close();
	//	}
	//}
	public BookHistoryDatabaseHandler(Book book)
	{
		_book = book;
		_path = GetDatabasePath(book.FolderPath);
		using (var db = GetConnection())
		{
			db.CreateTable<BookHistoryBook>();
			db.CreateTable<BookHistoryEvent>();
			db.Close();
		}
	}

	private static string GetDatabasePath(string bookFolder)
	{
		return Path.Combine(bookFolder, "history.db");
	}

	private SQLiteConnection GetConnection()
	{
		SQLiteConnection db=null;
		RetryUtility.Retry(
			() => db = new SQLiteConnection(_path));
		if (db!=null)
			return db;
		throw new ApplicationException("Could not open history db for" + _path);
	}

	public void AddEvent(string message)
	{
		new BookHistoryEvent
		{
			
		};
	}
	private void AddEvent( BookHistoryEvent evt)
	{
		using (var db = GetConnection())
		{
			var bookRecord = db.Table<BookHistoryBook>().FirstOrDefault(b => b.Id == book.ID);
			if (bookRecord == null)
			{
				bookRecord = new BookHistoryBook
				{
					Id = book.ID,
					// TODO: update Name every time because it can change? Add an event if we notice that it changed?
					Name = book.TitleBestForUserDisplay
				};
				db.Insert(bookRecord);
			}

			evt.BookId = book.ID;
			db.Insert(evt);
			db.Close();
		}
	}

	public List<BookHistoryEvent> QueryAllEventsForBook()
	{
		using (var db = GetConnection())
		{
			var events = db.Table<BookHistoryEvent>().ToList();
			db.Close();
			return events;
		}
	}
}
*/
