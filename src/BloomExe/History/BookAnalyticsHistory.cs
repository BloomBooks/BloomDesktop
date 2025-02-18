using System;
using SQLite;

namespace Bloom.History
{
    [StoreAsText] // without this, the enum is stored as an integer and subject to breaking if we change the order of the enum values
    public enum BookAnalyticsEventName
    {
        EditingStarted,
        EdittingPageEnded, // or timed out

        // the problem with these two is that if you go have lunch, then they would be totally thrown off.
        EntredEditTab,
        ExitedEditTab
    }

    // This records minor events that could be used in studies, for example to see how helpful
    // an ai translation was.
    [Table("analytics")]
    public class AnalyticsEvent
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Column("user_id")]
        public string UserId { get; set; }

        [Column(name: "user_name")]
        public string UserName { get; set; }

        [Column("event_name")]
        public BookAnalyticsEventName EventName
        {
            get;
            set;
        }

        [Column("date_time")]
        public DateTime When { get; set; }

        [Indexed]
        [Column("book_id")]
        public string BookId { get; set; }

    }
}
