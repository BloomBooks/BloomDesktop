using System;
using SQLite;


namespace Bloom.History
{
	public enum BookHistoryEventType
	{
		CheckOut,
		CheckIn,
		Created,
		Renamed,
		Uploaded,
		ForcedUnlock,
		ImportSpreadsheet,
		SyncProblem,
		Deleted
		// NB: add them here, too: teamCollection\CollectionHistoryTable.tsx
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
}
