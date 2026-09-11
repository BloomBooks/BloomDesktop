using System;
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

        /// <summary>
        /// Stand in for the Add Page code path: put one text-only page at the end of the book and
        /// hand back the group on it, in no chain, as FlowTextApi's page maker does.
        /// </summary>
        private static FlowTextChains.FlowGroup AppendTextOnlyPage(HtmlDom dom, string pageId)
        {
            var scratch = dom.RawDom.CreateElement("div");
            HtmlDom.SetInnerHtmlFromFragment(
                scratch,
                $"<div class='bloom-page numberedPage' id='{pageId}'>"
                    + "<div class='bloom-translationGroup'>"
                    + Editable("<p><br /></p>")
                    + "</div></div>"
            );
            var page = scratch.FirstChild as SafeXmlElement;
            dom.Body.AppendChild(page);
            var pages = dom
                .RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")
                .OfType<SafeXmlElement>()
                .ToList();
            return new FlowTextChains.FlowGroup
            {
                PageId = pageId,
                PageIndex = pages.IndexOf(page),
                IndexInPage = 0,
                Group = page.SafeSelectNodes(".//div[contains(@class,'bloom-translationGroup')]")
                    .OfType<SafeXmlElement>()
                    .First(),
            };
        }

        /// <summary>
        /// A fitter that gives every box four characters of the run and carries the rest on, and
        /// that marks where the text ran out when it is told this is the last box, as the browser
        /// does. Four characters a box makes the number of pages a run needs easy to count.
        /// </summary>
        private static FlowTextWalk.FitResult FitFourCharactersMarkingOverflow(
            FlowTextChains.FlowGroup group,
            bool isLast
        )
        {
            var fitted = SplitFirstParagraphAt(
                FlowTextChains.GetFlowEditable(group.Group, "en").InnerXml,
                4
            );
            if (isLast && !string.IsNullOrEmpty(fitted.tail))
                fitted.head = ReplaceFirst(fitted.head, "</p>", kMarker + "</p>");
            return fitted;
        }

        private static string ReplaceFirst(string text, string wanted, string replacement)
        {
            var at = text.IndexOf(wanted, StringComparison.Ordinal);
            return at < 0
                ? text
                : text.Substring(0, at) + replacement + text.Substring(at + wanted.Length);
        }

        [Test]
        public void Distribute_WithAddPage_MakesAPageForEachBoxfulTheChainCannotHold()
        {
            var dom = MakeBookDom(Page("p1", Group(Editable("<p>aaaabbbbccccdddd</p>"))));
            // Sanity check the start state: one page, holding the whole run, and four
            // characters a box means it needs four boxes in all.
            Assert.That(Chain(dom).Count, Is.EqualTo(1));
            Assert.That(BoxText(dom, 0), Is.EqualTo("aaaabbbbccccdddd"));

            var added = 0;
            var changed = FlowTextWalk.Distribute(
                Chain(dom),
                0,
                "en",
                FitFourCharactersMarkingOverflow,
                () => AppendTextOnlyPage(dom, "made" + ++added)
            );

            Assert.That(added, Is.EqualTo(3), "Three more boxes were needed to hold the run.");
            var chain = Chain(dom);
            Assert.That(
                chain.Select(group => group.PageId),
                Is.EqualTo(new[] { "p1", "made1", "made2", "made3" }),
                "Every page made carries the chain, in the order the text flows through them."
            );
            Assert.That(
                chain.Select((group, index) => BoxText(dom, index)),
                Is.EqualTo(new[] { "aaaa", "bbbb", "cccc", "dddd" })
            );
            Assert.That(
                changed.Select(group => group.PageId),
                Is.EqualTo(new[] { "p1", "made1", "made2", "made3" }),
                "The pages made are for the caller to save along with the boxes it changed."
            );
            Assert.That(
                FlowTextWalk.CollectRun(chain, 0, "en").InnerText,
                Is.EqualTo("aaaabbbbccccdddd"),
                "Dividing the run over new pages must not lose or repeat a word."
            );
        }

        [Test]
        public void Distribute_WithAddPage_StopsAtTheCapAndLeavesTheRestInTheLastPage()
        {
            var dom = MakeBookDom(Page("p1", Group(Editable("<p>aaaabbbbccccdddd</p>"))));
            // Sanity check: without a cap this run would need three pages made, as the test
            // above shows.
            Assert.That(BoxText(dom, 0), Is.EqualTo("aaaabbbbccccdddd"));

            var added = 0;
            FlowTextWalk.Distribute(
                Chain(dom),
                0,
                "en",
                FitFourCharactersMarkingOverflow,
                () => AppendTextOnlyPage(dom, "made" + ++added),
                maxPagesToAdd: 1
            );

            Assert.That(added, Is.EqualTo(1), "The cap allows one page and no more.");
            var chain = Chain(dom);
            Assert.That(BoxText(dom, 0), Is.EqualTo("aaaa"));
            Assert.That(
                BoxText(dom, 1).Replace(((char)0x200c).ToString(), ""),
                Is.EqualTo("bbbbccccdddd"),
                "Text in no box at all is text the book has lost, so the last page keeps it."
            );
            Assert.That(
                BoxHtml(dom, 1),
                Does.Contain(HtmlDom.kOverflowStartClass),
                "The mark is what says the last box holds more than fits it."
            );
            Assert.That(
                FlowTextWalk.CollectRun(chain, 0, "en").InnerText,
                Is.EqualTo("aaaabbbbccccdddd"),
                "The run is still whole, so the work can be asked for again."
            );
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

        /// <summary>
        /// Two pages of one chain, which is the least that RequestEveryChain will queue.
        /// </summary>
        private static HtmlDom TwoPageChain()
        {
            return MakeBookDom(
                Page("p1", Group(Editable("<p>abcdefghij</p>")))
                    + Page("p2", Group(Editable("<p><br /></p>")))
            );
        }

        [Test]
        public void QueueFromPageForward_QueuesARefitOfThePagesFromHereOn()
        {
            FlowTextWalk.ClearQueueForTests();
            var dom = TwoPageChain();

            FlowTextWalk.QueueFromPageForward(dom, "chain", "p2", "en", null);

            var queued = FlowTextWalk.QueuedWalksForTests();
            Assert.That(queued.Count, Is.EqualTo(1), "One chain was asked about, so one walk.");
            Assert.That(queued[0].FromPageId, Is.EqualTo("p2"));
            Assert.That(queued[0].Kind, Is.EqualTo(FlowTextWalk.WalkKind.FromPageForward));
            FlowTextWalk.ClearQueueForTests();
        }

        [Test]
        public void QueueEveryChain_QueuesARefitOfTheWholeFlow()
        {
            FlowTextWalk.ClearQueueForTests();
            var dom = TwoPageChain();

            FlowTextWalk.QueueEveryChain(dom, "en");

            var queued = FlowTextWalk.QueuedWalksForTests();
            Assert.That(queued.Count, Is.EqualTo(1));
            Assert.That(
                queued[0].FromPageId,
                Is.EqualTo("p1"),
                "A refit of the whole flow starts at its first page."
            );
            Assert.That(queued[0].Kind, Is.EqualTo(FlowTextWalk.WalkKind.WholeFlow));
            FlowTextWalk.ClearQueueForTests();
        }

        [Test]
        public void QueueEveryChain_AfterAWalkFromAPageOn_StillReportsTheWholeFlow()
        {
            FlowTextWalk.ClearQueueForTests();
            var dom = TwoPageChain();

            FlowTextWalk.QueueFromPageForward(dom, "chain", "p1", "en", null);
            var queuedFirst = FlowTextWalk.QueuedWalksForTests();
            Assert.That(
                queuedFirst[0].Kind,
                Is.EqualTo(FlowTextWalk.WalkKind.FromPageForward),
                "Sanity check: the entry the second request has to merge into."
            );

            // Both start at the same page, so the second request merges into the first.
            FlowTextWalk.QueueEveryChain(dom, "en");

            var queued = FlowTextWalk.QueuedWalksForTests();
            Assert.That(queued.Count, Is.EqualTo(1), "One chain and one language is one walk.");
            Assert.That(
                queued[0].Kind,
                Is.EqualTo(FlowTextWalk.WalkKind.WholeFlow),
                "The wider reason for the walk is the one to report."
            );
            FlowTextWalk.ClearQueueForTests();
        }

        [Test]
        public void QueueFromPageForward_AfterAWalkOfTheWholeFlow_StillReportsTheWholeFlow()
        {
            FlowTextWalk.ClearQueueForTests();
            var dom = TwoPageChain();

            FlowTextWalk.QueueEveryChain(dom, "en");
            // The whole flow is already going to be refitted from page one, so this merges in.
            FlowTextWalk.QueueFromPageForward(dom, "chain", "p2", "en", null);

            var queued = FlowTextWalk.QueuedWalksForTests();
            Assert.That(queued.Count, Is.EqualTo(1));
            Assert.That(queued[0].FromPageId, Is.EqualTo("p1"));
            Assert.That(queued[0].Kind, Is.EqualTo(FlowTextWalk.WalkKind.WholeFlow));
            FlowTextWalk.ClearQueueForTests();
        }

        [Test]
        public void QueueFromPageForward_QueuesTheWalkButDoesNotRunIt()
        {
            FlowTextWalk.ClearQueueForTests();
            var dom = TwoPageChain();
            Assert.That(FlowTextWalk.HasPending, Is.False, "Sanity check: nothing queued yet.");

            FlowTextWalk.QueueFromPageForward(dom, "chain", "p1", "en", null);

            Assert.That(FlowTextWalk.HasPending, Is.True, "The walk is waiting to be run.");
            Assert.That(
                FlowTextWalk.IsBusy,
                Is.False,
                "A walk merely waiting is not work in progress: nothing will run it until the "
                    + "user changes pages or asks for it, so anything waiting for it would wait "
                    + "for ever."
            );
            FlowTextWalk.ClearQueueForTests();
        }

        [Test]
        public void PendingChainIds_ReportsEachChainOnce()
        {
            FlowTextWalk.ClearQueueForTests();
            var dom = MakeBookDom(
                Page(
                    "p1",
                    Group(Editable("<p>one</p>") + Editable("<p>uno</p>", "es"))
                        + Group(Editable("<p>two</p>"), "other")
                )
                    + Page(
                        "p2",
                        Group(Editable("<p><br /></p>") + Editable("<p><br /></p>", "es"))
                            + Group(Editable("<p><br /></p>"), "other")
                    )
            );

            // Two languages of one chain are two walks, but one chain.
            FlowTextWalk.QueueFromPageForward(dom, "chain", "p1", "en", null);
            FlowTextWalk.QueueFromPageForward(dom, "chain", "p1", "es", null);
            FlowTextWalk.QueueFromPageForward(dom, "other", "p1", "en", null);
            Assert.That(
                FlowTextWalk.QueuedWalksForTests().Count,
                Is.EqualTo(3),
                "Sanity check: one walk per chain and language."
            );

            Assert.That(
                FlowTextWalk.PendingChainIds.OrderBy(id => id),
                Is.EqualTo(new[] { "chain", "other" })
            );
            FlowTextWalk.ClearQueueForTests();
        }

        [Test]
        public void RunPending_WithNothingQueued_LeavesNothingRunning()
        {
            FlowTextWalk.ClearQueueForTests();

            FlowTextWalk.RunPending(null);

            Assert.That(FlowTextWalk.IsBusy, Is.False);
            Assert.That(FlowTextWalk.HasPending, Is.False);
        }

        [Test]
        public void RunPending_RunsTheQueueOut()
        {
            FlowTextWalk.ClearQueueForTests();
            var dom = TwoPageChain();
            FlowTextWalk.QueueFromPageForward(dom, "chain", "p1", "en", null);
            Assert.That(FlowTextWalk.HasPending, Is.True, "Sanity check: there is a walk to run.");

            // There is no book here, so each walk finds nothing to fit and is taken off the
            // queue; what is under test is that RunPending starts the queue at all, and that it
            // ends with nothing waiting and nothing running.
            FlowTextWalk.RunPending(null);

            var gaveUpAt = DateTime.Now.AddSeconds(10);
            while ((FlowTextWalk.HasPending || FlowTextWalk.IsBusy) && DateTime.Now < gaveUpAt)
                System.Threading.Thread.Sleep(20);

            Assert.That(FlowTextWalk.HasPending, Is.False, "The queue ran out.");
            Assert.That(FlowTextWalk.IsBusy, Is.False, "Nothing is running any more.");
            FlowTextWalk.ClearQueueForTests();
        }

        [Test]
        public void QueueEveryChain_QueuesAChainThatReachesThePageBeingEdited()
        {
            FlowTextWalk.ClearQueueForTests();
            var dom = TwoPageChain();

            FlowTextWalk.QueueEveryChain(dom, "en");

            var queued = FlowTextWalk.QueuedWalksForTests();
            Assert.That(
                queued.Count,
                Is.EqualTo(1),
                "Wherever the user is standing, the chain has to be refitted: the walk covers "
                    + "the page being edited along with the rest."
            );
            var groups = FlowTextWalk.GroupsToFit(Chain(dom), queued[0].FromPageId, out var start);
            Assert.That(start, Is.EqualTo(0));
            Assert.That(
                groups.Select(group => group.PageId),
                Is.EqualTo(new[] { "p1", "p2" }),
                "Whichever page the user is on is one of the pages the walk fits."
            );
            FlowTextWalk.ClearQueueForTests();
        }

        [Test]
        public void GroupsToFit_CoversTheChainToItsEndFromTheStartPage()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>one</p>")))
                    + Page("p2", Group(Editable("<p>two</p>")))
                    + Page("p3", Group(Editable("<p>three</p>")))
            );
            // Sanity check the start state: the chain is those three pages, in book order.
            Assert.That(
                Chain(dom).Select(group => group.PageId),
                Is.EqualTo(new[] { "p1", "p2", "p3" })
            );

            var groups = FlowTextWalk.GroupsToFit(Chain(dom), "p2", out var start);

            Assert.That(
                start,
                Is.EqualTo(1),
                "The walk begins at the group on the page it was asked to start at."
            );
            Assert.That(
                groups.Select(group => group.PageId),
                Is.EqualTo(new[] { "p1", "p2", "p3" }),
                "Everything from the start page to the end of the chain is refitted, and what "
                    + "comes before the start is read for its markers."
            );
        }

        [Test]
        public void GroupsToFit_WithNoGroupOnTheStartPage_FindsNothingToFit()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>one</p>")))
                    + Page("p2", Group(Editable("<p>two</p>")))
            );

            var groups = FlowTextWalk.GroupsToFit(Chain(dom), "pageOfSomeOtherBook", out var start);

            Assert.That(groups, Is.Null);
            Assert.That(start, Is.EqualTo(0));
        }

        [Test]
        public void TakeRefitResults_HandsBackThisPagesBoxesOnceEach()
        {
            FlowTextWalk.ClearRefitResults();
            Assert.That(
                FlowTextWalk.TakeRefitResults("p1"),
                Is.Empty,
                "Sanity check: nothing is being held for that page yet."
            );

            FlowTextWalk.HoldRefitResult(
                "p1",
                new FlowTextWalk.RefitResult
                {
                    chainId = "chain",
                    lang = "en",
                    indexInPage = 0,
                    html = "<p>abcd</p>",
                }
            );

            var held = FlowTextWalk.TakeRefitResults("p1");
            Assert.That(held.Count, Is.EqualTo(1));
            Assert.That(held[0].chainId, Is.EqualTo("chain"));
            Assert.That(held[0].lang, Is.EqualTo("en"));
            Assert.That(held[0].indexInPage, Is.EqualTo(0));
            Assert.That(held[0].html, Is.EqualTo("<p>abcd</p>"));
            Assert.That(
                FlowTextWalk.TakeRefitResults("p1"),
                Is.Empty,
                "The browser puts each box in place once, so reading forgets it."
            );
        }

        [Test]
        public void TakeRefitResults_HandsBackNothingForAnotherPage()
        {
            FlowTextWalk.ClearRefitResults();
            FlowTextWalk.HoldRefitResult(
                "p1",
                new FlowTextWalk.RefitResult
                {
                    chainId = "chain",
                    lang = "en",
                    indexInPage = 0,
                    html = "<p>abcd</p>",
                }
            );

            Assert.That(FlowTextWalk.TakeRefitResults("p2"), Is.Empty);
            Assert.That(
                FlowTextWalk.TakeRefitResults("p1").Count,
                Is.EqualTo(1),
                "Sanity check: asking about another page did not consume this one's box."
            );
            FlowTextWalk.ClearRefitResults();
        }

        [Test]
        public void ClearRefitResults_ForgetsWhatWasHeld()
        {
            FlowTextWalk.ClearRefitResults();
            FlowTextWalk.HoldRefitResult(
                "p1",
                new FlowTextWalk.RefitResult
                {
                    chainId = "chain",
                    lang = "en",
                    indexInPage = 0,
                    html = "<p>abcd</p>",
                }
            );

            FlowTextWalk.ClearRefitResults();

            Assert.That(FlowTextWalk.TakeRefitResults("p1"), Is.Empty);
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
