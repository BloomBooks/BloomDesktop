using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Publish;
using NUnit.Framework;

namespace BloomTests.Publish
{
    [TestFixture]
    public class PublishHelperTests
    {
        [TestCase("width", "width: 1433.16px; top: -146.703px; left: -706.162px;", 1433.16)]
        [TestCase("top", "width: 1433.16px; top: -146.703px; left: -706.162px;", -146.703)]
        [TestCase("left", "width: 1433.16px; top: -146.703px; left: -706.162px;", -706.162)]
        [TestCase("left", "some silly nonsence", 0)]
        [TestCase("left", "width: 20", 0)]
        public void GetNumberFromPx(string label, string input, double expected)
        {
            Assert.That(
                Math.Abs(expected - PublishHelper.GetNumberFromPx(label, input)),
                Is.LessThan(0.001)
            );
        }
    }
}
