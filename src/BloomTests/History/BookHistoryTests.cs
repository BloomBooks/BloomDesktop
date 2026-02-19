using System.Linq;
using Bloom.History;
using BloomTemp;
using NUnit.Framework;

namespace BloomTests.History
{
    /// <summary>
    /// A rather minimal test of book history created by AI and focused on the most recent enhancement.
    /// </summary>
    [TestFixture]
    public class BookHistoryTests
    {
        [Test]
        public void RemoveMostRecentEvent_MultipleMatches_RemovesOnlyMostRecentMatch()
        {
            using (
                var folder = new TemporaryFolder(
                    nameof(RemoveMostRecentEvent_MultipleMatches_RemovesOnlyMostRecentMatch)
                )
            )
            {
                var bookId = "book-id";
                var msg = "checkin message";

                BookHistory.AddEvent(
                    folder.FolderPath,
                    "Test",
                    bookId,
                    BookHistoryEventType.CheckIn,
                    msg
                );
                BookHistory.AddEvent(
                    folder.FolderPath,
                    "Test",
                    bookId,
                    BookHistoryEventType.CheckOut,
                    "other"
                );
                BookHistory.AddEvent(
                    folder.FolderPath,
                    "Test",
                    bookId,
                    BookHistoryEventType.CheckIn,
                    msg
                );

                var removed = BookHistory.RemoveMostRecentEvent(
                    folder.FolderPath,
                    bookId,
                    BookHistoryEventType.CheckIn,
                    msg
                );

                Assert.That(removed, Is.True);
                var history = BookHistory.GetHistory(folder.FolderPath);
                Assert.That(
                    history.Count(e => e.Type == BookHistoryEventType.CheckIn && e.Message == msg),
                    Is.EqualTo(1)
                );
                Assert.That(
                    history.Count(e =>
                        e.Type == BookHistoryEventType.CheckOut && e.Message == "other"
                    ),
                    Is.EqualTo(1)
                );
            }
        }

        [Test]
        public void RemoveMostRecentEvent_NoMatch_ReturnsFalseAndKeepsHistory()
        {
            using (
                var folder = new TemporaryFolder(
                    nameof(RemoveMostRecentEvent_NoMatch_ReturnsFalseAndKeepsHistory)
                )
            )
            {
                var bookId = "book-id";
                BookHistory.AddEvent(
                    folder.FolderPath,
                    "Test",
                    bookId,
                    BookHistoryEventType.CheckOut,
                    "other"
                );

                var removed = BookHistory.RemoveMostRecentEvent(
                    folder.FolderPath,
                    bookId,
                    BookHistoryEventType.CheckIn,
                    "missing"
                );

                Assert.That(removed, Is.False);
                var history = BookHistory.GetHistory(folder.FolderPath);
                Assert.That(history.Count, Is.EqualTo(1));
                Assert.That(history[0].Type, Is.EqualTo(BookHistoryEventType.CheckOut));
            }
        }
    }
}
