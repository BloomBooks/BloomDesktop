using System.Collections.Generic;
using System.Linq;
using Bloom.Book;
using Bloom.SafeXml;
using NUnit.Framework;

namespace BloomTests.Book
{
    /// <summary>
    /// Making the pages the rest of a run of text needs, without a browser and without a book on
    /// disk.
    ///
    /// Two things only Bloom itself can do are given to the code under test as delegates, and
    /// each test writes its own: adding a page, which here appends a page to the DOM, and the fit
    /// of one box, which here divides the text by a rule of the test's own instead of laying
    /// anything out. What is under test is the loop between them — how many pages get made, what
    /// each one keeps, which chain they join, and that not a word of the run is lost on the way.
    /// </summary>
    [TestFixture]
    public class FlowTextCreatePagesTests
    {
        private static readonly string kMarker =
            $"<span class='bloom-overflowStart'>{(char)0x200c}</span>";

        /// <summary>
        /// A book of one page: a box holding text that stops fitting at the marker. The words are
        /// numbered so that a word lost or repeated at a join is plain to see.
        /// </summary>
        private static HtmlDom MakeBookDom(string editableContent, string chainId)
        {
            var chain = chainId == null ? "" : $" {HtmlDom.kFlowChainAttrName}='{chainId}'";
            return new HtmlDom(
                "<html><head></head><body>"
                    + "<div class='bloom-page numberedPage' id='sourcePage'>"
                    + $"<div class='bloom-translationGroup'{chain}>"
                    + "<div class='bloom-editable normal-style bloom-visibility-code-on' "
                    + $"lang='en'>{editableContent}</div>"
                    + "</div></div></body></html>"
            );
        }

        private static FlowTextChains.FlowGroup SourceGroup(HtmlDom dom)
        {
            var page = dom.RawDom.SelectSingleNode("//div[@id='sourcePage']") as SafeXmlElement;
            return FlowTextChains.GetFlowGroupsOfPage(page, "sourcePage", 0).Single();
        }

        private static string BoxText(FlowTextChains.FlowGroup group)
        {
            return FlowTextChains.GetFlowEditable(group.Group, "en").InnerText;
        }

        /// <summary>
        /// Add a page to the end of the book, holding one group with one empty box, and hand back
        /// its group. This is what Bloom's own Add Page code path does in production.
        /// </summary>
        private static FlowTextChains.FlowGroup AddPage(HtmlDom dom, int number)
        {
            var body = dom.RawDom.SelectSingleNode("//body") as SafeXmlElement;
            var pageId = $"made{number}";
            var page = dom.RawDom.CreateElement("div");
            page.SetAttribute("class", "bloom-page numberedPage");
            page.SetAttribute("id", pageId);
            var group = dom.RawDom.CreateElement("div");
            group.SetAttribute("class", "bloom-translationGroup");
            var editable = dom.RawDom.CreateElement("div");
            editable.SetAttribute("class", "bloom-editable normal-style bloom-visibility-code-on");
            editable.SetAttribute("lang", "en");
            HtmlDom.SetInnerHtmlFromFragment(editable, "<p></p>");
            group.AppendChild(editable);
            page.AppendChild(group);
            body.AppendChild(page);

            return FlowTextChains.GetFlowGroupsOfPage(page, pageId, number).Single();
        }

        /// <summary>
        /// A fit that gives the box the first `wordsPerBox` words of what it holds and carries the
        /// rest on, which exercises the loop without laying anything out. Cutting at a space is
        /// what the real fit does most of the time, and it carries the two markers the real fit
        /// puts on the tail: the paragraph continues the one before it, and a space was cut.
        ///
        /// The last box keeps everything, as the browser's fit does when told it is the last.
        /// </summary>
        private static FlowTextWalk.FitResult FitWords(
            FlowTextChains.FlowGroup group,
            bool isLast,
            int wordsPerBox
        )
        {
            var editable = FlowTextChains.GetFlowEditable(group.Group, "en");
            var content = editable.InnerXml;
            var text = editable.InnerText;
            var words = text.Split(' ').Where(word => word.Length > 0).ToList();
            if (isLast || words.Count <= wordsPerBox)
                return new FlowTextWalk.FitResult { head = content, tail = "" };

            var keeps = string.Join(" ", words.Take(wordsPerBox));
            var carries = string.Join(" ", words.Skip(wordsPerBox));
            var continues = content.Contains("data-flow-continuation");
            var opening = continues
                ? "<p data-flow-continuation='true' data-flow-seam-space='true'>"
                : "<p>";
            return new FlowTextWalk.FitResult
            {
                head = opening + keeps + "</p>",
                tail =
                    "<p data-flow-continuation='true' data-flow-seam-space='true'>"
                    + carries
                    + "</p>",
            };
        }

        /// <summary>
        /// Run the loop over a book of one page, with a fit that gives each box this many words.
        /// </summary>
        private static FlowTextCreatePages.Result Run(
            HtmlDom dom,
            int wordsPerBox,
            int maxPages = FlowTextCreatePages.kMaxPagesToCreate
        )
        {
            var made = 0;
            return FlowTextCreatePages.Run(
                SourceGroup(dom),
                "en",
                () => AddPage(dom, ++made),
                (group, isLast) => FitWords(group, isLast, wordsPerBox),
                maxPages
            );
        }

        [Test]
        public void Run_WhenAFitFails_PutsTheTextBackAndTakesAwayThePagesItMade()
        {
            // Making pages moves the tail of the box's text onto a new page before that page has
            // been laid out. If the layout never answers, the editor still holds the whole text
            // and the page made holds a copy of its tail, so the next save would write the
            // author's text into the book twice.
            var dom = MakeBookDom($"<p>w1 w2 {kMarker}w3 w4 w5 w6</p>", chainId: null);
            var source = SourceGroup(dom);
            var textBefore = BoxText(source);
            Assert.That(
                source.Group.HasAttribute(HtmlDom.kFlowChainAttrName),
                Is.False,
                "Sanity check: this box is in no chain before the work starts."
            );

            var made = 0;
            var removed = new List<string>();
            Assert.That(
                () =>
                    FlowTextCreatePages.Run(
                        SourceGroup(dom),
                        "en",
                        () => AddPage(dom, ++made),
                        (group, isLast) =>
                            throw new System.ApplicationException("the browser did not answer"),
                        removePage: group => removed.Add(group.PageId)
                    ),
                Throws.TypeOf<System.ApplicationException>(),
                "The failure is reported, not swallowed: the caller must not save."
            );

            Assert.That(made, Is.EqualTo(1), "Sanity check: one page was made before the failure.");
            Assert.That(
                removed,
                Is.EqualTo(new[] { "made1" }),
                "The page made before the failure is taken back out of the book."
            );
            Assert.That(
                BoxText(SourceGroup(dom)),
                Is.EqualTo(textBefore),
                "The source box holds the whole of its text again, tail included."
            );
            Assert.That(
                SourceGroup(dom).Group.HasAttribute(HtmlDom.kFlowChainAttrName),
                Is.False,
                "A box that was in no chain is in none again."
            );
        }

        [Test]
        public void Run_MakesPagesUntilTheLastBoxFits()
        {
            // Eight words, of which the box holds the first two. Three words go on each page
            // made, so the six that are left need two pages and the second of them is not full.
            var dom = MakeBookDom($"<p>w1 w2 {kMarker}w3 w4 w5 w6 w7 w8</p>", "chain");

            var result = Run(dom, wordsPerBox: 3);

            Assert.That(result, Is.Not.Null, "The box's text does not fit, so pages are needed.");
            Assert.That(result.PagesCreated, Is.EqualTo(2));
            Assert.That(
                BoxText(SourceGroup(dom)).Trim(),
                Is.EqualTo("w1 w2"),
                "The source box keeps what fits it, which is what stood before the mark."
            );
            Assert.That(BoxText(result.CreatedGroups[0]).Trim(), Is.EqualTo("w3 w4 w5"));
            Assert.That(BoxText(result.CreatedGroups[1]).Trim(), Is.EqualTo("w6 w7 w8"));
            Assert.That(
                result.SourceHtml,
                Does.Not.Contain("bloom-overflowStart"),
                "The source box's text fits now, so the mark that said otherwise has gone."
            );
            Assert.That(result.HitCap, Is.False);
        }

        [Test]
        public void Run_KeepsEveryWordOfTheRun()
        {
            var dom = MakeBookDom($"<p>w1 w2 {kMarker}w3 w4 w5 w6 w7 w8 w9</p>", "chain");

            var result = Run(dom, wordsPerBox: 2);

            // Read the run back the way Bloom does, joining each box's continuation paragraph onto
            // the paragraph it broke off from. Run itself throws if the words changed, so this
            // says what the run is as well as that it is whole.
            var groups = new List<FlowTextChains.FlowGroup> { SourceGroup(dom) };
            groups.AddRange(result.CreatedGroups);
            var run = FlowTextWalk.CollectRun(groups, 0, "en").InnerText;
            Assert.That(
                FlowTextWalk.ComparableWords(run),
                Is.EqualTo("w1 w2 w3 w4 w5 w6 w7 w8 w9")
            );
        }

        [Test]
        public void Run_LinksTheNewGroupsIntoTheChainTheBoxIsIn()
        {
            var dom = MakeBookDom($"<p>w1 {kMarker}w2 w3 w4</p>", "chain");

            var result = Run(dom, wordsPerBox: 2);

            Assert.That(
                result.ChainId,
                Is.EqualTo("chain"),
                "A box already in a chain keeps it, so the pages made join that run of text."
            );
            Assert.That(
                result.CreatedGroups.Select(group =>
                    group.Group.GetAttribute(HtmlDom.kFlowChainAttrName)
                ),
                Is.All.EqualTo("chain")
            );
        }

        [Test]
        public void Run_StartsAChainForABoxThatIsInNone()
        {
            // A single "Just Text" page with a long paste on it: nothing is linked to anything,
            // and taking the offer is what makes the chain.
            var dom = MakeBookDom($"<p>w1 {kMarker}w2 w3 w4</p>", null);
            Assert.That(
                SourceGroup(dom).Group.HasAttribute(HtmlDom.kFlowChainAttrName),
                Is.False,
                "The test data is wrong if the box is already in a chain."
            );

            var result = Run(dom, wordsPerBox: 2);

            Assert.That(result.ChainId, Is.Not.Null.And.Not.Empty);
            Assert.That(
                SourceGroup(dom).Group.GetAttribute(HtmlDom.kFlowChainAttrName),
                Is.EqualTo(result.ChainId),
                "The source box has to be in the chain too, or the text cannot flow back to it."
            );
            Assert.That(
                result.CreatedGroups.Select(group =>
                    group.Group.GetAttribute(HtmlDom.kFlowChainAttrName)
                ),
                Is.All.EqualTo(result.ChainId)
            );
        }

        [Test]
        public void Run_StopsAtTheCapAndLosesNoText()
        {
            // A fit that never says the text fits, however many pages it is given: the cap is
            // what has to stop the work, and every word still has to be in the book.
            var dom = MakeBookDom($"<p>w1 {kMarker}w2 w3 w4 w5 w6 w7 w8 w9</p>", "chain");
            var made = 0;

            var result = FlowTextCreatePages.Run(
                SourceGroup(dom),
                "en",
                () => AddPage(dom, ++made),
                (group, isLast) => FitWords(group, isLast: false, wordsPerBox: 2),
                maxPages: 3
            );

            Assert.That(result.PagesCreated, Is.EqualTo(3), "The cap is what stopped it.");
            Assert.That(
                result.HitCap,
                Is.True,
                "The work stopped with text still in hand, which is what the flag says."
            );
            var groups = new List<FlowTextChains.FlowGroup> { SourceGroup(dom) };
            groups.AddRange(result.CreatedGroups);
            Assert.That(
                FlowTextWalk.ComparableWords(FlowTextWalk.CollectRun(groups, 0, "en").InnerText),
                Is.EqualTo("w1 w2 w3 w4 w5 w6 w7 w8 w9"),
                "Text in no box at all is text the book has lost, so the last page keeps the rest."
            );
        }

        [Test]
        public void Run_MakesNoPagesForABoxWhoseTextFits()
        {
            var dom = MakeBookDom("<p>w1 w2 w3</p>", "chain");

            var result = Run(dom, wordsPerBox: 2);

            Assert.That(
                result,
                Is.Null,
                "Without the mark there is no text that does not fit, so nothing is needed."
            );
            Assert.That(
                dom.RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]").Length,
                Is.EqualTo(1),
                "No page was made."
            );
        }
    }
}
