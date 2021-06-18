using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Book;
using Bloom.TeamCollection;
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

	}

	[Table("events")]
	public class BookHistoryEvent
	{
		[PrimaryKey, AutoIncrement]
		public int Id { get; set; }

		[Column("userid")] public string UserId { get; set; }

		[Column("user_message")] public string Message { get; set; }
		[Column("date")] public DateTime When { get; set; }

		[Indexed]
		[Column("book_id")] public string BookId { get; set; }
	}
}

public class BookHistoryDatabaseHandler
{
	public BookHistoryDatabaseHandler(string bookFolder)
	{
		var db = new SQLiteConnection(GetDatabasePath(bookFolder));
		db.CreateTable<BookHistoryBook>();
		db.CreateTable<BookHistoryEvent>();
		db.Close();
	}

	private static string GetDatabasePath(string bookFolder)
	{
		return Path.Combine(bookFolder, "history.db");
	}

	public void AddEvent(Book book, BookHistoryEvent evt)
	{
		using (var db = new SQLiteConnection(GetDatabasePath(book.FolderPath)))
		{
			var bookRecord = db.Table<BookHistoryBook>().FirstOrDefault(b => b.Id == book.ID);
			if (bookRecord == null)
			{
				bookRecord = new BookHistoryBook
				{
					Id = book.ID,
					Name = book.TitleBestForUserDisplay
				};
				db.Insert(bookRecord);
			}

			evt.BookId = book.ID;
			db.Insert(evt);
			db.Close();
		}
	}

	public List<BookHistoryEvent> QueryAllEventsForBook(Book book)
	{
		using (var db = new SQLiteConnection(GetDatabasePath(book.FolderPath)))
		{
			var events= db.Table<BookHistoryEvent>().ToList();
			db.Close();
			return events;
		}
	}
}
