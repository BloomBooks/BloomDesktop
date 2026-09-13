using Bloom.Book;
using NUnit.Framework;

namespace BloomTests.Book
{
    /// <summary>
    /// What Bloom would report about the use of flow text. The two pieces that decide it are
    /// tested here; the sending itself is not, in keeping with how the rest of Bloom treats
    /// analytics.
    /// </summary>
    [TestFixture]
    public class FlowTextAnalyticsTests
    {
        [SetUp]
        public void Setup()
        {
            FlowTextAnalytics.ForgetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            FlowTextAnalytics.ForgetForTests();
        }

        private static HtmlDom MakeBookDom(string pages)
        {
            return new HtmlDom($"<html><head></head><body>{pages}</body></html>");
        }

        private static string Page(string id, string content)
        {
            return $"<div class='bloom-page numberedPage' id='{id}'>{content}</div>";
        }

        private static string Group(string chainId)
        {
            var chain = chainId == null ? "" : $" {HtmlDom.kFlowChainAttrName}='{chainId}'";
            return $"<div class='bloom-translationGroup'{chain}>"
                + "<div class='bloom-editable normal-style bloom-visibility-code-on' lang='en'>"
                + "<p>some text</p></div></div>";
        }

        [Test]
        public void LongestRunInPages_WithNoChainAnywhere_IsZero()
        {
            var dom = MakeBookDom(Page("p1", Group(null)) + Page("p2", Group(null)));

            Assert.That(FlowTextAnalytics.LongestRunInPages(dom), Is.EqualTo(0));
        }

        [Test]
        public void LongestRunInPages_WithOneChainOverThreePages_IsThree()
        {
            var dom = MakeBookDom(
                Page("p1", Group("chain")) + Page("p2", Group("chain")) + Page("p3", Group("chain"))
            );

            Assert.That(FlowTextAnalytics.LongestRunInPages(dom), Is.EqualTo(3));
        }

        [Test]
        public void LongestRunInPages_WithTwoChains_IsTheLongerOfThem()
        {
            var dom = MakeBookDom(
                Page("p1", Group("short") + Group("long"))
                    + Page("p2", Group("short"))
                    + Page("p3", Group("long"))
                    + Page("p4", Group("long"))
                    + Page("p5", Group("long"))
                    + Page("p6", Group("long"))
            );

            Assert.That(
                FlowTextAnalytics.LongestRunInPages(dom),
                Is.EqualTo(5),
                "The book's use of the feature is the longest run in it, not the sum."
            );
        }

        [Test]
        public void LongestRunInPages_WithTwoBoxesOfOneChainOnOnePage_IsOne()
        {
            // Text flowing from one box into another on the same page is real use of the
            // feature, and worth telling apart from no use at all.
            var dom = MakeBookDom(Page("p1", Group("chain") + Group("chain")));

            Assert.That(FlowTextAnalytics.LongestRunInPages(dom), Is.EqualTo(1));
        }

        [Test]
        public void LongestRunInPages_WithALoneBoxCarryingAChainId_IsZero()
        {
            // One box is not a chain: it has nowhere to send its text. A stray attribute left on
            // a single box must not read as a one-page use of the feature.
            var dom = MakeBookDom(Page("p1", Group("chain")) + Page("p2", Group(null)));
            Assert.That(
                FlowTextChains.GetChainGroups(dom, "chain").Count,
                Is.EqualTo(1),
                "Sanity check: the book really does have just the one group in this chain."
            );

            Assert.That(FlowTextAnalytics.LongestRunInPages(dom), Is.EqualTo(0));
        }

        [Test]
        public void ShouldReport_TheFirstTimeABookReachesALength_IsTrue()
        {
            Assert.That(FlowTextAnalytics.ShouldReport("book1", 3), Is.True);
        }

        [Test]
        public void ShouldReport_ForALengthAlreadyReported_IsFalse()
        {
            Assert.That(
                FlowTextAnalytics.ShouldReport("book1", 3),
                Is.True,
                "Sanity check: the first report is the one that gets through."
            );

            Assert.That(
                FlowTextAnalytics.ShouldReport("book1", 3),
                Is.False,
                "A refit that leaves the run the same length is not news."
            );
        }

        [Test]
        public void ShouldReport_WhenTheRunGrows_IsTrueAgain()
        {
            FlowTextAnalytics.ShouldReport("book1", 3);

            Assert.That(FlowTextAnalytics.ShouldReport("book1", 5), Is.True);
        }

        [Test]
        public void ShouldReport_WhenTheRunShrinks_IsFalse()
        {
            FlowTextAnalytics.ShouldReport("book1", 5);

            Assert.That(
                FlowTextAnalytics.ShouldReport("book1", 4),
                Is.False,
                "The figure worth having is the longest the run ever reached."
            );
        }

        [Test]
        public void ShouldReport_ForNoRunAtAll_IsFalse()
        {
            Assert.That(
                FlowTextAnalytics.ShouldReport("book1", 0),
                Is.False,
                "A book that does not use the feature has nothing to report."
            );
        }

        [Test]
        public void ShouldReport_CountsEachBookOnItsOwn()
        {
            FlowTextAnalytics.ShouldReport("book1", 5);

            Assert.That(
                FlowTextAnalytics.ShouldReport("book2", 3),
                Is.True,
                "What one book reached says nothing about another."
            );
        }
    }
}
