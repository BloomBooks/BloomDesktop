using System.Linq;
using Bloom.Book;
using Bloom.SafeXml;
using Moq;
using NUnit.Framework;

namespace BloomTests.Book
{
    /// <summary>
    /// The book-wide half of flow text: which boxes make up a chain, where an earlier box's text
    /// stops fitting, and what moving the part that does not fit does to the two boxes.
    ///
    /// The marker in these tests is the same zero-width character the browser writes, because the
    /// whole point of the marker is that C# reads what the browser wrote without measuring.
    /// </summary>
    [TestFixture]
    public class FlowTextChainsTests : BookTestsBase
    {
        private static readonly string kMarker =
            $"<span class='bloom-overflowStart'>{(char)0x200c}</span>";

        protected override string GetTestFolderName() => "FlowTextChainsTests";

        private static HtmlDom MakeBookDom(string pages)
        {
            return new HtmlDom($"<html><head></head><body>{pages}</body></html>");
        }

        private static string Page(string id, string content, string extraClasses = "")
        {
            return $"<div class='bloom-page numberedPage {extraClasses}' id='{id}'>{content}</div>";
        }

        private static string Group(string editables, string chainId = null)
        {
            var chain = chainId == null ? "" : $" {HtmlDom.kFlowChainAttrName}='{chainId}'";
            return $"<div class='bloom-translationGroup'{chain}>{editables}</div>";
        }

        private static string Editable(
            string paragraphs,
            string lang = "en",
            string style = "normal-style"
        )
        {
            return $"<div class='bloom-editable {style} bloom-visibility-code-on' lang='{lang}'>{paragraphs}</div>";
        }

        private static SafeXmlElement FirstEditable(HtmlDom dom, string pageId)
        {
            return dom.RawDom.SelectSingleNode(
                    $"//div[@id='{pageId}']//div[contains(@class,'bloom-editable')]"
                ) as SafeXmlElement;
        }

        private static SafeXmlElement EditableOfLang(HtmlDom dom, string pageId, string lang)
        {
            return dom.RawDom.SelectSingleNode(
                    $"//div[@id='{pageId}']//div[contains(@class,'bloom-editable')][@lang='{lang}']"
                ) as SafeXmlElement;
        }

        private static SafeXmlElement PageElement(HtmlDom dom, string pageId)
        {
            return dom.RawDom.SelectSingleNode($"//div[@id='{pageId}']") as SafeXmlElement;
        }

        private static string[] ParagraphTexts(SafeXmlElement editable)
        {
            return FlowTextChains
                .GetTopLevelParagraphs(editable)
                .Select(paragraph => paragraph.InnerText)
                .ToArray();
        }

        private static readonly string kContinues =
            $" {FlowTextChains.kContinuationAttrName}='true'";
        private static readonly string kSeam = $" {FlowTextChains.kSeamSpaceAttrName}='true'";

        [Test]
        public void GetChainGroups_ReturnsGroupsInPageThenDocumentOrder()
        {
            var dom = MakeBookDom(
                Page(
                    "p1",
                    Group(Editable("<p>one</p>"), "chain") + Group(Editable("<p>two</p>"), "chain")
                )
                    + Page("p2", Group(Editable("<p>three</p>"), "chain"))
                    + Page("p3", Group(Editable("<p>other</p>"), "otherChain"))
            );

            var groups = FlowTextChains.GetChainGroups(dom, "chain");

            Assert.That(groups.Count, Is.EqualTo(3));
            Assert.That(
                groups.Select(g => $"{g.PageId}:{g.IndexInPage}"),
                Is.EqualTo(new[] { "p1:0", "p1:1", "p2:0" })
            );
        }

        [Test]
        public void GetChainGroups_IgnoresGroupsInsideABloomCanvas()
        {
            // A canvas element lays its own text out, so it can never be part of a chain, however
            // the attribute got onto it.
            var dom = MakeBookDom(
                Page(
                    "p1",
                    Group(Editable("<p>page text</p>"), "chain")
                        + "<div class='bloom-canvas'>"
                        + Group(Editable("<p>canvas text</p>"), "chain")
                        + "</div>"
                )
            );

            var groups = FlowTextChains.GetChainGroups(dom, "chain");

            Assert.That(groups.Count, Is.EqualTo(1));
            Assert.That(groups[0].Group.InnerText, Does.Contain("page text"));
        }

        [Test]
        public void FindPendingOverflowBefore_FindsTheNearestEarlierPage()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable($"<p>first{kMarker} spills</p>")))
                    + Page("p2", Group(Editable($"<p>second{kMarker} spills too</p>")))
                    + Page("p3", Group(Editable("<p><br/></p>")))
            );

            var found = FlowTextChains.FindPendingOverflowBefore(dom, "p3", "en");

            Assert.That(found, Is.Not.Null);
            Assert.That(found.PageId, Is.EqualTo("p2"));
            Assert.That(found.IndexInPage, Is.EqualTo(0));
            Assert.That(found.PreviewText, Is.EqualTo("spills too"));
        }

        [Test]
        public void FindPendingOverflowBefore_SkipsFrontAndBackMatter()
        {
            var dom = MakeBookDom(
                Page("cover", Group(Editable($"<p>title{kMarker} spills</p>")), "bloom-frontMatter")
                    + Page("p1", Group(Editable("<p><br/></p>")))
            );

            Assert.That(FlowTextChains.FindPendingOverflowBefore(dom, "p1", "en"), Is.Null);
        }

        [Test]
        public void FindPendingOverflowBefore_SkipsABoxThatIsNotNormalStyle()
        {
            var dom = MakeBookDom(
                Page(
                    "p1",
                    Group(Editable($"<p>heading{kMarker} spills</p>", "en", "Heading1-style"))
                ) + Page("p2", Group(Editable("<p><br/></p>")))
            );

            Assert.That(FlowTextChains.FindPendingOverflowBefore(dom, "p2", "en"), Is.Null);
        }

        [Test]
        public void FindPendingOverflowBefore_SkipsABoxOfAnotherLanguage()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable($"<p>french{kMarker} spills</p>", "fr")))
                    + Page("p2", Group(Editable("<p><br/></p>")))
            );

            Assert.That(FlowTextChains.FindPendingOverflowBefore(dom, "p2", "en"), Is.Null);
        }

        [Test]
        public void FindPendingOverflowBefore_ReturnsNullWhenNothingOverflows()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>it all fits</p>")))
                    + Page("p2", Group(Editable("<p><br/></p>")))
            );

            Assert.That(FlowTextChains.FindPendingOverflowBefore(dom, "p2", "en"), Is.Null);
        }

        [Test]
        public void FindPendingOverflowBefore_UsesTheLabelTheCallerSupplies()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable($"<p>first{kMarker} spills</p>")))
                    + Page("p2", Group(Editable("<p><br/></p>")))
            );

            var found = FlowTextChains.FindPendingOverflowBefore(
                dom,
                "p2",
                "en",
                pageId => pageId == "p1" ? "7" : "?"
            );

            Assert.That(found.PageNumberLabel, Is.EqualTo("7"));
        }

        [Test]
        public void SplitParagraphAtMarker_MarkerInPlainText_DividesTheParagraph()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable($"<p>this fits{kMarker} and this does not</p>")))
            );
            var editable = FirstEditable(dom, "p1");

            var split = FlowTextChains.SplitParagraphAtMarker(editable);

            Assert.That(split.IsContinuation, Is.True);
            Assert.That(editable.InnerText, Is.EqualTo("this fits"));
            Assert.That(split.TailNodes.Count, Is.EqualTo(1));
            Assert.That(split.TailNodes[0].InnerText, Is.EqualTo(" and this does not"));
            // The marker described a fit in the box it came out of, so it does not travel.
            Assert.That(FlowTextChains.FindOverflowMarker(editable), Is.Null);
        }

        [Test]
        public void SplitParagraphAtMarker_MarkerAfterASpace_TheSpaceGoesWithTheTail()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable($"<p>this fits {kMarker}and this does not</p>")))
            );
            var editable = FirstEditable(dom, "p1");
            // Sanity check the starting point: the space is in the head, before the marker.
            Assert.That(editable.InnerText, Does.StartWith("this fits "), "test setup");

            var split = FlowTextChains.SplitParagraphAtMarker(editable);

            // Neither half can hold a space at the seam: the editor writes a paragraph's trailing
            // space as a zero-width filler, and a leading one does not survive the save. So the
            // attribute records it and the editor puts it back when it joins the halves.
            Assert.That(editable.InnerText, Is.EqualTo("this fits"));
            var tailParagraph = (SafeXmlElement)split.TailNodes[0];
            Assert.That(tailParagraph.InnerText, Is.EqualTo("and this does not"));
            Assert.That(
                tailParagraph.GetAttribute(FlowTextChains.kSeamSpaceAttrName),
                Is.EqualTo("true")
            );
        }

        [Test]
        public void SplitParagraphAtMarker_MarkerInsideBoldSpan_ClonesTheSpanIntoBothHalves()
        {
            var dom = MakeBookDom(
                Page(
                    "p1",
                    Group(Editable($"<p>plain <strong>bold {kMarker}more bold</strong> tail</p>"))
                )
            );
            var editable = FirstEditable(dom, "p1");

            var split = FlowTextChains.SplitParagraphAtMarker(editable);

            // Text that was bold before the split is still bold on both sides of it. The space at
            // the point of the split belongs to neither half, so the attribute records it.
            Assert.That(editable.InnerXml, Does.Contain("<strong>bold</strong>"));
            var tail = (SafeXmlElement)split.TailNodes[0];
            Assert.That(tail.InnerXml, Does.Contain("<strong>more bold</strong>"));
            Assert.That(tail.InnerText, Is.EqualTo("more bold tail"));
            Assert.That(tail.GetAttribute(FlowTextChains.kSeamSpaceAttrName), Is.EqualTo("true"));
        }

        [Test]
        public void SplitParagraphAtMarker_MarkerAtParagraphStart_MovesTheWholeParagraph()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable($"<p>first</p><p>{kMarker}second</p><p>third</p>")))
            );
            var editable = FirstEditable(dom, "p1");

            var split = FlowTextChains.SplitParagraphAtMarker(editable);

            // Nothing of that paragraph stays behind, so no paragraph is divided and the one that
            // moves is a whole paragraph where it lands.
            Assert.That(split.IsContinuation, Is.False);
            Assert.That(editable.InnerText, Is.EqualTo("first"));
            Assert.That(
                split.TailNodes.Select(node => node.InnerText),
                Is.EqualTo(new[] { "second", "third" })
            );
        }

        [Test]
        public void SplitParagraphAtMarker_MarkerAtParagraphEnd_MovesOnlyTheLaterParagraphs()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable($"<p>first{kMarker}</p><p>second</p>")))
            );
            var editable = FirstEditable(dom, "p1");

            var split = FlowTextChains.SplitParagraphAtMarker(editable);

            Assert.That(split.IsContinuation, Is.False);
            Assert.That(editable.InnerText, Is.EqualTo("first"));
            Assert.That(split.TailNodes.Count, Is.EqualTo(1));
            Assert.That(split.TailNodes[0].InnerText, Is.EqualTo("second"));
        }

        [Test]
        public void SplitParagraphAtMarker_MarkerInALaterParagraph_KeepsTheEarlierOnes()
        {
            var dom = MakeBookDom(
                Page(
                    "p1",
                    Group(Editable($"<p>one</p><p>two fits{kMarker} two spills</p><p>three</p>"))
                )
            );
            var editable = FirstEditable(dom, "p1");

            var split = FlowTextChains.SplitParagraphAtMarker(editable);

            Assert.That(split.IsContinuation, Is.True);
            Assert.That(
                editable.ChildNodes.OfType<SafeXmlElement>().Select(child => child.InnerText),
                Is.EqualTo(new[] { "one", "two fits" })
            );
            Assert.That(
                split.TailNodes.Select(node => node.InnerText),
                Is.EqualTo(new[] { " two spills", "three" })
            );
        }

        [Test]
        public void SplitParagraphAtMarker_NoMarker_ReturnsNull()
        {
            var dom = MakeBookDom(Page("p1", Group(Editable("<p>it all fits</p>"))));

            Assert.That(FlowTextChains.SplitParagraphAtMarker(FirstEditable(dom, "p1")), Is.Null);
        }

        [Test]
        public void MoveTailInto_MarksTheContinuationAndReplacesThePlaceholder()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable($"<p>this fits{kMarker} and this does not</p>")))
                    + Page("p2", Group(Editable("<p><br/></p>")))
            );
            var source = FirstEditable(dom, "p1");
            var target = FirstEditable(dom, "p2");

            Assert.That(FlowTextChains.MoveTailInto(source, target), Is.True);

            var paragraphs = target.ChildNodes.OfType<SafeXmlElement>().ToArray();
            Assert.That(paragraphs.Length, Is.EqualTo(1), "The empty placeholder should be gone.");
            Assert.That(paragraphs[0].InnerText, Is.EqualTo(" and this does not"));
            Assert.That(
                paragraphs[0].GetAttribute(FlowTextChains.kContinuationAttrName),
                Is.EqualTo("true")
            );
        }

        [Test]
        public void MoveTailInto_PutsTheTailAheadOfWhatTheTargetAlreadyHeld()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable($"<p>fits{kMarker} spills</p>")))
                    + Page("p2", Group(Editable("<p>already here</p>")))
            );
            var source = FirstEditable(dom, "p1");
            var target = FirstEditable(dom, "p2");

            FlowTextChains.MoveTailInto(source, target);

            Assert.That(
                target.ChildNodes.OfType<SafeXmlElement>().Select(child => child.InnerText),
                Is.EqualTo(new[] { " spills", "already here" })
            );
        }

        [Test]
        public void MoveTailInto_NoMarker_ChangesNothing()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>it all fits</p>")))
                    + Page("p2", Group(Editable("<p><br/></p>")))
            );
            var target = FirstEditable(dom, "p2");

            Assert.That(FlowTextChains.MoveTailInto(FirstEditable(dom, "p1"), target), Is.False);
            Assert.That(target.ChildNodes.OfType<SafeXmlElement>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void UnlinkFrom_ClearsThisGroupAndTheLaterOnesOnly()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>one</p>"), "chain"))
                    + Page("p2", Group(Editable("<p>two</p>"), "chain"))
                    + Page("p3", Group(Editable("<p>three</p>"), "chain"))
                    + Page("p4", Group(Editable("<p>four</p>"), "chain"))
            );

            var changed = FlowTextChains.UnlinkFrom(dom, "chain", "p3", 0);

            Assert.That(changed.Select(group => group.PageId), Is.EqualTo(new[] { "p3", "p4" }));
            Assert.That(FlowTextChains.GetChainGroups(dom, "chain").Count, Is.EqualTo(2));
        }

        [Test]
        public void UnlinkFrom_LeavingOneGroupBehind_UnlinksThatGroupToo()
        {
            // A chain needs two boxes to be a chain: one box on its own has nowhere to send its
            // extra text and nowhere to get any from.
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>one</p>"), "chain"))
                    + Page("p2", Group(Editable("<p>two</p>"), "chain"))
            );

            FlowTextChains.UnlinkFrom(dom, "chain", "p2", 0);

            Assert.That(FlowTextChains.GetChainGroups(dom, "chain"), Is.Empty);
        }

        [Test]
        public void UnlinkFrom_ClearsTheContinuationAttribute()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>one</p>"), "chain"))
                    + Page(
                        "p2",
                        Group(
                            Editable($"<p {FlowTextChains.kContinuationAttrName}='true'>two</p>"),
                            "chain"
                        )
                    )
            );

            FlowTextChains.UnlinkFrom(dom, "chain", "p2", 0);

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath($"//p[@{FlowTextChains.kContinuationAttrName}]");
        }

        [Test]
        public void ContinueInto_SavesTheSourcePageWithSaveForPageChangedAndNeverSaves()
        {
            SetDom(
                Page("p1", Group(Editable($"<p>this fits{kMarker} and this does not</p>")))
                    + Page("p2", Group(Editable("<p><br/></p>")))
            );
            var book = CreateBook();

            var result = FlowTextChains.ContinueInto(book, "p1", 0, "p2", 0, "en");

            Assert.That(result.MovedAny, Is.True);
            _storage.Verify(
                storage => storage.SaveForPageChanged("p1", It.IsAny<SafeXmlElement>()),
                Times.Once,
                "The source page is the only page C# writes, and it writes it once."
            );
            _storage.Verify(
                storage => storage.Save(),
                Times.Never,
                "A whole-book save would gather the page the browser is showing."
            );
        }

        [Test]
        public void ContinueInto_PutsTheSameChainIdOnBothGroups()
        {
            SetDom(
                Page("p1", Group(Editable($"<p>this fits{kMarker} and this does not</p>")))
                    + Page("p2", Group(Editable("<p><br/></p>")))
            );
            var book = CreateBook();

            var result = FlowTextChains.ContinueInto(book, "p1", 0, "p2", 0, "en");

            Assert.That(result.ChainId, Is.Not.Null.And.Not.Empty);
            Assert.That(
                FlowTextChains.GetChainGroups(book.OurHtmlDom, result.ChainId).Count,
                Is.EqualTo(2)
            );
            Assert.That(
                result.TargetHtmlByLang["en"],
                Does.Contain(FlowTextChains.kContinuationAttrName)
            );
        }

        [Test]
        public void ContinueInto_KeepsTheChainIdTheSourceAlreadyHad()
        {
            SetDom(
                Page("p1", Group(Editable("<p>one</p>"), "chain"))
                    + Page("p2", Group(Editable($"<p>two fits{kMarker} two spills</p>"), "chain"))
                    + Page("p3", Group(Editable("<p><br/></p>")))
            );
            var book = CreateBook();

            var result = FlowTextChains.ContinueInto(book, "p2", 0, "p3", 0, "en");

            Assert.That(result.ChainId, Is.EqualTo("chain"));
            Assert.That(
                FlowTextChains.GetChainGroups(book.OurHtmlDom, "chain").Count,
                Is.EqualTo(3)
            );
        }

        [Test]
        public void ContinueInto_MovesEveryLanguageThatOverflows()
        {
            // Each language's text flows through its own boxes. If one language moved and another
            // stayed, the two would say different things about the same paragraph.
            SetDom(
                Page(
                    "p1",
                    Group(
                        Editable($"<p>english fits{kMarker} english spills</p>")
                            + Editable($"<p>french fits{kMarker} french spills</p>", "fr")
                    )
                ) + Page("p2", Group(Editable("<p><br/></p>") + Editable("<p><br/></p>", "fr")))
            );
            var book = CreateBook();

            var result = FlowTextChains.ContinueInto(book, "p1", 0, "p2", 0, "en");

            Assert.That(result.TargetHtmlByLang.Keys, Is.EquivalentTo(new[] { "en", "fr" }));
            Assert.That(result.TargetHtmlByLang["fr"], Does.Contain("french spills"));
        }

        [Test]
        public void ContinueInto_MissingGroup_ReturnsNull()
        {
            SetDom(Page("p1", Group(Editable($"<p>fits{kMarker} spills</p>"))));
            var book = CreateBook();

            Assert.That(FlowTextChains.ContinueInto(book, "p1", 0, "nosuchpage", 0, "en"), Is.Null);
        }

        /// <summary>
        /// A three-page chain in the state a settled chain is in: only the last box carries the
        /// mark that says where its text ran out, and each box after the first begins with the
        /// tail of the paragraph the box before it started.
        /// </summary>
        private static HtmlDom MakeThreePageChain()
        {
            return MakeBookDom(
                Page("p1", Group(Editable("<p>Alpha one.</p>"), "chain"))
                    + Page(
                        "p2",
                        Group(
                            Editable($"<p{kContinues}{kSeam}>Beta two.</p><p>Gamma three.</p>"),
                            "chain"
                        )
                    )
                    + Page(
                        "p3",
                        Group(
                            Editable(
                                $"<p{kContinues}{kSeam}>Delta four.{kMarker} Epsilon five.</p>"
                            ),
                            "chain"
                        )
                    )
            );
        }

        [Test]
        public void MoveChainedTextOffPage_MiddlePage_PrependsIntoTheNextBoxAndJoinsTheContinuation()
        {
            var dom = MakeThreePageChain();
            // Sanity check the starting point: the last box holds only its own half of the run,
            // and it holds the mark that says where its text ran out.
            Assert.That(ParagraphTexts(FirstEditable(dom, "p3")).Length, Is.EqualTo(1), "setup");
            Assert.That(
                FlowTextChains.FindOverflowMarker(FirstEditable(dom, "p3")),
                Is.Not.Null,
                "setup"
            );

            var moves = FlowTextChains.MoveChainedTextOffPage(dom, PageElement(dom, "p2"));

            var target = FirstEditable(dom, "p3");
            // The middle box's own two paragraphs come first, and the paragraph the last box
            // began with carried on the middle box's last paragraph, so the two are one again.
            Assert.That(
                ParagraphTexts(target),
                Is.EqualTo(new[] { "Beta two.", "Gamma three. Delta four. Epsilon five." })
            );
            var paragraphs = FlowTextChains.GetTopLevelParagraphs(target);
            // The first paragraph still carries on "Alpha one." on the page before it.
            Assert.That(
                paragraphs[0].GetAttribute(FlowTextChains.kContinuationAttrName),
                Is.EqualTo("true")
            );
            Assert.That(
                paragraphs[1].HasAttribute(FlowTextChains.kContinuationAttrName),
                Is.False,
                "The joined paragraph continues nothing: it is whole again."
            );
            Assert.That(
                FlowTextChains.FindOverflowMarker(target),
                Is.Null,
                "The mark described a fit of text that has changed; a walk puts it back."
            );
            Assert.That(moves.Count, Is.EqualTo(1));
            Assert.That(moves[0].ChainId, Is.EqualTo("chain"));
            Assert.That(moves[0].WalkFromPageId, Is.EqualTo("p3"));
            Assert.That(moves[0].Langs, Is.EqualTo(new[] { "en" }));
        }

        [Test]
        public void MoveChainedTextOffPage_LastPage_AppendsToThePreviousBox()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>Alpha one.</p>"), "chain"))
                    + Page(
                        "p2",
                        Group(
                            Editable($"<p{kContinues}>Beta two.</p><p>Gamma three.{kMarker}</p>"),
                            "chain"
                        )
                    )
                    + Page(
                        "p3",
                        Group(
                            Editable($"<p{kContinues}{kSeam}>Delta four.</p><p>Epsilon five.</p>"),
                            "chain"
                        )
                    )
            );
            // Sanity check the starting point: the box that will receive the text holds two
            // paragraphs of its own.
            Assert.That(ParagraphTexts(FirstEditable(dom, "p2")).Length, Is.EqualTo(2), "setup");

            var moves = FlowTextChains.MoveChainedTextOffPage(dom, PageElement(dom, "p3"));

            var target = FirstEditable(dom, "p2");
            Assert.That(
                ParagraphTexts(target),
                Is.EqualTo(new[] { "Beta two.", "Gamma three. Delta four.", "Epsilon five." })
            );
            Assert.That(FlowTextChains.FindOverflowMarker(target), Is.Null);
            Assert.That(moves[0].WalkFromPageId, Is.EqualTo("p2"));
            Assert.That(moves[0].Langs, Is.EqualTo(new[] { "en" }));
        }

        [Test]
        public void MoveChainedTextOffPage_OnlyBoxOfTheChain_MovesNothing()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>Alpha one.</p>"), "lonelyChain"))
                    + Page("p2", Group(Editable("<p>Not in a chain.</p>")))
            );

            var moves = FlowTextChains.MoveChainedTextOffPage(dom, PageElement(dom, "p1"));

            // There is nowhere to put the text, so it goes with the page, as it did before flow
            // text existed.
            Assert.That(
                ParagraphTexts(FirstEditable(dom, "p2")),
                Is.EqualTo(new[] { "Not in a chain." })
            );
            Assert.That(
                ParagraphTexts(FirstEditable(dom, "p1")),
                Is.EqualTo(new[] { "Alpha one." })
            );
            Assert.That(moves.Count, Is.EqualTo(1));
            Assert.That(moves[0].WalkFromPageId, Is.Null);
            Assert.That(moves[0].Langs, Is.Empty);
        }

        [Test]
        public void MoveChainedTextOffPage_LeavingOneBoxBehind_UnlinksItAndAsksForNoWalk()
        {
            // A chain needs two boxes to be a chain: one box on its own has nowhere to send its
            // extra text and nothing to refit.
            var dom = MakeBookDom(
                Page("p1", Group(Editable($"<p>Alpha one.</p>"), "chain"))
                    + Page("p2", Group(Editable($"<p{kContinues}{kSeam}>Beta two.</p>"), "chain"))
            );

            var moves = FlowTextChains.MoveChainedTextOffPage(dom, PageElement(dom, "p2"));

            Assert.That(
                ParagraphTexts(FirstEditable(dom, "p1")),
                Is.EqualTo(new[] { "Alpha one. Beta two." })
            );
            // The page being deleted still carries the attribute, and goes with it; the box that
            // stays behind is on its own now, so it is no longer part of a chain.
            Assert.That(
                FlowTextChains.GetChainGroups(dom, "chain").Select(group => group.PageId),
                Is.EqualTo(new[] { "p2" })
            );
            Assert.That(moves[0].WalkFromPageId, Is.Null);
        }

        [Test]
        public void MoveChainedTextOffPage_MovesEveryLanguageOfTheGroup()
        {
            // Each language's text flows through its own boxes. A language left behind would lose
            // its text while another kept it.
            var dom = MakeBookDom(
                Page(
                    "p1",
                    Group(
                        Editable("<p>Alpha one.</p>") + Editable("<p>Alpha un.</p>", "fr"),
                        "chain"
                    )
                )
                    + Page(
                        "p2",
                        Group(
                            Editable($"<p{kContinues}{kSeam}>Beta two.</p>")
                                + Editable($"<p{kContinues}{kSeam}>Beta deux.</p>", "fr"),
                            "chain"
                        )
                    )
                    + Page(
                        "p3",
                        Group(
                            Editable($"<p{kContinues}{kSeam}>Gamma three.</p>")
                                + Editable($"<p{kContinues}{kSeam}>Gamma trois.</p>", "fr"),
                            "chain"
                        )
                    )
            );

            var moves = FlowTextChains.MoveChainedTextOffPage(dom, PageElement(dom, "p2"));

            Assert.That(
                ParagraphTexts(EditableOfLang(dom, "p3", "en")),
                Is.EqualTo(new[] { "Beta two. Gamma three." })
            );
            Assert.That(
                ParagraphTexts(EditableOfLang(dom, "p3", "fr")),
                Is.EqualTo(new[] { "Beta deux. Gamma trois." })
            );
            Assert.That(moves[0].Langs, Is.EquivalentTo(new[] { "en", "fr" }));
        }

        [Test]
        public void MoveChainedTextOffPage_PageWithNoChainedGroup_ChangesNothing()
        {
            var dom = MakeBookDom(
                Page("p1", Group(Editable("<p>Alpha one.</p>"), "chain"))
                    + Page("p2", Group(Editable("<p>On its own.</p>")))
                    + Page("p3", Group(Editable($"<p{kContinues}>Beta two.</p>"), "chain"))
            );
            var before = dom.RawDom.OuterXml;

            var moves = FlowTextChains.MoveChainedTextOffPage(dom, PageElement(dom, "p2"));

            Assert.That(moves, Is.Empty);
            Assert.That(dom.RawDom.OuterXml, Is.EqualTo(before));
        }

        /// <summary>
        /// The group of the page's first translation group, which is the one a chain runs through
        /// in these tests.
        /// </summary>
        private static SafeXmlElement FirstGroup(HtmlDom dom, string pageId)
        {
            return dom.RawDom.SelectSingleNode(
                    $"//div[@id='{pageId}']//div[contains(@class,'bloom-translationGroup')]"
                ) as SafeXmlElement;
        }

        [Test]
        public void PageHoldsNothingBut_EmptyBoxAndNothingElse_IsTrue()
        {
            var dom = MakeBookDom(Page("p1", Group(Editable("<p><br /></p>"), "chain")));
            // Sanity check: the box really is empty before we ask.
            Assert.That(FlowTextChains.HasVisibleText(FirstEditable(dom, "p1")), Is.False);

            Assert.That(
                FlowTextChains.PageHoldsNothingBut(PageElement(dom, "p1"), FirstGroup(dom, "p1")),
                Is.True
            );
        }

        [Test]
        public void PageHoldsNothingBut_AnotherGroupHasText_IsFalse()
        {
            var dom = MakeBookDom(
                Page(
                    "p1",
                    Group(Editable("<p><br /></p>"), "chain") + Group(Editable("<p>A caption.</p>"))
                )
            );

            Assert.That(
                FlowTextChains.PageHoldsNothingBut(PageElement(dom, "p1"), FirstGroup(dom, "p1")),
                Is.False,
                "Text in any other box on the page would go with the page."
            );
        }

        [Test]
        public void PageHoldsNothingBut_RealPicture_IsFalse()
        {
            var dom = MakeBookDom(
                Page(
                    "p1",
                    "<div class='bloom-imageContainer'><img src='aor_Cat1.png'/></div>"
                        + Group(Editable("<p><br /></p>"), "chain")
                )
            );

            Assert.That(
                FlowTextChains.PageHoldsNothingBut(PageElement(dom, "p1"), FirstGroup(dom, "p1")),
                Is.False,
                "A picture the author chose would go with the page."
            );
        }

        [Test]
        public void PageHoldsNothingBut_PlaceholderPicture_IsTrue()
        {
            var dom = MakeBookDom(
                Page(
                    "p1",
                    "<div class='bloom-imageContainer'><img src='placeHolder.png'/></div>"
                        + Group(Editable("<p><br /></p>"), "chain")
                )
            );

            Assert.That(
                FlowTextChains.PageHoldsNothingBut(PageElement(dom, "p1"), FirstGroup(dom, "p1")),
                Is.True,
                "The placeholder stands for a picture nobody has chosen yet."
            );
        }

        [Test]
        public void PageHoldsNothingBut_Video_IsFalse()
        {
            var dom = MakeBookDom(
                Page(
                    "p1",
                    "<div class='bloom-videoContainer'><video><source src='video/one.mp4'/></video></div>"
                        + Group(Editable("<p><br /></p>"), "chain")
                )
            );

            Assert.That(
                FlowTextChains.PageHoldsNothingBut(PageElement(dom, "p1"), FirstGroup(dom, "p1")),
                Is.False,
                "A video the author recorded would go with the page."
            );
        }
    }
}
