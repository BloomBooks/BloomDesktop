using System;
using SQLite;

namespace Bloom.History
{
    /// <summary>
    /// This enumeration is used to record the type of event that has occurred in the history of a book.
    /// The numeric values are stored in the database, so don't change them.  If you add a new item, you
    /// MUST add it to the end of this list, and also add its name to the end of the list of names stored
    /// in teamCollection\CollectionHistoryTable.tsx.
    /// </summary>
    /// <remarks>
    /// If you reorder these items, or add a new item anywhere but at the end, you will break the history
    /// reports by showing the wrong event types for all entries created before the change occurred!!!
    /// </remarks>
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
        // and also add them to EventTypeEnumerationIsStable() in History\HistoryEventTests.cs
    }

    /// <summary>
    /// This class represents the common fields of BookHistoryEvent and CollectionBookHistoryEvent.
    /// Any fields added should also be added to BookHistoryEvent.FromCollectionBookHistoryEvent().
    /// </summary>
    public class HistoryEvent
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Column("userid")]
        public string UserId { get; set; }

        [Column(name: "username")]
        public string UserName { get; set; }

        [Column("type")]
        public BookHistoryEventType Type { get; set; }

        [Column("user_message")]
        public string Message { get; set; }

        // I don't fully understand why, but this gets stored in the underlying table in such a way that
        // the DateTimes that come back are Kind=unspecified. From inspecting the binary data, I _think_
        // they are being stored as a bigint representing ticks. This means the underlying table
        // doesn't inherently know whether the DateTime is local or UTC.
        // Prior to March 10, 2022 we were storing local DateTimes (DateTime.Now), and therefore,
        // sorting by retrieved DateTimes did not reliably put the events in true chronological order
        // if they were created in different time zones. We now store the dates as UTC.
        [Column("date")]
        public DateTime When { get; set; }

        [Column("version")]
        public string BloomVersion { get; set; }

        [Indexed]
        [Column("book_id")]
        public string BookId { get; set; }
    }
}
