using Bloom.History;
using NUnit.Framework;

namespace BloomTests.History
{
    [TestFixture]
    public class HistoryEventTests
    {
        [Test]
        public void EventTypeEnumerationIsStable()
        {
            Assert.That((int)BookHistoryEventType.CheckOut, Is.EqualTo(0));
            Assert.That((int)BookHistoryEventType.CheckIn, Is.EqualTo(1));
            Assert.That((int)BookHistoryEventType.Created, Is.EqualTo(2));
            Assert.That((int)BookHistoryEventType.Renamed, Is.EqualTo(3));
            Assert.That((int)BookHistoryEventType.Uploaded, Is.EqualTo(4));
            Assert.That((int)BookHistoryEventType.ForcedUnlock, Is.EqualTo(5));
            Assert.That((int)BookHistoryEventType.ImportSpreadsheet, Is.EqualTo(6));
            Assert.That((int)BookHistoryEventType.SyncProblem, Is.EqualTo(7));
            Assert.That((int)BookHistoryEventType.Deleted, Is.EqualTo(8));
        }
    }
}
