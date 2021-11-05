using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.TeamCollection;
using SIL.Code;
using SIL.Linq;
using SIL.Reporting;
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
		Uploaded
	}
	[Table("events")]
	public class BookHistoryEvent
	{
		// we don't put this in the database. We always show the current title.
		public string Title;
		public string ThumbnailPath;

		[PrimaryKey, AutoIncrement]
		public int Id { get; set; }

		[Column("userid")] public string UserId { get; set; }
		[Column(name:"username")] public string UserName { get; set; }
		[Column("type")] public BookHistoryEventType Type { get; set; }
		[Column("user_message")] public string Message { get; set; }
		[Column("date")] public DateTime When { get; set; }

		[Indexed]
		[Column("book_id")] public string BookId { get; set; }
	}
}

public class CollectionHistory
{
	public static IEnumerable<BookHistoryEvent> GetAllEvents(BookCollection collection)
	{
		var all = collection.GetBookInfos().Select(bookInfo =>
		{
			if (Directory.Exists(bookInfo.FolderPath))
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
			else
			{
				Debug.Fail($"Trying to get history of folder {bookInfo.FolderPath} but it does not exist");
				// In production, if the book doesn't exist we just don't include any history for it.
				return new List<BookHistoryEvent>();
			}
		});

		// strip out, if there are no events
		var booksWithHistory =  from b in all where b.Any() select b;

		return booksWithHistory.SelectMany(e => e);
	}
}

public class BookHistory
{
	public static List<BookHistoryEvent> GetHistory(BookInfo book)
	{
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

	public static void AddEvent(Book book,  BookHistoryEventType eventType, string message="")
	{
		try
		{
			using (var db = GetConnection(book.FolderPath))
			{
				GetOrMakeBookRecord(book, db);

				var evt = new BookHistoryEvent()
				{
					BookId = book.ID,
					Message = message,
					UserId = TeamCollectionManager.CurrentUser,
					UserName = TeamCollectionManager.CurrentUserFirstName,
					Type = eventType,
					When = DateTime.Now
				};

				db.Insert(evt);
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

	private static BookHistoryBook GetOrMakeBookRecord(Book book, SQLiteConnection db)
	{
		var bookRecord = GetBookRecord(book, db);
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

		return bookRecord;
	}

	private static BookHistoryBook GetBookRecord(Book book, SQLiteConnection db)
	{
		return db.Table<BookHistoryBook>().FirstOrDefault(b => b.Id == book.ID);
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
