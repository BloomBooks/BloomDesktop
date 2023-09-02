using System;
using System.Collections.Generic;
using System.IO;
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
		[PrimaryKey][Column("id")]
		public string Id { get; set; }

		// todo: books have many names... could stick them all in here, or make a new table for names, or?
		[Column("name")] public string Name { get; set; }
		[Column("pendingCheckinMessage")] public string PendingCheckinMessage { get; set; }

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
					return events;
				}
			}
		}

		public static void SetPendingCheckinMessage(Book.Book book, string message)
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

		public static string GetPendingCheckinMessage(Book.Book book)
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

		public static void AddEvent(Book.Book book, BookHistoryEventType eventType, string message = "")
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

		private static BookHistoryBook GetOrMakeBookRecord(Book.Book book, SQLiteConnection db)
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
			Bloom.Utils.Patient.Retry(
				() => db = new SQLiteConnection(path));
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
