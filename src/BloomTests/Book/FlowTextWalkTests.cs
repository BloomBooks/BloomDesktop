using System.Collections.Generic;
using System.Linq;
using Bloom.Book;
using Bloom.SafeXml;
using NUnit.Framework;

namespace BloomTests.Book
{
    /// <summary>
    /// Refitting a whole chain without a browser: gathering the run of text the chain carries,
    /// and putting the divided run back into its boxes.
    ///
    /// Where the text stops fitting is the browser's answer, and there is no browser here, so
    /// each test scripts that answer: the fitter these tests pass to Distribute divides the
    /// content by a rule of the test's own. What is under test is everything around it — the
    /// joining of paragraphs that were divided, the space at a seam, the box that gets the
    /// remainder, and the boxes of the other languages.
    /// </summary>
    [TestFixture]
    public class FlowTextWalkTests
    {
        private static readonly string kMarker =
            $"<span class='bloom-overflowStart'>{(char)0x200c}</span>";

        private static HtmlDom MakeBookDom(string pages)
        {
            return new HtmlDom($"<html><head></head><body>{pages}</body></html>");
        }

        private static string Page(string id, string content)
        {
            return $"<div class='bloom-page numberedPage' id='{id}'>{content}</div>";
        }

        private static string Group(string editables, string chainId = "chain")
        {
            return $"<div class='bloom-translationGroup' {HtmlDom.kFlowChainAttrName}='{chainId}'>"
                + $"{editables}</div>";
        }

        private static string Editable(
            string paragraphs,
            string lang = "en",
            string style = "normal-style"
        )
        {
            return $"<div class='bloom-editable {style} bloom-visibility-code-on' lang='{lang}'>"
                + $"{paragraphs}</div>";
        }

        private static List<FlowTextChains.FlowGroup> Chain(HtmlDom dom)
        {
            return FlowTextChains.GetChainGroups(dom, "chain");
        }

        private static string BoxHtml(HtmlDom dom, int groupIndex, string lang = "en")
        {
            return FlowTextChains.GetFlowEditable(Chain(dom)[groupIndex].Group, lang).InnerXml;
        }

        private static string BoxText(HtmlDom dom, int groupIndex, string lang = "en")
        {
            return FlowTextChains.GetFlowEditable(Chain(dom)[groupIndex].Group, lang).InnerText;
        }

        /// <summary>
        /// A fitter that gives each box the first `charactersPerBox` characters of the run's
        /// first paragraph and carries the rest on, which is enough to exercise the division
        /// without laying anything out. The last box is given everything that is left.
        /// </summary>
        private static FlowTextWalk.FitResult SplitFirstParagraphAt(
            string content,
            int charactersPerBox
        )
        {
            var opening = content.IndexOf("<p", System.StringComparison.Ordinal);
            var textStart = content.IndexOf('>', opening) + 1;
            var openingTag = content.Substring(opening, textStart - opening);
            var closing = content.IndexOf("</p>", System.StringComparison.Ordinal);
            var text = content.Substring(textStart, closing - textStart);
            if (text.Length <= charactersPerBox)
                return new FlowTextWalk.FitResult { head = content, tail = "" };

            var rest = content.Substring(closing + "</p>".Length);
            return new FlowTextWalk.FitResult
            {
                head = openingTag + text.Substring(0, charactersPerBox) + "</p>",
                tail =
                    "<p data-flow-continuation='true'>"
                    + text.Substring(charactersPerBox)
                    + "</p>"
                    + rest,
            };
        }

        [Test]
        public void Distribute_KeepsEveryWordWhenItStartsAtASeam()
        {
            // The walk starts in the middle of the chain, at a box whose first paragraph
            // carries on a paragraph that began on the page before it. Every word of the run
            // from there on has to still be there, once each, when the walk has divided it.
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>aa bb cc</p>")))
                    + Page(
                        "p2",
                        Group(
                            Editable(
                                "<p data-flow-continuation='true' data-flow-seam-space='true'>"
                                    + "dd ee ff gg hh</p>"
                            )
                        )
                    )
                    + Page(
                        "p3",
                        Group(
                            Editable(
                                "<p data-flow-continuation='true' data-flow-seam-space='true'>"
                                    + "ii jj kk</p>"
                            )
                        )
                    )
            );
            var before = FlowTextWalk.CollectRun(Chain(dom), 1, "en").InnerText;
            // Sanity check the start state: the run from the second box on is those words. The
            // seam at the front of it is the join to a page the walk does not touch, so the
            // space that was cut there stays in the attribute rather than in the text.
            Assert.That(before, Is.EqualTo("dd ee ff gg hh ii jj kk"));

            FlowTextWalk.Distribute(
                Chain(dom),
                1,
                "en",
                (group, isLast) =>
                    SplitFirstParagraphAt(
                        FlowTextChains.GetFlowEditable(group.Group, "en").InnerXml,
                        isLast ? int.MaxValue : 9
                    )
            );

            Assert.That(
                FlowTextWalk.CollectRun(Chain(dom), 1, "en").InnerText,
                Is.EqualTo(before),
                "Dividing the run must not lose or repeat a word."
            );
        }

        [Test]
        public void CollectRun_JoinsAContinuationParagraphBackOntoTheParagraphItContinues()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>The rain had</p>")))
                    + Page(
                        "p2",
                        Group(Editable("<p data-flow-continuation='true'>been falling</p>"))
                    )
            );

            var run = FlowTextWalk.CollectRun(Chain(dom), 0, "en");

            // Sanity check that the two boxes really were two paragraphs before we joined them.
            Assert.That(FlowTextChains.GetTopLevelParagraphs(Chain(dom)[1].Group), Is.Empty);
            Assert.That(
                FlowTextChains.GetTopLevelParagraphs(run).Count,
                Is.EqualTo(1),
                "The tail of a divided paragraph must not stay a paragraph of its own."
            );
            Assert.That(run.InnerText, Is.EqualTo("The rain hadbeen falling"));
        }

        [Test]
        public void CollectRun_PutsBackTheSpaceThatWasCutAtTheSeam()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable($"<p>The rain had{(char)0x200b}</p>")))
                    + Page(
                        "p2",
                        Group(
                            Editable(
                                "<p data-flow-continuation='true' data-flow-seam-space='true'>"
                                    + "been falling</p>"
                            )
                        )
                    )
            );

            var run = FlowTextWalk.CollectRun(Chain(dom), 0, "en");

            // The space is back, and the editor's own zero-width filler is not between the two
            // halves where it would separate the last word from the first.
            Assert.That(run.InnerText, Is.EqualTo("The rain had been falling"));
        }

        [Test]
        public void CollectRun_KeepsWholeParagraphsThatMovedAsParagraphs()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>one</p><p>two</p>")))
                    + Page("p2", Group(Editable("<p>three</p>")))
            );

            var run = FlowTextWalk.CollectRun(Chain(dom), 0, "en");

            // Nothing says the third paragraph continues the second, so it is its own paragraph.
            Assert.That(
                FlowTextChains.GetTopLevelParagraphs(run).Select(p => p.InnerText),
                Is.EqualTo(new[] { "one", "two", "three" })
            );
        }

        [Test]
        public void CollectRun_LeavesOutAnEmptyBoxAndTheMarksThatSayWhereTextStoppedFitting()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable($"<p>The rain{kMarker} had</p>")))
                    + Page("p2", Group(Editable("<p><br /></p>")))
            );

            var run = FlowTextWalk.CollectRun(Chain(dom), 0, "en");

            Assert.That(run.InnerXml, Does.Not.Contain("bloom-overflowStart"));
            Assert.That(
                FlowTextChains.GetTopLevelParagraphs(run).Count,
                Is.EqualTo(1),
                "An empty box's placeholder paragraph is not content and must not join the run."
            );
        }

        [Test]
        public void CollectRun_StartsAtTheGroupItIsGiven()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>one</p>")))
                    + Page("p2", Group(Editable("<p>two</p>")))
                    + Page("p3", Group(Editable("<p>three</p>")))
            );

            var run = FlowTextWalk.CollectRun(Chain(dom), 1, "en");

            Assert.That(run.InnerText, Is.EqualTo("twothree"));
        }

        [Test]
        public void Distribute_GivesTheLastBoxEverythingThatIsLeft()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>abcdefghij</p>")))
                    + Page("p2", Group(Editable("<p><br /></p>")))
            );
            var chain = Chain(dom);
            // Sanity check the start state: all of the text is in the first box.
            Assert.That(BoxText(dom, 1), Does.Not.Contain("c"));

            FlowTextWalk.Distribute(
                chain,
                0,
                "en",
                (group, isLast) =>
                    isLast
                        ? new FlowTextWalk.FitResult
                        {
                            head = FlowTextChains.GetFlowEditable(group.Group, "en").InnerXml,
                            tail = "",
                        }
                        : SplitFirstParagraphAt(
                            FlowTextChains.GetFlowEditable(group.Group, "en").InnerXml,
                            4
                        )
            );

            Assert.That(BoxText(dom, 0), Is.EqualTo("abcd"));
            Assert.That(BoxText(dom, 1), Is.EqualTo("efghij"));
        }

        [Test]
        public void Distribute_EmptiesTheBoxesTheRunNoLongerReaches()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>abcd</p>")))
                    + Page("p2", Group(Editable("<p data-flow-continuation='true'>efgh</p>")))
                    + Page("p3", Group(Editable("<p data-flow-continuation='true'>ij</p>")))
            );

            // Everything fits in the first box now, so the two boxes after it hold nothing.
            FlowTextWalk.Distribute(
                Chain(dom),
                0,
                "en",
                (group, isLast) =>
                    new FlowTextWalk.FitResult
                    {
                        head = FlowTextChains.GetFlowEditable(group.Group, "en").InnerXml,
                        tail = "",
                    }
            );

            Assert.That(BoxText(dom, 0), Is.EqualTo("abcdefghij"));
            Assert.That(BoxHtml(dom, 1), Is.EqualTo(FlowTextWalk.kPlaceholderParagraph));
            Assert.That(BoxHtml(dom, 2), Is.EqualTo(FlowTextWalk.kPlaceholderParagraph));
        }

        [Test]
        public void Distribute_KeepsTheStartBoxAContinuationOfThePageBeforeIt()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>abcd</p>")))
                    + Page(
                        "p2",
                        Group(
                            Editable(
                                "<p data-flow-continuation='true' data-flow-seam-space='true'>"
                                    + "efghij</p>"
                            )
                        )
                    )
                    + Page("p3", Group(Editable("<p><br /></p>")))
            );

            // The walk starts on the second page, so the first page is never read: what says
            // that page 2's first paragraph carries on page 1's has to survive the rewrite.
            FlowTextWalk.Distribute(
                Chain(dom),
                1,
                "en",
                (group, isLast) =>
                    isLast
                        ? new FlowTextWalk.FitResult
                        {
                            head = FlowTextChains.GetFlowEditable(group.Group, "en").InnerXml,
                            tail = "",
                        }
                        : SplitFirstParagraphAt(
                            FlowTextChains.GetFlowEditable(group.Group, "en").InnerXml,
                            3
                        )
            );

            Assert.That(BoxText(dom, 0), Is.EqualTo("abcd"), "The page before must not change.");
            Assert.That(BoxText(dom, 1), Is.EqualTo("efg"));
            Assert.That(BoxHtml(dom, 1), Does.Contain("data-flow-continuation"));
            Assert.That(BoxHtml(dom, 1), Does.Contain("data-flow-seam-space"));
            Assert.That(BoxText(dom, 2), Is.EqualTo("hij"));
        }

        [Test]
        public void Distribute_LeavesTheBoxesOfOtherLanguagesAlone()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>abcdefghij</p>") + Editable("<p>francais</p>", "fr")))
                    + Page(
                        "p2",
                        Group(Editable("<p><br /></p>") + Editable("<p>deuxieme</p>", "fr"))
                    )
            );

            FlowTextWalk.Distribute(
                Chain(dom),
                0,
                "en",
                (group, isLast) =>
                    isLast
                        ? new FlowTextWalk.FitResult
                        {
                            head = FlowTextChains.GetFlowEditable(group.Group, "en").InnerXml,
                            tail = "",
                        }
                        : SplitFirstParagraphAt(
                            FlowTextChains.GetFlowEditable(group.Group, "en").InnerXml,
                            4
                        )
            );

            Assert.That(BoxText(dom, 0), Is.EqualTo("abcd"));
            Assert.That(BoxText(dom, 0, "fr"), Is.EqualTo("francais"));
            Assert.That(BoxText(dom, 1, "fr"), Is.EqualTo("deuxieme"));
        }

        [Test]
        public void Distribute_ReportsOnlyTheGroupsWhoseBoxChanged()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>abcd</p>")))
                    + Page("p2", Group(Editable("<p data-flow-continuation='true'>efgh</p>")))
            );

            // The fitter divides the run at exactly the place it is already divided, so nothing
            // moves and nothing needs saving.
            var changed = FlowTextWalk.Distribute(
                Chain(dom),
                0,
                "en",
                (group, isLast) =>
                    isLast
                        ? new FlowTextWalk.FitResult
                        {
                            head = FlowTextChains.GetFlowEditable(group.Group, "en").InnerXml,
                            tail = "",
                        }
                        : SplitFirstParagraphAt(
                            FlowTextChains.GetFlowEditable(group.Group, "en").InnerXml,
                            4
                        )
            );

            Assert.That(changed, Is.Empty);
            Assert.That(BoxText(dom, 0), Is.EqualTo("abcd"));
            Assert.That(BoxText(dom, 1), Is.EqualTo("efgh"));
        }

        /// <summary>
        /// A page carrying the warning the thumbnail shows a triangle for, as a page whose box
        /// was overfull when the reader last edited it carries it.
        /// </summary>
        private static string WarningPage(string id, string content)
        {
            return $"<div class='bloom-page numberedPage pageOverflows' id='{id}'>{content}</div>";
        }

        private static SafeXmlElement PageOf(HtmlDom dom, int groupIndex)
        {
            return Chain(dom)[groupIndex].Group.ParentWithClass("bloom-page");
        }

        /// <summary>A fitter that gives each box the first four characters and carries the rest on.</summary>
        private static FlowTextWalk.FitResult FitFourCharacters(
            FlowTextChains.FlowGroup group,
            bool isLast
        )
        {
            var content = FlowTextChains.GetFlowEditable(group.Group, "en").InnerXml;
            return isLast
                ? new FlowTextWalk.FitResult { head = content, tail = "" }
                : SplitFirstParagraphAt(content, 4);
        }

        [Test]
        public void Distribute_TakesTheOverflowWarningOffAPageWhoseBoxHandsItsTextOn()
        {
            var dom = MakeBookDom(
                WarningPage(
                    "p1",
                    Group(Editable("<p>abcdefghij</p>", "en", "normal-style overflow"))
                ) + Page("p2", Group(Editable("<p><br /></p>")))
            );
            // Sanity check the start state: the page and its box both claim to be overfull.
            Assert.That(PageOf(dom, 0).HasClass("pageOverflows"), Is.True);

            var changed = FlowTextWalk.Distribute(Chain(dom), 0, "en", FitFourCharacters);

            Assert.That(BoxText(dom, 0), Is.EqualTo("abcd"));
            Assert.That(
                PageOf(dom, 0).HasClass("pageOverflows"),
                Is.False,
                "The box's extra text has gone to the next page, so the page is not overflowing."
            );
            Assert.That(
                FlowTextChains.GetFlowEditable(Chain(dom)[0].Group, "en").HasClass("overflow"),
                Is.False
            );
            Assert.That(changed.Select(group => group.PageId), Does.Contain("p1"));
        }

        [Test]
        public void Distribute_LeavesTheWarningOnAPageWhoseLastBoxRanOutOfRoom()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>abcdefghij</p>")))
                    + WarningPage(
                        "p2",
                        Group(Editable("<p><br /></p>", "en", "normal-style overflow"))
                    )
            );

            FlowTextWalk.Distribute(
                Chain(dom),
                0,
                "en",
                (group, isLast) =>
                {
                    var content = FlowTextChains.GetFlowEditable(group.Group, "en").InnerXml;
                    // The last box keeps everything, with the mark that says where its text
                    // stopped fitting: it has nowhere to send the rest.
                    return isLast
                        ? new FlowTextWalk.FitResult
                        {
                            head = $"<p data-flow-continuation='true'>ef{kMarker}ghij</p>",
                            tail = "",
                        }
                        : SplitFirstParagraphAt(content, 4);
                }
            );

            Assert.That(
                PageOf(dom, 1).HasClass("pageOverflows"),
                Is.True,
                "The last box's text runs out inside it, so its page is still overflowing."
            );
        }

        [Test]
        public void Distribute_LeavesTheWarningOnAPageWhereAnotherBoxIsStillOverfull()
        {
            var dom = MakeBookDom(
                WarningPage(
                    "p1",
                    Group(Editable("<p>abcdefghij</p>"))
                        + Group(
                            Editable("<p>elsewhere</p>", "en", "normal-style overflow"),
                            "other"
                        )
                ) + Page("p2", Group(Editable("<p><br /></p>")))
            );

            FlowTextWalk.Distribute(Chain(dom), 0, "en", FitFourCharacters);

            Assert.That(
                PageOf(dom, 0).HasClass("pageOverflows"),
                Is.True,
                "A box of another chain on the page still holds more text than fits it."
            );
        }

        [Test]
        public void Distribute_SavesAPageWhoseOnlyChangeIsTheWarningComingOff()
        {
            var dom = MakeBookDom(
                WarningPage("p1", Group(Editable("<p>abcd</p>", "en", "normal-style overflow")))
                    + Page("p2", Group(Editable("<p data-flow-continuation='true'>efgh</p>")))
            );

            // Every box already holds what the fit gives it, so nothing but the warning changes.
            var wasInFirstBox = BoxHtml(dom, 0);
            var wasInSecondBox = BoxHtml(dom, 1);

            var changed = FlowTextWalk.Distribute(Chain(dom), 0, "en", FitFourCharacters);

            Assert.That(BoxHtml(dom, 0), Is.EqualTo(wasInFirstBox));
            Assert.That(BoxHtml(dom, 1), Is.EqualTo(wasInSecondBox));
            Assert.That(PageOf(dom, 0).HasClass("pageOverflows"), Is.False);
            Assert.That(
                changed.Select(group => group.PageId),
                Does.Contain("p1"),
                "The page has to be saved, or the thumbnail goes on showing its triangle."
            );
        }

        [Test]
        public void GetChainIds_ReportsEachChainOnce()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>one</p>")) + Group(Editable("<p>two</p>"), "other"))
                    + Page("p2", Group(Editable("<p>three</p>")))
            );

            var ids = FlowTextWalk.GetChainIds(dom);

            Assert.That(ids.OrderBy(id => id), Is.EqualTo(new[] { "chain", "other" }));
        }
    }
}
