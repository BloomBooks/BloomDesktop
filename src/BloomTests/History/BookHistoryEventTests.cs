using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.History;
using NUnit.Framework;

namespace BloomTests.History
{
    public class BookHistoryEventTests
    {
        [Test]
        public void ToString_YieldsReasonableResult()
        {
            var history = new BookHistoryEvent();
            history.Title = "checkin";
            history.BookId = "12345";

            var output = history.ToString();

            Assert.That(output, Does.Contain("Title: checkin"));
            Assert.That(output, Does.Contain("BookId: 12345"));
        }
    }
}
