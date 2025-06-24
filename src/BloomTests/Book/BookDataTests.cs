using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web;
using System.Windows.Input;
using System.Xml;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Edit;
using Bloom.SafeXml;
using L10NSharp;
using NUnit.Framework;
using SIL.IO;
using SIL.Reporting;
using SIL.TestUtilities;
using SIL.Text;
using SIL.Windows.Forms.ClearShare;
using Bloom.SubscriptionAndFeatures;

// TODO (default name BL-13703) currently, the Tag setter also automatically sets the name using LibPalasso logic.
// If we make changes to that logic now that we are changing default names with the new
// language chooser, we need to check through the tests in this file

namespace BloomTests.Book
{
    // Tests of BookData, especially SynchronizeDataItemsThroughoutDOM and friends.
    // When testing these, it's important to note that attribute values, including id, are
    // now copied to output elements. So be careful about using id to get an element
    // you want to test in the result. It may be better to use a unique attribute name.
    // However, any attribute on the input element will get copied to the output unless forbidden.
    [TestFixture]
    public sealed class BookDataTests
    {
        private CollectionSettings _collectionSettings;
        private ILocalizationManager _localizationManager;
        private ILocalizationManager _palasoLocalizationManager;

        [SetUp]
        public void Setup()
        {
            _collectionSettings = new CollectionSettings(
                new NewCollectionSettings()
                {
                    PathToSettingsFile = CollectionSettings.GetPathForNewSettings(
                        new TemporaryFolder("BookDataTests").Path,
                        "test"
                    ),
                }
            );
            _collectionSettings.Language1.Tag = "xyz";
            _collectionSettings.Language2.Tag = "en";
            _collectionSettings.Language3.Tag = "fr";
            ErrorReport.IsOkToInteractWithUser = false;

            LocalizationManager.UseLanguageCodeFolders = true;
            var localizationDirectory =
                FileLocationUtilities.GetDirectoryDistributedWithApplication("localization");
            _localizationManager = LocalizationManager.Create(
                TranslationMemory.XLiff,
                "fr",
                "Bloom",
                "Bloom",
                "1.0.0",
                localizationDirectory,
                "SIL/Bloom",
                null,
                ""
            );
            _palasoLocalizationManager = LocalizationManager.Create(
                TranslationMemory.XLiff,
                "fr",
                "Palaso",
                "Palaso",
                "1.0.0",
                localizationDirectory,
                "SIL/Palaso",
                null,
                ""
            );
        }

        [TearDown]
        public void TearDown()
        {
            _localizationManager.Dispose();
            _palasoLocalizationManager.Dispose();
            LocalizationManager.ForgetDisposedManagers();
        }

        [Test]
        public void TextOfInnerHtml_RemovesMarkup()
        {
            var input = "This <em>is</em> the day";
            var output = BookData.TextOfInnerHtml(input);
            Assert.That(output, Is.EqualTo("This is the day"));
        }

        [Test]
        public void TextOfInnerHtml_HandlesSpansProperly()
        {
            var input =
                "<p><strong><span class='x'>01.</span></strong> <span class='y'>The Creation</span></p>";
            var output = BookData.TextOfInnerHtml(input);
            Assert.That(output, Is.EqualTo("01. The Creation"));
        }

        [Test]
        public void TextOfInnerHtml_HandlesLinebreakSpanWithNoByteOrderMarkProperly()
        {
            // Markup should be removed, linebreak replaced with \n, whitespace should be trimmed.
            var input =
                "<p>Enter</p> <p>Shift-Enter<span class=\"bloom-linebreak\"></span>Last Line </p>";
            var output = BookData.TextOfInnerHtml(input);
            Assert.That(output, Is.EqualTo("Enter Shift-Enter\nLast Line"));
        }

        [Test]
        public void TextOfInnerHtml_HandlesLinebreakSpanWithByteOrderMarkProperly()
        {
            // Markup should be removed, linebreak replaced with \n, byte order mark should be removed, whitespace should be trimmed.
            var input =
                "<p>Enter</p> <p>Shift-Enter<span class=\"bloom-linebreak\"></span>﻿Last Line </p>";
            var output = BookData.TextOfInnerHtml(input);
            Assert.That(output, Is.EqualTo("Enter Shift-Enter\nLast Line"));
        }

        [Test]
        public void TextOfInnerHtml_HandlesXmlEscapesCorrectly()
        {
            var input =
                "Jack &amp; Jill like xml sequences like &amp;amp; &amp; &amp;lt; &amp; &amp;gt; for characters like &lt;&amp;&gt;";
            var output = BookData.TextOfInnerHtml(input);
            Assert.That(
                output,
                Is.EqualTo(
                    "Jack & Jill like xml sequences like &amp; & &lt; & &gt; for characters like <&>"
                )
            );
        }

        [Test]
        public void BookStarter_ClearUnneededOriginalContentFromDerivative_ClearsOutNonDerivativeData()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
					<div data-book='printingInfo' lang='es'><p>First Edition 2020<br />Second Edition 2023</p></div>
					<div data-book='randomOtherInfo' lang='es'>Something weird here</div>
					<div data-book='newNonDerivField' lang='en'>Something that oughta be removed in a new book.</div>
				</div>
				<div class='bloom-page' id='guid1'>
					<div class='bloom-translationGroup bloom-clearWhenMakingDerivative'>
						<div class='bloom-editable' data-book='printingInfo' lang='es'><p>First Edition 2020<br />Second Edition 2023</p></div>
					</div>
				</div>
				<div class='bloom-page' id='guid2'>
					<div class='bloom-translationGroup bloom-clearWhenMakingDerivative'>
						<div class='bloom-editable' data-book='printingInfo' lang='es'><p>First Edition 2020<br />Second Edition 2023</p></div>
						<div class='bloom-editable' data-book='printingInfo' lang='z'></div>
						<div class='bloom-editable' data-book='printingInfo' lang='en'>English here</div>
					</div>
					<div class='bloom-translationGroup'><!-- No 'clear out derivative stuff' class here. -->
						<div class='bloom-editable' data-book='randomOtherInfo' lang='es'>Something weird here</div>
						<div class='bloom-editable' data-book='randomOtherInfo' lang='z'></div>
						<div class='bloom-editable' data-book='randomOtherInfo' lang='en'>Some other stuff.</div>
					</div>
					<div class='bloom-translationGroup bloom-clearWhenMakingDerivative'>
						<div class='bloom-editable' data-book='newNonDerivField' lang='es'>Something that oughta be removed in a new book.</div>
						<div class='bloom-editable' data-book='newNonDerivField' lang='z'></div>
					</div>
				</div>
			 </body></html>"
            );
            var bookData = new BookData(dom, _collectionSettings, null);

            // SUT
            BookStarter.ClearUnneededOriginalContentFromDerivative(dom, bookData);

            // Test the remaining BookData items
            Assert.That(
                bookData.GetMultiTextVariableOrEmpty("printingInfo").ContainsAlternative("es"),
                Is.False
            );
            Assert.That(
                bookData.GetMultiTextVariableOrEmpty("randomOtherInfo").ContainsAlternative("es"),
                Is.True
            );
            Assert.That(
                bookData.GetMultiTextVariableOrEmpty("randomOtherInfo").ContainsAlternative("en"),
                Is.True
            );
            Assert.That(
                bookData.GetMultiTextVariableOrEmpty("newNonDerivField").ContainsAlternative("es"),
                Is.False
            );
            Assert.That(
                bookData.GetMultiTextVariableOrEmpty("newNonDerivField").ContainsAlternative("en"),
                Is.False
            );
        }

        [Test]
        public void GatherDataItemsFromXElement_EmptyBeforeContent_SavesAsDeletedOnly()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
					<div data-book='bookTitle' lang='dcc'>DccTitle</div>
				</div>
				<div class='bloom-page' id='guid2'>
					<div data-book='bookTitle' lang='dcc'>DccTitle</div>
					<div data-book='bookTitle' lang='en'><p></p></div>
				</div>
				<div class='bloom-page' id='guid3'>
					<div data-book='bookTitle' lang='dcc'>DccTitle</div>
					<div data-book='bookTitle' lang='en'>This should not be kept</div>
				</div>
			 </body></html>"
            );
            DataSet data = new DataSet();
            var itemsToDelete = new HashSet<Tuple<string, string>>();
            var bookData = new BookData(
                new HtmlDom("<html><body></body></html>"),
                _collectionSettings,
                null
            );
            bookData.GatherDataItemsFromXElement(data, dom.RawDom.DocumentElement, itemsToDelete);
            // Deleted because on the first page we found an effectively empty block with this data-book and lang
            Assert.That(itemsToDelete.Contains(Tuple.Create("bookTitle", "en")));
            // Since we concluded that this key and lang should be deleted processing the first page,
            // we should not have stored the value found on page 2.
            Assert.That(
                data.TextVariables["bookTitle"].TextAlternatives.ContainsAlternative("en"),
                Is.False
            );
        }

        [Test]
        public void GatherDataItemsFromXElement_BackgroundImage_GathersCroppingData()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
                <div id='bloomDataDiv'>
                    
                </div>
                <div class='bloom-page' id='guid2'>
                    <div class=""bloom-canvas bloom-has-canvas-element"" data-imgsizebasedon=""649,231"" >
                        <div class=""bloom-canvas-element bloom-backgroundImage"" data-bubble=""{`version`:`1.0`,`style`:`none`,`tails`:[],`level`:1,`backgroundColors`:[`transparent`],`shadowOffset`:0}"" style=""width: 254.663px; top: 0px; left: 197.168px; height: 231px;"" data-bloom-active=""true"">
                            <div class=""bloom-imageContainer"">
                                <img data-book=""coverImage"" src=""macaw-parrot-sitting-on-a-tree-branch-4c6a4.jpg"" onerror=""this.classList.add('bloom-imageLoadError')"" style=""width: 308.031px; left: 0px; top: -29.3363px;""/>
                            </div>
                        </div>
                    </div>
                </div>
             </body></html>"
            );
            DataSet data = new DataSet();
            var itemsToDelete = new HashSet<Tuple<string, string>>();
            var bookData = new BookData(
                new HtmlDom("<html><body></body></html>"),
                _collectionSettings,
                null
            );

            //SUT
            bookData.GatherDataItemsFromXElement(data, dom.RawDom.DocumentElement, itemsToDelete);

            Assert.That(
                data.TextVariables.TryGetValue("coverImage", out DataSetElementValue dsv),
                Is.True
            );
            var attributes = dsv.GetAttributeList("*");
            Assert.That(attributes, Is.Not.Null);
            var imgStyle = attributes.FirstOrDefault(x => x.Item1 == "style");
            Assert.That(imgStyle, Is.Not.Null);
            Assert.That(
                imgStyle.Item2.Unencoded,
                Is.EqualTo("width: 308.031px; left: 0px; top: -29.3363px;")
            );
            var ceStyle = attributes.FirstOrDefault(x => x.Item1 == "data-canvas-element-style");
            Assert.That(ceStyle, Is.Not.Null);
            Assert.That(
                ceStyle.Item2.Unencoded,
                Is.EqualTo("width: 254.663px; top: 0px; left: 197.168px; height: 231px;")
            );
            var sizeBasedOnDefault = attributes.FirstOrDefault(
                x => x.Item1 == "data-canvas-imgsizebasedon"
            );
            Assert.That(sizeBasedOnDefault, Is.Not.Null);
            Assert.That(sizeBasedOnDefault.Item2.Unencoded, Is.EqualTo("649,231"));
        }

        [Test]
        public void UpdateImageFromDataSet_CreatesBackgroundCanvasElement()
        {
            DataSet data = new DataSet();
            var htmlDom = new HtmlDom(
                @"
<html><body>
    <div id='bloomDataDiv'>
    </div>
    <div class='bloom-page' id='guid2'>
        <div class=""bloom-canvas"" >
            <img data-book=""coverImage"" src=""placeHolder.png""/>
        </div>
    </div>

</body></html>"
            );
            var bookData = new BookData(htmlDom, _collectionSettings, null);
            var dsv = new DataSetElementValue(new MultiTextBase(), false);
            data.TextVariables["coverImage"] = dsv;
            var attributes = new List<Tuple<string, XmlString>>
            {
                Tuple.Create(
                    "style",
                    XmlString.FromUnencoded("width: 308.031px; left: 0px; top: -29.3363px;")
                ),
                Tuple.Create(
                    "data-canvas-element-style",
                    XmlString.FromUnencoded(
                        "width: 254.663px; top: 0px; left: 197.168px; height: 231px;"
                    )
                ),
                Tuple.Create("data-canvas-imgsizebasedon", XmlString.FromUnencoded("649,231"))
            };
            dsv.SetAttributeList("*", attributes);
            var imgNode =
                htmlDom.RawDom.SelectSingleNode(
                    "//div[@class='bloom-canvas']//img[@data-book='coverImage']"
                ) as SafeXmlElement;
            bookData.UpdateImageFromDataSet(data, imgNode, "coverImage");
            Assert.That(
                imgNode.GetAttribute("style"),
                Is.EqualTo("width: 308.031px; left: 0px; top: -29.3363px;")
            );
            var imageContainer = imgNode.ParentElement;
            Assert.That(imageContainer.GetAttribute("class"), Is.EqualTo("bloom-imageContainer"));
            var ce = imageContainer.ParentElement;
            Assert.That(ce, Is.Not.Null);
            Assert.That(ce.HasClass(HtmlDom.kCanvasElementClass), Is.True);
            Assert.That(ce.HasClass(HtmlDom.kBackgroundImageClass), Is.True);
            Assert.That(
                ce.GetAttribute("style"),
                Is.EqualTo("width: 254.663px; top: 0px; left: 197.168px; height: 231px;")
            );
            var bloomCanvas = ce.ParentElement;
            Assert.That(bloomCanvas, Is.Not.Null);
            Assert.That(bloomCanvas.HasClass("bloom-canvas"), Is.True);
            Assert.That(bloomCanvas.HasClass("bloom-has-canvas-element"), Is.True);
            Assert.That(bloomCanvas.GetAttribute("data-imgsizebasedon"), Is.EqualTo("649,231"));
        }

        [Test]
        public void MakeLanguageUploadData_FindsDefaultInfo()
        {
            var bookData = new BookData(
                new HtmlDom("<html><body></body></html>"),
                _collectionSettings,
                null
            );
            var results = bookData.MakeLanguageUploadData(new[] { "en", "tpi", "xy3" });
            Assert.That(results.Length, Is.EqualTo(3), "should get one result per input");
            VerifyLangData(results[0], "en", "English", "eng");
            VerifyLangData(results[1], "tpi", "Tok Pisin", "tpi");
            VerifyLangData(results[2], "xy3", "xy3", "xy3");
        }

        [Test]
        public void MakeLanguageUploadData_FindsOverriddenNames()
        {
            var bookData = new BookData(
                new HtmlDom("<html><body></body></html>"),
                _collectionSettings,
                null
            );
            _collectionSettings.Language1.SetName("Cockney", true);
            // Note: no current way of overriding others; verify they aren't changed.
            var results = bookData.MakeLanguageUploadData(new[] { "en", "tpi", "xyz" });
            Assert.That(results.Length, Is.EqualTo(3), "should get one result per input");
            VerifyLangData(results[0], "en", "English", "eng");
            VerifyLangData(results[1], "tpi", "Tok Pisin", "tpi");
            VerifyLangData(results[2], "xyz", "Cockney", "xyz");
        }

        void VerifyLangData(LanguageDescriptor lang, string code, string name, string ethCode)
        {
            Assert.That(lang.LangTag, Is.EqualTo(code));
            Assert.That(lang.Name, Is.EqualTo(name));
            Assert.That(lang.EthnologueCode, Is.EqualTo(ethCode));
        }

        [Test]
        public void SuckInDataFromEditedDom_NoDataDivTitleChanged_NewTitleInCache()
        {
            HtmlDom bookDom = new HtmlDom(
                @"<html ><head></head><body>
				<div class='bloom-page' id='guid2'>
					<textarea lang='xyz' data-book='bookTitle'>original</textarea>
				</div>
			 </body></html>"
            );
            var data = new BookData(bookDom, _collectionSettings, null);
            Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz").Xml);

            HtmlDom editedPageDom = new HtmlDom(
                @"<html ><head></head><body>
				<div class='bloom-page' id='guid2'>
					<textarea lang='xyz' data-book='bookTitle'>changed</textarea>
				</div>
			 </body></html>"
            );

            var info = new BookInfo(_collectionSettings.FolderPath, true);

            data.SuckInDataFromEditedDom(editedPageDom, info);

            Assert.AreEqual("changed", data.GetVariableOrNull("bookTitle", "xyz").Xml);
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='originalTitle' and text()='changed']",
                    1
                );
            Assert.That(info.OriginalTitle, Is.EqualTo("changed"));
        }

        [TestCase("new title")]
        [TestCase("HTML Lesson 1: <strong> & <em> tags")] // Test '&', '<', '>' chars
        public void SuckInDataFromEditedDomNew_NotDerivativeNoCurrent_SetsOriginalTitleToVernacular(
            string unencodedTitle
        )
        {
            string encodedTitle = HttpUtility.HtmlEncode(unencodedTitle);
            string titleXml =
                $"<p><span id=\"abcdefg\" class=\"audio-sentence\">{encodedTitle}</span></p>";
            HtmlDom bookDom = new HtmlDom(
                $@"<html ><head></head><body>
				<div class='bloom-page' id='guid2'>
					<div lang='xyz' data-book='bookTitle'>{titleXml}</div>
				</div>
			 </body></html>"
            );
            var data = new BookData(bookDom, _collectionSettings, null);
            var info = new BookInfo(_collectionSettings.FolderPath, true);

            data.SuckInDataFromEditedDom(bookDom, info);

            Assert.That(
                data.GetVariableOrNull("originalTitle", "*").Xml,
                Is.EqualTo(encodedTitle),
                "Data should return the InnerXML (NOT the InnerText) of the title"
            );
            Assert.That(
                bookDom
                    .SelectSingleNode("//div[@id='bloomDataDiv']/div[@data-book='originalTitle']")
                    .InnerXml,
                Is.EqualTo(encodedTitle)
            );
            Assert.That(info.OriginalTitle, Is.EqualTo(unencodedTitle));
        }

        [TestCase("new title")]
        [TestCase("HTML Lesson 1: <strong> & <em> tags")] // Test '&', '<', '>' chars
        public void SuckInDataFromEditedDom_Derivative_NoOriginalTitle_LeavesOriginalTitleEmpty(
            string unencodedTitle
        )
        {
            string encodedTitle = HttpUtility.HtmlEncode(unencodedTitle);
            HtmlDom bookDom = new HtmlDom(
                $@"<html ><head></head><body>
				<div id='bloomDataDiv'>
					<div data-book='originalCopyright' lang='*'>Copyright 2020 someone</div>
				</div>
				<div class='bloom-page' id='guid2'>
					<div lang='xyz' data-book='bookTitle'>{encodedTitle}</div>
				</div>
			 </body></html>"
            );
            var data = new BookData(bookDom, _collectionSettings, null);

            data.SuckInDataFromEditedDom(bookDom);

            Assert.That(data.GetVariableOrNull("originalTitle", "*").Xml, Is.Null);
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='originalTitle']");
        }

        [TestCase("hand-edited title")]
        [TestCase("HTML Lesson 1: <strong> & <em> tags")] // Test '&', '<', '>' chars
        public void SuckInDataFromEditedDom_Derivative_OriginalTitle_CopiesToMetadata(
            string unencodedTitle
        )
        {
            string encodedTitle = HttpUtility.HtmlEncode(unencodedTitle);
            HtmlDom bookDom = new HtmlDom(
                $@"<html ><head></head><body>
				<div id='bloomDataDiv'>
					<div data-book='originalCopyright' lang='*'>Copyright 2020 someone</div>
					<div data-book='originalTitle' lang='*'>{encodedTitle}</div>
				</div>
				<div class='bloom-page' id='guid2'>
					<div lang='xyz' data-book='bookTitle'>new title</div>
				</div>
			 </body></html>"
            );
            var data = new BookData(bookDom, _collectionSettings, null);
            var info = new BookInfo(_collectionSettings.FolderPath, true);

            data.SuckInDataFromEditedDom(bookDom, info);

            Assert.That(
                data.GetVariableOrNull("originalTitle", "*").Xml,
                Is.EqualTo(encodedTitle),
                "data should equal the encoded title"
            );
            Assert.That(
                info.OriginalTitle,
                Is.EqualTo(unencodedTitle),
                "Info.OriginalTitle should equal the decoded title"
            );
        }

        [TestCase("new title")]
        [TestCase("HTML Lesson 1: <strong> & <em> tags")] // Test '&', '<', '>' chars
        public void SuckInDataFromEditedDom_NotDerivativeCurrent_SetsOriginalTitleToVernacular(
            string unencodedTitle
        )
        {
            string encodedTitle = HttpUtility.HtmlEncode(unencodedTitle);
            HtmlDom bookDom = new HtmlDom(
                $@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='originalTitle' lang='*'>original</div>
				</div>
				<div class='bloom-page' id='guid2'>
					<div lang='en' data-book='bookTitle'>new title in English</div>
					<div lang='xyz' data-book='bookTitle'>{encodedTitle}</div>
					<div lang='fr' data-book='bookTitle'>new title in French</div>
				</div>
			 </body></html>"
            );
            var data = new BookData(bookDom, _collectionSettings, null);

            data.SuckInDataFromEditedDom(bookDom);

            Assert.That(
                data.GetVariableOrNull("originalTitle", "*").Xml,
                Is.EqualTo(encodedTitle),
                "data should equal the encoded title"
            );
        }

        /// <summary>
        /// Regression test: the difference between this situation (had a value before) and the one where this is newly discovered was the source of a bug
        /// </summary>
        [Test]
        public void SuckInDataFromEditedDom_HasDataDivWithOldTitleThenTitleChanged_NewTitleInCache()
        {
            HtmlDom bookDom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
				</div>
				<div class='bloom-page' id='guid2'>
					<textarea lang='xyz' data-book='bookTitle'>original</textarea>
				</div>
			 </body></html>"
            );

            var data = new BookData(bookDom, _collectionSettings, null);
            Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz").Xml);

            HtmlDom editedPageDom = new HtmlDom(
                @"<html ><head></head><body>
				<div class='bloom-page' id='guid2'>
					<textarea lang='xyz' data-book='bookTitle'>changed</textarea>
				</div>
			 </body></html>"
            );

            data.SuckInDataFromEditedDom(editedPageDom);

            Assert.AreEqual("changed", data.GetVariableOrNull("bookTitle", "xyz").Xml);
        }

        // This simulates a case where the English title has been deleted on the cover.
        // It should get deleted everywhere.
        [Test]
        public void SuckInDataFromEditedDom_TitleRemovedFromEditedDom_RemovesEverywhere()
        {
            HtmlDom bookDom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
					<div data-book='bookTitle' lang='dcc'>DccTitle</div>
					<div data-book='bookTitle' lang='en'>EnTitle</div>
				</div>
				<div class='bloom-page cover' id='guid4'>
					<div class='bloom-translationGroup'>
						<div class='bloom-editable' data-book='bookTitle' lang='dcc'>DccTitle</div>
						<div class='bloom-editable' data-book='bookTitle' lang='en'>EnTitle</div>
					</div>
				</div>
				<div class='bloom-page titlePage' id='guid5'>
					<div class='bloom-translationGroup'>
						<div class='bloom-editable' data-book='bookTitle' lang='dcc'>DccTitle</div>
						<div class='bloom-editable' data-book='bookTitle' lang='en'>EnTitle</div>
					</div>
				</div>
				</body></html>"
            );

            var data = new BookData(bookDom, _collectionSettings, null);

            HtmlDom editedPageDom = new HtmlDom(
                @"<html ><head></head><body>
				<div class='bloom-page cover' id='guid4'>
					<div class='bloom-translationGroup'>
						<div class='bloom-editable' data-book='bookTitle' lang='dcc'>DccTitle</div>
						<div class='bloom-editable' data-book='bookTitle' lang='en'><p></p></div>
					</div>
				</div>
			 </body></html>"
            );

            data.SuckInDataFromEditedDom(editedPageDom);

            var assertThatHtml = AssertThatXmlIn.Dom(bookDom.RawDom);
            assertThatHtml.HasNoMatchForXpath(
                "//div[@data-book='bookTitle' and contains(text(), 'EnTitle')]"
            );
            assertThatHtml.HasSpecifiedNumberOfMatchesForXpath(
                "//div[@data-book='bookTitle' and contains(text(), 'DccTitle')]",
                3
            );
        }

        // BRANDING-RELATED TESTS

        [Test]
        public void MergeBrandingSettings_NoSettings_DoesNothing()
        {
            HtmlDom bookDom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
				</div>
			 </body></html>"
            );

            var data = new BookData(bookDom, _collectionSettings, null);
            Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz").Xml);
            data.MergeBrandingSettings("nonsense");
            Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz").Xml);
        }

        [Test]
        public void MergeBrandingSettings_SettingsExistsButIsEmpty_DoesNothing()
        {
            HtmlDom bookDom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
				</div>
			 </body></html>"
            );
            using (
                var tempFolder = new TemporaryFolder(
                    "MergeBrandingSettings_SettingsExistsButIsEmpty_DoesNothing"
                )
            )
            {
                File.WriteAllText(Path.Combine(tempFolder.Path, "branding.json"), @"{}");

                var data = new BookData(bookDom, _collectionSettings, null);
                Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz").Xml);
                data.MergeBrandingSettings(tempFolder.Path);
                Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz").Xml);
            }
        }

        [Test]
        public void MergeBrandingSettings_SettingsExistsButHasBogusJson_DoesNothing()
        {
            HtmlDom bookDom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
				</div>
			 </body></html>"
            );
            using (
                var tempFolder = new TemporaryFolder(
                    "MergeBrandingSettings_SettingsExistsButHasBogusJson_DoesNothing"
                )
            )
            {
                File.WriteAllText(Path.Combine(tempFolder.Path, "branding.json"), "");

                var data = new BookData(bookDom, _collectionSettings, null);
                Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz").Xml);
                data.MergeBrandingSettings(tempFolder.Path);
                Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz").Xml);
            }
        }

        [Test]
        public void MergeBrandingSettings_SettingsExistsButLacksCondition_DoesNothing()
        {
            HtmlDom bookDom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
				</div>
			 </body></html>"
            );
            using (
                var tempFolder = new TemporaryFolder(
                    "MergeBrandingSettings_SettingsExistsButLacksCondition_DoesNothing"
                )
            )
            {
                File.WriteAllText(
                    Path.Combine(tempFolder.Path, "branding.json"),
                    @"{
	""presets"": [{
		""data-book"": ""insideBackCover"",
		""lang"": ""xyz"",
		""content"": ""stuff from settings""
	}, {
		""data-book"": ""insideBackCover"",
		""lang"": ""en"",
		""content"": ""English stuff from settings""
	},  {
		""data-book"": ""title"",
		""lang"": ""xyz"",
		""content"": ""xyz title"",
		""condition"":""someUnknownCondition""
	}]
}"
                );

                var data = new BookData(bookDom, _collectionSettings, null);
                data.MergeBrandingSettings(tempFolder.Path);
                Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz").Xml);
                Assert.That(data.GetVariableOrNull("insideBackCover", "en").Xml, Is.Null);
                Assert.That(data.GetVariableOrNull("title", "xyz").Xml, Is.Null);
            }
        }

        [Test]
        public void MergeBrandingSettings_UpdatesEmptyField()
        {
            HtmlDom bookDom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
						<div data-book='insideBackCover' lang='xyz'></div>
				</div>
			 </body></html>"
            );

            using (var tempFolder = new TemporaryFolder("MergeBrandingSettings_UpdatesEmptyField"))
            {
                File.WriteAllText(
                    Path.Combine(tempFolder.Path, "branding.json"),
                    // First item tests successful setting;
                    // Second tests setting of another language of same property;
                    // Third tests setting of another property;
                    // Fourth tests overwrite prevented by existing value;
                    // Remaining ones should be ignored, present to verify this.
                    @"{
	""presets"": [{
		""data-book"": ""insideBackCover"",
		""lang"": ""xyz"",
		""content"": ""stuff from settings"",
		""condition"":""ifEmpty""
	}, {
		""data-book"": ""insideBackCover"",
		""lang"": ""en"",
		""content"": ""English stuff from settings"",
		""condition"":""ifEmpty""
	},  {
		""data-book"": ""title"",
		""lang"": ""xyz"",
		""content"": ""xyz title"",
		""condition"":""ifEmpty""
	}, {
		""data-book"": ""bookTitle"",
		""lang"": ""xyz"",
		""content"": ""stuff from settings that should not be used"",
		""condition"":""ifEmpty""
	}, {
		""lang"": ""xyz"",
		""content"": ""stuff from settings that should not be used"",
		""condition"":""ifEmpty""
	}, {
		""data-book"": ""bookTitle"",
		""content"": ""stuff from settings that should not be used"",
		""condition"":""ifEmpty""
	}, {
		""data-book"": ""bookTitle"",
		""lang"": ""xyz"",
		""condition"":""ifEmpty""
	}, {
		""data-book"": "" "",
		""lang"": ""xyz"",
		""content"": ""stuff from settings that should not be used"",
		""condition"":""ifEmpty""
	}]
}"
                );
                var data = new BookData(bookDom, _collectionSettings, null);
                data.MergeBrandingSettings(tempFolder.Path);

                Assert.AreEqual(
                    "stuff from settings",
                    data.GetVariableOrNull("insideBackCover", "xyz").Xml
                );
                Assert.AreEqual(
                    "English stuff from settings",
                    data.GetVariableOrNull("insideBackCover", "en").Xml
                );
                Assert.AreEqual("xyz title", data.GetVariableOrNull("title", "xyz").Xml);
                Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz").Xml);
            }
        }

        [Test]
        public void MergeBrandingSettings_OverridesIfSpecified()
        {
            HtmlDom bookDom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
						<div data-book='insideBackCover' lang='xyz'>original back cover</div>
				</div>
			 </body></html>"
            );

            using (var tempFolder = new TemporaryFolder("MergeBrandingSettings_UpdatesEmptyField"))
            {
                File.WriteAllText(
                    Path.Combine(tempFolder.Path, "branding.json"),
                    // First item tests successful setting;
                    // Second tests setting of another language of same property;
                    @"{
	""presets"": [{
		""data-book"": ""insideBackCover"",
		""lang"": ""xyz"",
		""content"": ""stuff from settings"",
		""condition"":""always""
}, {
		""data-book"": ""bookTitle"",
		""lang"": ""xyz"",
		""content"": ""stuff from settings that should not be used"",
		""condition"":""ifEmpty""
	}]
}"
                );
                var data = new BookData(bookDom, _collectionSettings, null);
                data.MergeBrandingSettings(tempFolder.Path);

                Assert.AreEqual(
                    "stuff from settings",
                    data.GetVariableOrNull("insideBackCover", "xyz").Xml
                );
                Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz").Xml);
            }
        }

        [Test]
        public void MergeBrandingSettings_BrandingHasLicenseAndNotesButNotCopyright_MetadataMatches()
        {
            HtmlDom bookDom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
						<div data-book='insideBackCover' lang='xyz'>original back cover</div>
				</div>
			 </body></html>"
            );
            using (
                var tempFolder = new TemporaryFolder(
                    "MergeBrandingSettings_BrandingHasLicenseAndNotesButNotCopyright_MetadataMatches"
                )
            )
            {
                File.WriteAllText(
                    Path.Combine(tempFolder.Path, "branding.json"),
                    // First item tests successful setting;
                    // Second tests setting of another language of same property;
                    @"{
	""presets"": [{
		""data-book"": ""licenseNotes"",
		""lang"": ""*"",
		""content"": ""These are custom notes."",
		""condition"":""ifEmpty""
	}, {
		""data-book"": ""licenseUrl"",
		""lang"": ""*"",
		""content"": ""http://creativecommons.org/licenses/by/3.0/igo/"",
		""condition"":""ifEmpty""
	}]
}"
                );
                var data = new BookData(bookDom, _collectionSettings, null);
                data.MergeBrandingSettings(tempFolder.Path);
                var metadata = BookCopyrightAndLicense.GetMetadata(bookDom, data);
                Assert.AreEqual(
                    "http://creativecommons.org/licenses/by/3.0/igo/",
                    metadata.License.Url
                );
                Assert.AreEqual("These are custom notes.", metadata.License.RightsStatement);
                Assert.That(metadata.CopyrightNotice, Is.Null.Or.Empty);
            }
        }

        [Test]
        public void MergeBrandingSettings_HasCopyrightAndLicenseAndLicenseNotes_MetadataMatches()
        {
            HtmlDom bookDom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
						<div data-book='insideBackCover' lang='xyz'>original back cover</div>
				</div>
			 </body></html>"
            );
            using (
                var tempFolder = new TemporaryFolder(
                    "MergeBrandingSettings_HasCopyrightAndLicenseAndLicenseNotes_MetadataMatches"
                )
            )
            {
                File.WriteAllText(
                    Path.Combine(tempFolder.Path, "branding.json"),
                    // First item tests successful setting;
                    // Second tests setting of another language of same property;
                    @"{
	""presets"": [{
		""data-book"": ""licenseNotes"",
		""lang"": ""*"",
		""content"": ""These are custom notes."",
		""condition"":""ifAllCopyrightEmpty""
	}, {
		""data-book"": ""licenseUrl"",
		""lang"": ""*"",
		""content"": ""http://creativecommons.org/licenses/by/3.0/igo/"",
		""condition"":""ifAllCopyrightEmpty""
	}, {
		""data-book"": ""copyright"",
		""lang"": ""*"",
		""content"": ""Copyright © 2016"",
		""condition"":""ifAllCopyrightEmpty""
	}]
}"
                );
                var data = new BookData(bookDom, _collectionSettings, null);
                data.MergeBrandingSettings(tempFolder.Path);
                var metadata = BookCopyrightAndLicense.GetMetadata(bookDom, data);

                Assert.AreEqual(
                    "http://creativecommons.org/licenses/by/3.0/igo/",
                    metadata.License.Url
                );
                Assert.AreEqual("These are custom notes.", metadata.License.RightsStatement);
                Assert.AreEqual("Copyright © 2016", metadata.CopyrightNotice);
            }
        }

        // This is actually the only REASONABLE way to specify copyrights; these other tests are kinda
        // bogus in having copyright notices that say things like "Copyright © 2016".... that doesn't
        // even say the org, and would incorrectly insert 2016 regardless of the actual year of the book.
        // The following tests a real use.
        [Test]
        public void MergeBrandingSettings_HasJustCopyrightOrg_GetsFullCopyrightForCurrentYear()
        {
            HtmlDom bookDom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
						<div data-book='insideBackCover' lang='xyz'>original back cover</div>
				</div>
			 </body></html>"
            );
            using (
                var tempFolder = new TemporaryFolder(
                    "MergeBrandingSettings_HasJustCopyrightOrg_GetsFullCopyrightForCurrentYear"
                )
            )
            {
                File.WriteAllText(
                    Path.Combine(tempFolder.Path, "branding.json"),
                    // First item tests successful setting;
                    // Second tests setting of another language of same property;
                    @"{
						""presets"": [ {
							""data-book"": ""copyright"",
							""lang"": ""*"",
							""content"": ""Chewtoys International"",
							""condition"":""ifAllCopyrightEmpty""
						}]
					}"
                );
                var data = new BookData(bookDom, _collectionSettings, null);
                data.MergeBrandingSettings(tempFolder.Path);
                var metadata = BookCopyrightAndLicense.GetMetadata(bookDom, data);

                Assert.AreEqual(
                    $"Copyright © {DateTime.Now.Year.ToString()} Chewtoys International",
                    metadata.CopyrightNotice
                );
            }
        }

        // we don't want shell books to get this notice
        [TestCase("copyright", "Copyright © 2012, test")]
        [TestCase("licenseNotes", "Some extra notes")]
        [TestCase("licenseUrl", "http://creativecommons.org/licenses/by-nd/3.0/bynd/")]
        public void MergeBrandingSettings_HasCopyrightAlready_CustomBrandingStuffIgnored(
            string dataDivName,
            string dataDivContent
        )
        {
            HtmlDom bookDom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='"
                    + dataDivName
                    + "' lang='*'>"
                    + dataDivContent
                    + @"</div>
				</div>
			 </body></html>"
            );
            using (
                var tempFolder = new TemporaryFolder(
                    "MergeBrandingSettings_HasCopyrightAlready_CustomBrandingStuffIgnored"
                )
            )
            {
                File.WriteAllText(
                    Path.Combine(tempFolder.Path, "branding.json"),
                    @"{
	""presets"": [{
		""data-book"": ""licenseNotes"",
		""lang"": ""*"",
		""content"": ""These are custom notes."",
		""condition"":""ifAllCopyrightEmpty""
	}, {
		""data-book"": ""licenseUrl"",
		""lang"": ""*"",
		""content"": ""http://creativecommons.org/licenses/by/3.0/igo/"",
		""condition"":""ifAllCopyrightEmpty""
	}, {
		""data-book"": ""copyright"",
		""lang"": ""*"",
		""content"": ""Copyright © 2016"",
		""condition"":""ifAllCopyrightEmpty""
	}]
}"
                );
                var data = new BookData(bookDom, _collectionSettings, null);
                data.MergeBrandingSettings(tempFolder.Path);
                var metadata = BookCopyrightAndLicense.GetMetadata(bookDom, data);
                if (dataDivName == "copyright")
                    Assert.IsTrue(metadata.CopyrightNotice.Contains("2012")); // unchanged from testcase
                else
                    Assert.That(metadata.CopyrightNotice, Is.Null);
                if (dataDivName == "licenseUrl")
                {
                    Assert.That(metadata.License, Is.InstanceOf<CreativeCommonsLicense>());
                    Assert.That(
                        metadata.License.Url,
                        Is.EqualTo("http://creativecommons.org/licenses/by-nd/3.0/bynd/")
                    );
                }
                else if (dataDivName == "copyright")
                {
                    Assert.That(metadata.License, Is.InstanceOf<NullLicense>());
                }
                else
                {
                    Assert.That(metadata.License is CustomLicense);
                }

                if (dataDivName == "licenseNotes")
                    Assert.That(metadata.License.RightsStatement, Is.EqualTo("Some extra notes"));
                else
                    Assert.That(metadata.License.RightsStatement, Is.Null.Or.Empty);
            }
        }

        [Test]
        public void MergeBrandingSettings_CustomLicense_LicenseUrlRemoved()
        {
            HtmlDom bookDom = new HtmlDom(
                @"<html><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='licenseUrl' lang='*'>http://creativecommons.org/licenses/by/4.0/</div>
				</div>
			 </body></html>"
            );
            using (
                var tempFolder = new TemporaryFolder(
                    "MergeBrandingSettings_CustomLicense_LicenseUrlRemoved"
                )
            )
            {
                File.WriteAllText(
                    Path.Combine(tempFolder.Path, "branding.json"),
                    @"{
	""presets"": [{
		""data-book"": ""licenseNotes"",
		""lang"": ""*"",
		""content"": ""My custom license."",
		""condition"":""always""
	}, {
		""data-book"": ""licenseUrl"",
		""lang"": ""*"",
		""content"": """",
		""condition"":""always""
	}]
}"
                );
                var data = new BookData(bookDom, _collectionSettings, null);
                data.MergeBrandingSettings(tempFolder.Path);
                var metadata = BookCopyrightAndLicense.GetMetadata(bookDom, data);

                Assert.That(metadata.License, Is.InstanceOf<CustomLicense>());
                Assert.That(metadata.License.Url, Is.Null.Or.Empty);

                Assert.That(metadata.License.RightsStatement, Is.EqualTo("My custom license."));
            }
        }

        [Test]
        public void SuckInDataFromEditedDom_XmatterPageAttributeAddedToXmatterPage_XmatterPageAttributeAddedToBookDataAndDataDiv()
        {
            HtmlDom bookDom = new HtmlDom(
                @"<html><head></head><body>
				<div id='bloomDataDiv'>
				</div>
				<div class='bloom-page frontCover' id='guid2' data-xmatter-page='frontCover'>
				</div>
			 </body></html>"
            );

            var data = new BookData(bookDom, _collectionSettings, null);
            Assert.IsNull(
                data.GetXmatterPageDataAttributeValue("frontCover", "data-backgroundaudio")
            );
            Assert.IsNull(
                data.GetXmatterPageDataAttributeValue("frontCover", "data-backgroundaudiovolume")
            );

            HtmlDom editedPageDom = new HtmlDom(
                @"<html><head></head><body>
				<div id='bloomDataDiv'>
				</div>
				<div class='bloom-page frontCover' id='guid2' data-xmatter-page='frontCover' data-backgroundaudio='SoundTrack0.mp3' data-backgroundaudiovolume='0.21'>
				</div>
			 </body></html>"
            );

            data.SuckInDataFromEditedDom(editedPageDom);

            Assert.AreEqual(
                "SoundTrack0.mp3",
                data.GetXmatterPageDataAttributeValue("frontCover", "data-backgroundaudio")
            );
            Assert.AreEqual(
                "0.21",
                data.GetXmatterPageDataAttributeValue("frontCover", "data-backgroundaudiovolume")
            );

            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-xmatter-page='frontCover' "
                        + "and @data-backgroundaudio='SoundTrack0.mp3' and @data-backgroundaudiovolume='0.21' and not(text())]",
                    1
                );
        }

        [Test]
        public void SuckInDataFromEditedDom_DataBookElementAddedToXmatterPage_ElementWithAttributesAddedToBookDataAndDataDiv()
        {
            HtmlDom bookDom = new HtmlDom(
                @"<html><head></head><body>
				<div id='bloomDataDiv'>
				</div>
				<div class='bloom-page frontCover' id='guid2'>
				</div>
			 </body></html>"
            );

            var data = new BookData(bookDom, _collectionSettings, null);

            HtmlDom editedPageDom = new HtmlDom(
                @"<html><head></head><body>
				<div class='bloom-page frontCover' id='guid2'>
					<div data-book='coverImageDescription' lang='en' id='aNiceId' data-backgroundaudio='SoundTrack0.mp3' ><p>a bird</p></div>
				</div>
			 </body></html>"
            );

            data.SuckInDataFromEditedDom(editedPageDom);

            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription' "
                        + "and @data-backgroundaudio='SoundTrack0.mp3' and @id='aNiceId']",
                    1
                );
        }

        //[Test] TODO - this test doesn't work because SuckInDataFromEditedDom first updates the page from the data div before updating the data div from the page.
        // I couldn't get it worked out, but the production code does do things correctly. I attempted to add this test for BL-5409.
        public void SuckInDataFromEditedDom_XmatterPageAttributeUpdatedInXmatterPage_XmatterPageAttributeUpdatedInBookDataAndDataDiv()
        {
            HtmlDom bookDom = new HtmlDom(
                @"<html><head></head><body>
				<div id='bloomDataDiv'>
					<div class='bloom-page frontCover' data-xmatter-page='frontCover' data-backgroundaudio='SoundTrack0.mp3' data-backgroundaudiovolume='0.21'>
					</div>
				</div>
				<div class='bloom-page frontCover' id='guid2' data-xmatter-page='frontCover' data-backgroundaudio='SoundTrack0.mp3' data-backgroundaudiovolume='0.21'>
				</div>
			 </body></html>"
            );

            var data = new BookData(bookDom, _collectionSettings, null);
            Assert.AreEqual(
                "SoundTrack0.mp3",
                data.GetXmatterPageDataAttributeValue("frontCover", "data-backgroundaudio")
            );
            Assert.AreEqual(
                "0.21",
                data.GetXmatterPageDataAttributeValue("frontCover", "data-backgroundaudiovolume")
            );

            HtmlDom editedPageDom = new HtmlDom(
                @"<html><head></head><body>
				<div id='bloomDataDiv'>
					<div class='bloom-page frontCover' data-xmatter-page='frontCover' data-backgroundaudio='SoundTrack0.mp3' data-backgroundaudiovolume='0.21'>
					</div>
				</div>
				<div class='bloom-page frontCover' id='guid2' data-xmatter-page='frontCover' data-backgroundaudio='SoundTrack2.mp3' data-backgroundaudiovolume='0.99'>
				</div>
			 </body></html>"
            );

            data.SuckInDataFromEditedDom(editedPageDom);

            Assert.AreEqual(
                "SoundTrack2.mp3",
                data.GetXmatterPageDataAttributeValue("frontCover", "data-backgroundaudio")
            );
            Assert.AreEqual(
                "0.99",
                data.GetXmatterPageDataAttributeValue("frontCover", "data-backgroundaudiovolume")
            );

            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-xmatter-page='frontCover' "
                        + "and @data-backgroundaudio='SoundTrack2.mp3' and @data-backgroundaudiovolume='0.99' and not(text())]",
                    1
                );
        }

        [Test]
        public void UpdateFieldsAndVariables_CustomLibraryVariable_CopiedToOtherElement()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div class='bloom-page' id='guid3'>
					<p>
						<textarea lang='xyz' idc='copyOfVTitle'  data-book='bookTitle'>tree</textarea>
						<textarea lang='xyz' id='1' data-collection='testLibraryVariable'>aa</textarea>
					   <textarea lang='xyz' id2='2'  data-collection='testLibraryVariable'>bb</textarea>
					</p>
				</div>
				</body></html>"
            );
            var data = new BookData(dom, _collectionSettings, null);
            data.UpdateVariablesAndDataDivThroughDOM();
            var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id2='2']");
            Assert.AreEqual("aa", textarea2.InnerText);
        }

        [Test]
        public void UpdateVariablesAndDataDivThroughDOM_LeveledReader_CopiesLevelFromBookInfo()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body class='leveled-reader'>
				<div id='bloomDataDiv'>
				</div>
				<div class='bloom-page frontCover' id='guid2' data-xmatter-page='frontCover'>
				</div>
				</body></html>"
            );
            var data = new BookData(dom, _collectionSettings, null);
            var info = GetLeveledDecodableInfo();
            data.UpdateVariablesAndDataDivThroughDOM(info);

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='levelOrStageNumber' and @lang='*' and text()='3']",
                    1
                );
        }

        [Test]
        public void UpdateVariablesAndDataDivThroughDOM_DecodableReader_CopiesStageFromBookInfo()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body class='decodable-reader'>
				<div id='bloomDataDiv'>
				</div>
				<div class='bloom-page frontCover' id='guid2' data-xmatter-page='frontCover'>
				</div>
				</body></html>"
            );
            var data = new BookData(dom, _collectionSettings, null);
            var info = GetLeveledDecodableInfo();
            data.UpdateVariablesAndDataDivThroughDOM(info);

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='levelOrStageNumber' and @lang='*' and text()='4']",
                    1
                );
        }

        [Test]
        public void UpdateVariablesAndDataDivThroughDOM_NotLeveledOrDecodable_DoesNotCopyLevelOrStageFromBookInfo()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
				</div>
				<div class='bloom-page frontCover' id='guid2' data-xmatter-page='frontCover'>
				</div>
				</body></html>"
            );
            var data = new BookData(dom, _collectionSettings, null);
            var info = GetLeveledDecodableInfo();
            data.UpdateVariablesAndDataDivThroughDOM(info);

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='levelOrStageNumber']"
                );
        }

        [Test]
        public void UpdateVariablesAndDataDivThroughDOM_NotLeveledOrDecodable_EliminatesOutOfDateDataDivElement()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
					<div data-book='levelOrStageNumber' lang='*'>4</div>
				</div>
				<div class='bloom-page frontCover' id='guid2' data-xmatter-page='frontCover'>
				</div>
				</body></html>"
            );
            var data = new BookData(dom, _collectionSettings, null);
            var info = GetLeveledDecodableInfo();
            data.UpdateVariablesAndDataDivThroughDOM(info);

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='levelOrStageNumber']"
                );
        }

        [Test]
        public void UpdateVariablesAndDataDivThroughDOM_DecodableReader_SetsDecodableStageLetters()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body class='decodable-reader'>
				<div id='bloomDataDiv'>
				</div>
				<div class='bloom-page frontCover' id='guid2' data-xmatter-page='frontCover'>
				</div>
				</body></html>"
            );
            var data = new BookData(dom, _collectionSettings, null);

            var bookFolderPath = Path.Combine(_collectionSettings.FolderPath, "book");
            var info = new BookInfo(bookFolderPath, true);
            var decodableState = ToolboxToolState.CreateFromToolId("decodableReader");
            decodableState.State = "stage:4;sort:alphabetic";
            info.Tools.Add(decodableState);

            var readerSettingsPath = DecodableReaderToolSettings.GetReaderToolsSettingsFilePath(
                _collectionSettings
            );
            File.WriteAllText(
                readerSettingsPath,
                "{\"levels\":[{\"thingsToRemember\":[\"Simple Pictures\",\"Concrete Topics (e.g. animals, house hold objects)\"],\"maxWordsPerPage\":5,\"maxWordsPerSentence\":5,\"maxWordsPerBook\":23,\"maxUniqueWordsPerBook\":8,\"maxAverageWordsPerSentence\":0},{\"thingsToRemember\":[\"\"],\"maxWordsPerPage\":10,\"maxWordsPerSentence\":7,\"maxWordsPerBook\":72,\"maxUniqueWordsPerBook\":16,\"maxAverageWordsPerSentence\":0},{\"thingsToRemember\":[\"\"],\"maxWordsPerPage\":18,\"maxWordsPerSentence\":8,\"maxWordsPerBook\":206,\"maxUniqueWordsPerBook\":32,\"maxAverageWordsPerSentence\":0},{\"thingsToRemember\":[\"\"],\"maxWordsPerPage\":22,\"maxWordsPerSentence\":9,\"maxWordsPerBook\":294,\"maxUniqueWordsPerBook\":50,\"maxAverageWordsPerSentence\":0},{\"thingsToRemember\":[\"\"],\"maxWordsPerPage\":25,\"maxWordsPerSentence\":10,\"maxWordsPerBook\":500,\"maxUniqueWordsPerBook\":64,\"maxAverageWordsPerSentence\":0}],\"stages\":[{\"sightWords\":\"the of and to\",\"letters\":\"a e r\",\"allowedWordsFile\":\"\",\"name\":\"1\"},{\"sightWords\":\"is you that he\",\"letters\":\"i o\",\"allowedWordsFile\":\"\",\"name\":\"2\"},{\"sightWords\":\"was for as with\",\"letters\":\"n t\",\"allowedWordsFile\":\"\",\"name\":\"3\"},{\"sightWords\":\"his they I\",\"letters\":\"l s\",\"allowedWordsFile\":\"\",\"name\":\"4\"},{\"sightWords\":\"be this have from\",\"letters\":\"c u\",\"allowedWordsFile\":\"\",\"name\":\"5\"},{\"sightWords\":\"or one by what\",\"letters\":\"d ng\",\"allowedWordsFile\":\"\",\"name\":\"6\"},{\"sightWords\":\"all we when\",\"letters\":\"m p\",\"allowedWordsFile\":\"\",\"name\":\"7\"},{\"sightWords\":\"your said there\",\"letters\":\"g h\",\"allowedWordsFile\":\"\",\"name\":\"8\"},{\"sightWords\":\"she do how\",\"letters\":\"th\",\"allowedWordsFile\":\"\",\"name\":\"9\"},{\"sightWords\":\"were about out\",\"letters\":\"b sh\",\"allowedWordsFile\":\"\",\"name\":\"10\"},{\"sightWords\":\"then them little\",\"letters\":\"f y\",\"allowedWordsFile\":\"\",\"name\":\"11\"},{\"sightWords\":\"so some her\",\"letters\":\"ch w\",\"allowedWordsFile\":\"\",\"name\":\"12\"},{\"sightWords\":\"are make like\",\"letters\":\"k v\",\"allowedWordsFile\":\"\",\"name\":\"13\"},{\"sightWords\":\"into has look\",\"letters\":\"x z\",\"allowedWordsFile\":\"\",\"name\":\"14\"},{\"sightWords\":\"go see no\",\"letters\":\"j q\",\"allowedWordsFile\":\"\",\"name\":\"15\"}],\"letters\":\"a b c ch d e f g h i j k l m n ng o p q r s sh t th u v w x y z\",\"sentencePunct\":\"\",\"moreWords\":\"\",\"useAllowedWords\":0}"
            );
            data.UpdateVariablesAndDataDivThroughDOM(info);

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='decodableStageLetters' and @lang='"
                        + data.Language1.Tag
                        + "' and text()='a, e, r, i, o, n, t, l, s']",
                    1
                );
        }

        private BookInfo GetLeveledDecodableInfo()
        {
            // an arbitrary temp folder where we won't find any metadata, to create an empty default BookInfo.
            var info = new BookInfo(_collectionSettings.FolderPath, true);
            var levelState = ToolboxToolState.CreateFromToolId("leveledReader");
            levelState.State = "3";
            info.Tools.Add(levelState);
            var decodableState = ToolboxToolState.CreateFromToolId("decodableReader");
            decodableState.State = "stage:4;sort:alphabetic";
            info.Tools.Add(decodableState);
            return info;
        }

        [Test]
        public void UpdateFieldsAndVariables_HasBookTitleTemplateWithVernacularPlaceholder_CreatesTitleForVernacular()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitleTemplate' lang='{V}'>the title</div>
				</div>
				</body></html>"
            );
            var data = new BookData(dom, _collectionSettings, null);
            data.UpdateVariablesAndDataDivThroughDOM();
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@data-book='bookTitle' and @lang='"
                        + data.Language1.Tag
                        + "' and text()='the title']",
                    1
                );
        }

        [Test]
        public void UpdateVariablesAndDataDivThroughDOM_DataBookAttributes_AttributesAddedToDiv()
        {
            var dom = new HtmlDom(
                @"<html><head></head><body>
				<div id='bloomDataDiv'>
					<div data-xmatter-page='frontCover' "
                    + HtmlDom.musicAttrName
                    + "='audio/SoundTrack1.mp3' "
                    + HtmlDom.musicVolumeName
                    + @"='0.17'></div>
				</div>
				<div id='firstPage' class='bloom-page' data-xmatter-page='frontCover'>1st page</div>
				</body></html>"
            );
            var data = new BookData(dom, _collectionSettings, null);
            data.UpdateVariablesAndDataDivThroughDOM();
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='firstPage' and @data-xmatter-page='frontCover' and @"
                        + HtmlDom.musicAttrName
                        + "='audio/SoundTrack1.mp3' and @"
                        + HtmlDom.musicVolumeName
                        + "='0.17']",
                    1
                );
        }

        [Test]
        public void UpdateFieldsAndVariables_VernacularTitleChanged_TitleCopiedToParagraphAnotherPage()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div class='bloom-page' id='guid2'>
						<p>
							<textarea lang='xyz' data-book='bookTitle'>original</textarea>
						</p>
				</div>
				<div class='bloom-page' id='0a99fad3-0a17-4240-a04e-86c2dd1ec3bd'>
						<p class='centered' lang='xyz' data-book='bookTitle' id='P1'>originalButNoExactlyCauseItShouldn'tMatter</p>
				</div>
			 </body></html>"
            );
            var data = new BookData(dom, _collectionSettings, null);
            var textarea1 = dom.SelectSingleNodeHonoringDefaultNS(
                "//textarea[@data-book='bookTitle' and @lang='xyz']"
            );
            textarea1.InnerText = "peace & quiet";
            data.SynchronizeDataItemsThroughoutDOM();
            var paragraph = dom.SelectSingleNodeHonoringDefaultNS(
                "//p[@data-book='bookTitle'  and @lang='xyz']"
            );
            Assert.AreEqual("peace & quiet", paragraph.InnerText);
        }

        [Test]
        public void UpdateFieldsAndVariables_OneDataItemChanges_ItemsWithThatLanguageAlsoUpdated()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div class='bloom-page' id='guid1'>
					<p>
						<textarea lang='en' id='1'  data-book='bookTitle'>EnglishTitle</textarea>
						<textarea lang='xyz' id='2'  data-book='bookTitle'>xyzTitle</textarea>
					</p>
				</div>
				<div class='bloom-page' id='guid3'>
					<p>
						<textarea lang='xyz' id3='3'  data-book='bookTitle'>xyzTitle</textarea>
					</p>
				</div>
			 </body></html>"
            );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//textarea[@lang='en' and @id='1' and text()='EnglishTitle']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//textarea[@lang='xyz'  and @id='2' and text()='xyzTitle']",
                    1
                );
            var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='2']");
            textarea2.InnerText = "newXyzTitle";
            var data = new BookData(dom, CreateCollection(Language1LangTag: "etr"), null);
            data.SynchronizeDataItemsThroughoutDOM();
            var textarea3 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id3='3']");
            Assert.AreEqual("newXyzTitle", textarea3.InnerText);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//textarea[@id='1' and text()='EnglishTitle']",
                    1
                );
        }

        [Test]
        public void UpdateFieldsAndVariables_EnglishDataItemChanges_VernItemsUntouched()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div class='bloom-page' id='guid1'>
					<p>
						<textarea lang='en' id='1'  data-book='bookTitle'>EnglishTitle</textarea>
						<textarea lang='xyz' id='2'  data-book='bookTitle'>xyzTitle</textarea>
					</p>
				</div>
				<div class='bloom-page' id='guid3'>
					<p>
						<textarea lang='xyz' id3='3'  data-book='bookTitle'>xyzTitle</textarea>
					</p>
				</div>
			 </body></html>"
            );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//textarea[@lang='en' and @id='1' and text()='EnglishTitle']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//textarea[@lang='xyz'  and @id='2' and text()='xyzTitle']",
                    1
                );
            var textarea1 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='1']");
            textarea1.InnerText = "newEnglishTitle";
            var data = new BookData(dom, CreateCollection(Language1LangTag: "etr"), null);
            data.SynchronizeDataItemsThroughoutDOM();
            var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='2']");
            Assert.AreEqual("xyzTitle", textarea2.InnerText);
            var textarea3 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id3='3']");
            Assert.AreEqual("xyzTitle", textarea3.InnerText);
        }

        [Test]
        public void UpdateFieldsAndVariables_BookTitleInSpanOnSecondPage_UpdatesH2OnFirstWithCurrentNationalLang()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div class='bloom-page titlePage'>
						<div class='pageContent'>
							<h2 data-book='bookTitle' lang='N1'>{national book title}</h2>
						</div>
					</div>
				<div class='bloom-page verso'>
					<div class='pageContent'>
						(<span lang='en' data-book='bookTitle'>Vaccinations</span><span lang='tpi' data-book='bookTitle'>Tambu Sut</span>)
						<br />
					</div>
				</div>
				</body></html>"
            );
            var collectionSettings = CreateCollection("etr", null, "en", null, "tpi", null);
            var data = new BookData(dom, collectionSettings, null);
            data.SynchronizeDataItemsThroughoutDOM();
            var nationalTitle = (SafeXmlElement)
                dom.SelectSingleNodeHonoringDefaultNS("//h2[@data-book='bookTitle']");
            Assert.AreEqual("Vaccinations", nationalTitle.InnerText);

            //now switch the national language to Tok Pisin

            collectionSettings.Language2.Tag = "tpi";
            collectionSettings.Language3.Tag = "en";
            // Hack to update cache of ML1
            data.SetMultilingualContentLanguages("etr", "tpi", "en");
            data.SynchronizeDataItemsThroughoutDOM();
            nationalTitle = (SafeXmlElement)
                dom.SelectSingleNodeHonoringDefaultNS("//h2[@data-book='bookTitle']");
            Assert.AreEqual("Tambu Sut", nationalTitle.InnerText);
        }

        [Test]
        public void UpdateFieldsAndVariables_OneLabelPreserved_DuplicatesRemovedNotAdded()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div class='bloom-page titlePage'>
						<div id='target' class='bloom-content1' data-book='insideBackCover'>
							<label class='bubble'>Some more space to put things</label><label class='bubble'>Some more space to put things</label>Here is the content
						</div>
				</div>
				</body></html>"
            );
            var collectionSettings = CreateCollection(Language1LangTag: "etr");
            var data = new BookData(dom, collectionSettings, null);
            data.SynchronizeDataItemsThroughoutDOM();
            var target = (SafeXmlElement)
                dom.SelectSingleNodeHonoringDefaultNS("//div[@id='target']");

            // It's expected that the surviving label goes at the end.
            Assert.That(
                target.InnerText,
                Is.EqualTo("Here is the contentSome more space to put things")
            );
            var label = (SafeXmlElement)target.SelectSingleNodeHonoringDefaultNS("//label");
            Assert.That(label.InnerText, Is.EqualTo("Some more space to put things"));
        }

        [Test]
        public void GetMultilingualContentLanguage_ContentLanguageSpecifiedInHtml_ReadsIt()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='contentLanguage1'>en</div>
						<div data-book='contentLanguage2'>tpi</div>
						<div data-book='contentLanguage3'>fr</div>
				</div>
				</body></html>"
            );
            var collectionSettings = CreateCollection();
            var data = new BookData(dom, collectionSettings, null);
            Assert.AreEqual("en", data.Language1.Tag);
            Assert.AreEqual("tpi", data.Language2.Tag);
            Assert.AreEqual("fr", data.Language3.Tag);
        }

        [Test]
        public void SetMultilingualContentLanguages_NonCollectionLanguage_Throws()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				</body></html>"
            );
            var collectionSettings = CreateCollection();
            var data = new BookData(dom, collectionSettings, null);
            Assert.Throws<ApplicationException>(
                () => data.SetMultilingualContentLanguages(new[] { "de" })
            );
            Assert.Throws<ApplicationException>(
                () => data.SetMultilingualContentLanguages(new[] { "fr", "de" })
            );
            Assert.Throws<ApplicationException>(
                () => data.SetMultilingualContentLanguages(new[] { "fr", "en", "de" })
            );
        }

        [Test]
        public void SetMultilingualContentLanguage_ContentLanguageSpecifiedInHtml_ReadsIt()
        {
            // Same languages as collection (tpi, fr, en) but different order.
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='contentLanguage1'>fr</div>
						<div data-book='contentLanguage2'>en</div>
						<div data-book='contentLanguage3'>tpi</div>
				</div>
				</body></html>"
            );
            var collectionSettings = CreateCollection();
            var data = new BookData(dom, collectionSettings, null);
            //data.SetMultilingualContentLanguages(new [] {"fr", "en", "tpi"});
            Assert.AreEqual("fr", data.Language1.Tag);
            Assert.AreEqual("en", data.Language2.Tag);
            Assert.AreEqual("tpi", data.Language3.Tag);
        }

        [Test]
        public void UpdateVariablesAndDataDivThroughDOM_NewLangAdded_AddedToDataDiv()
        {
            var dom = new HtmlDom(
                @"<html><head></head><body><div data-book='someVariable' lang='en'>hi</div></body></html>"
            );

            var e = dom.RawDom.CreateElement("div");
            e.SetAttribute("data-book", "someVariable");
            e.SetAttribute("lang", "fr");
            e.InnerText = "bonjour";
            dom.RawDom.SelectSingleNode("//body").AppendChild(e);
            var data = new BookData(dom, CreateCollection(), null);
            data.UpdateVariablesAndDataDivThroughDOM();
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//body/div[1][@id='bloomDataDiv']", 1); //NB microsoft uses 1 as the first. W3c uses 0.
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='someVariable' and @lang='en' and text()='hi']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='someVariable' and @lang='fr' and text()='bonjour']",
                    1
                );
        }

        [Test]
        public void UpdateVariablesAndDataDivThroughDOM_HasDataLibraryValues_LibraryValuesNotPutInDataDiv()
        {
            var dom = new HtmlDom(
                @"<html><head></head><body><div data-book='someVariable' lang='en'>hi</div><div data-collection='user' lang='en'>john</div></body></html>"
            );
            var data = new BookData(dom, CreateCollection(), null);
            data.UpdateVariablesAndDataDivThroughDOM();
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='user']");
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-collection]");
        }

        [Test]
        public void UpdateVariablesAndDataDivThroughDOM_DoesNotExist_MakesOne()
        {
            var dom = new HtmlDom(
                @"<html><head></head><body><div data-book='someVariable'>world</div></body></html>"
            );
            var data = new BookData(dom, CreateCollection(), null);
            data.UpdateVariablesAndDataDivThroughDOM();
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//body/div[1][@id='bloomDataDiv']", 1); //NB microsoft uses 1 as the first. W3c uses 0.
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='someVariable' and text()='world']",
                    1
                );
        }

        [Test]
        public void UpdateVariablesAndDataDivThroughDOM_NewXmatterPageAttributeSet_AddedToDataDiv()
        {
            var dom = new HtmlDom(@"<html><head></head><body></body></html>");

            var e = dom.RawDom.CreateElement("div");
            e.SetAttribute("data-xmatter-page", "frontCover");
            e.SetAttribute("data-someattribute", "someValue");
            e.InnerText = "anything";
            dom.RawDom.SelectSingleNode("//body").AppendChild(e);

            var data = new BookData(dom, CreateCollection(), null);
            data.UpdateVariablesAndDataDivThroughDOM();

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//body/div[1][@id='bloomDataDiv']", 1); //NB microsoft uses 1 as the first. W3c uses 0.
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-xmatter-page='frontCover' and @data-someattribute='someValue' and not(text())]",
                    1
                );
        }

        //[Test] TODO - this test doesn't work because UpdateVariablesAndDataDivThroughDOM first updates the page from the data div before updating the data div from the page.
        // I couldn't get it worked out, but the production code does do things correctly. I attempted to add this test for BL-5409.
        public void UpdateVariablesAndDataDivThroughDOM_UpdatedXmatterPageAttributeSet_UpdatedInDataDiv()
        {
            var dom = new HtmlDom(
                @"<html><head></head><body>
				<div id='bloomDataDiv'><div data-xmatter-page='frontCover' data-someattribute='someValue'></div></div>
				<div class='bloom-page' data-xmatter-page='frontCover' data-someattribute='someValue'>anything</div>
				</body></html>"
            );

            var e = (SafeXmlElement)
                dom.RawDom.SelectSingleNode(
                    "//body/div[@class='bloom-page' and @data-xmatter-page='frontCover']"
                );
            e.SetAttribute("data-someattribute", "someOtherValue");

            var data = new BookData(dom, CreateCollection(), null);
            data.UpdateVariablesAndDataDivThroughDOM();

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//body/div[1][@id='bloomDataDiv']", 1); //NB microsoft uses 1 as the first. W3c uses 0.
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-xmatter-page='frontCover' and @data-someattribute='someOtherValue' and not(text())]",
                    1
                );
        }

        //[Test] TODO - this test doesn't work because UpdateVariablesAndDataDivThroughDOM first updates the page from the data div before updating the data div from the page.
        // I couldn't get it worked out, but the production code does do things correctly. I attempted to add this test for BL-5409.
        public void UpdateVariablesAndDataDivThroughDOM_RemovedXmatterPageAttribute_RemovedFromDataDiv()
        {
            var dom = new HtmlDom(
                @"<html><head></head><body>
				<div id='bloomDataDiv'><div data-xmatter-page='frontCover' data-someattribute='someValue'></div></div>
				<div class='bloom-page' data-xmatter-page='frontCover' data-someattribute='someValue'>anything</div>
				</body></html>"
            );

            var e = (SafeXmlElement)
                dom.RawDom.SelectSingleNode(
                    "//body/div[@class='bloom-page' and @data-xmatter-page='frontCover']"
                );
            e.RemoveAttribute("data-someattribute");

            var data = new BookData(dom, CreateCollection(), null);
            data.UpdateVariablesAndDataDivThroughDOM();

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//body/div[1][@id='bloomDataDiv']", 1); //NB microsoft uses 1 as the first. W3c uses 0.
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-xmatter-page='frontCover' and not(@data-someattribute) and not(text())]",
                    1
                );
        }

        [Test]
        public void UpdateVariablesAndDataDivThroughDOM_L1WithSpecialChars_EncodedProperly()
        {
            var dom = new HtmlDom(
                @"<html><head></head><body><div id='data-div' data-book='contentLanguage1' lang='*'>en</div></body></html>"
            );
            var data = new BookData(dom, CreateCollection("en-uk&au", "English"), null);

            data.SetMultilingualContentLanguages("en-uk&au");
            data.UpdateVariablesAndDataDivThroughDOM();

            // Verify the language codes
            Assert.That(dom.RawDom.InnerXml.Contains("en-uk&amp;au"), "L1");
        }

        [Test]
        public void UpdateVariablesAndDataDivThroughDOM_L2L3WithSpecialChars_EncodedProperly()
        {
            var dom = new HtmlDom(
                @"<html><head></head><body><div id='data-div' data-book='contentLanguage1' lang='*'>en</div></body></html>"
            );
            var data = new BookData(
                dom,
                CreateCollection(
                    "en",
                    "English",
                    "es-es&la",
                    "spanish and latin",
                    "fr-fr&ca",
                    "French and Canadian"
                ),
                null
            );
            data.SetMultilingualContentLanguages("en", "es-es&la", "fr-fr&ca");

            data.UpdateVariablesAndDataDivThroughDOM();

            // Verify the language codes
            Assert.That(dom.RawDom.InnerXml.Contains("es-es&amp;la"), "L2");
            Assert.That(dom.RawDom.InnerXml.Contains("fr-fr&amp;ca"), "L3");
        }

        [Test]
        public void SetMultilingualContentLanguages_HasTrilingualLanguages_AddsToDataDiv()
        {
            var dom = new HtmlDom(@"<html><head></head><body></body></html>");
            var data = new BookData(
                dom,
                CreateCollection("en", "English", "okm", "Oklhoman", "kbt", "Klingon"),
                null
            );
            data.SetMultilingualContentLanguages("kbt", "en", "okm");
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='contentLanguage1' and text()='kbt']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='contentLanguage2' and text()='en']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='contentLanguage3' and text()='okm']",
                    1
                );
        }

        [Test]
        public void SetMultilingualContentLanguages_SecondAndThirdContentLangTurnedOff_RemovedFromDataDiv()
        {
            var dom = new HtmlDom(
                @"<html><head><div id='bloomDataDiv'><div data-book='contentLanguage2'>xyz</div><div data-book='contentLanguage3'>kbt</div></div></head><body></body></html>"
            );
            var data = new BookData(dom, CreateCollection(), null);
            data.SetMultilingualContentLanguages("en");
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='contentLanguage2']",
                    0
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='contentLanguage3']",
                    0
                );
        }

        [TestCase("", "", "", null)]
        [TestCase("the country", "", "", "the country")]
        [TestCase("the country", "the province", "", "the province, the country")]
        [TestCase(
            "the country",
            "the province",
            "the district",
            "the district, the province, the country"
        )]
        [TestCase("", "the province", "the district", "the district, the province")]
        [TestCase("", "", "the district", "the district")]
        [TestCase("", "the province", "", "the province")]
        [TestCase(
            "country 1 & 2",
            "province 1 & 2",
            "district 1 & 2",
            "district 1 &amp; 2, province 1 &amp; 2, country 1 &amp; 2"
        )] // output is expected to return an encoded string
        public void Constructor_CollectionSettingsHasVariousLocationFields_LanguageLocationFilledCorrect(
            string country,
            string province,
            string district,
            string expected
        )
        {
            var dom = new HtmlDom();
            var data = new BookData(
                dom,
                new CollectionSettings()
                {
                    Country = country,
                    Province = province,
                    District = district
                },
                null
            );
            Assert.AreEqual(expected, data.GetVariableOrNull("languageLocation", "*").Xml);
        }

        /*    data.AddLanguageString("*", "nameOfLanguage", collectionSettings.Language1.Name, true);
                data.AddLanguageString("*", "nameOfNationalLanguage1",
                                       collectionSettings.Language2.GetNameInLanguage(collectionSettings.Language2Tag), true);
                data.AddLanguageString("*", "nameOfNationalLanguage2",
                                       collectionSettings.Language3.GetNameInLanguage(collectionSettings.Language2Tag), true);
                data.AddGenericLanguageString("iso639Code", collectionSettings.Language1Tag, true);*/

        [Test]
        public void Constructor_CollectionSettingsHasLangTag_iso639CodeFilledIn()
        {
            var dom = new HtmlDom();
            var data = new BookData(dom, CreateCollection(Language1LangTag: "xyz"), null);
            Assert.AreEqual("xyz", data.GetVariableOrNull("iso639Code", "*").Xml);
        }

        [Test]
        public void Constructor_CollectionSettingsHasLangTag_DataSetContainsProperV()
        {
            var dom = new HtmlDom();
            var data = new BookData(dom, CreateCollection(Language1LangTag: "xyz"), null);
            Assert.AreEqual("xyz", data.DealiasWritingSystemId("V"));
        }

        // We have currently commented out the code that sets nameOfLanguage and nameOfNationalLanguage1/2 as we can't find
        // anything that uses them. Keeping the tests around in case we decide to reinstate.
        //[Test]
        //public void Constructor_CollectionSettingsHasLanguage1Name_LanguagenameOfNationalLanguage1FilledIn()
        //{
        //	var dom = new HtmlDom();
        //	var data = new BookData(dom, CreateCollection(Language1Name: "foobar"), null);
        //	Assert.AreEqual("foobar", data.GetVariableOrNull("nameOfLanguage", "*").Xml);
        //}

        //NB: yes, this is confusing, having lang1 = language, lang2 = nationalLang1, lang3 = nationalLang2

        //[Test]
        //public void Constructor_CollectionSettingsHasLanguage2Tag_nameOfNationalLanguage1FilledIn()
        //{
        //	var dom = new HtmlDom();
        //	var data = new BookData(dom, CreateCollection(Language2Tag: "tpi", Language2Name:null), null);
        //	Assert.AreEqual("Tok Pisin", data.GetVariableOrNull("nameOfNationalLanguage1", "*").Xml);
        //}

        //[Test]
        //public void Constructor_CollectionSettingsHasLanguage3Tag_nameOfNationalLanguage2FilledIn()
        //{
        //	var dom = new HtmlDom();
        //	var data = new BookData(dom, CreateCollection(Language1Tag : "en", Language1Name:"English", "fr", "French", "tpi", Language3Name:"Tok Pisin"), null);
        //	data.SetMultilingualContentLanguages("en", "fr", "tpi");
        //	Assert.AreEqual("Tok Pisin", data.GetVariableOrNull("nameOfNationalLanguage2", "*").Xml);
        //}

        [Test]
        public void Set_DidNotHaveForm_Added()
        {
            var htmlDom = new HtmlDom();
            var data = new BookData(htmlDom, CreateCollection(), null);
            data.Set("1", XmlString.FromXml("one"), "en");
            Assert.AreEqual("one", data.GetVariableOrNull("1", "en").Xml);
            AssertThatXmlIn
                .Dom(htmlDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 1);
            var roundTripData = new BookData(htmlDom, CreateCollection(), null);
            var t = roundTripData.GetVariableOrNull("1", "en").Xml;
            Assert.AreEqual("one", t);
        }

        [Test]
        public void Set_AddTwoForms_BothAdded()
        {
            var htmlDom = new HtmlDom();
            var data = new BookData(htmlDom, CreateCollection(), null);
            data.Set("1", XmlString.FromXml("one"), "en");
            data.Set("1", XmlString.FromXml("uno"), "es");
            var roundTripData = new BookData(htmlDom, CreateCollection(), null);
            Assert.AreEqual("one", roundTripData.GetVariableOrNull("1", "en").Xml);
            Assert.AreEqual("uno", roundTripData.GetVariableOrNull("1", "es").Xml);
        }

        [Test]
        public void Set_DidHaveForm_StillJustOneCopy()
        {
            var htmlDom = new HtmlDom();
            var data = new BookData(htmlDom, CreateCollection(), null);
            data.Set("1", XmlString.FromXml("one"), "en");
            data.Set("1", XmlString.FromXml("one"), "en");
            Assert.AreEqual("one", data.GetVariableOrNull("1", "en").Xml);
            AssertThatXmlIn
                .Dom(htmlDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 1);
            var roundTripData = new BookData(htmlDom, CreateCollection(), null);
            var t = roundTripData.GetVariableOrNull("1", "en").Xml;
            Assert.AreEqual("one", t);
        }

        [Test]
        public void Set_EmptyString_Removes()
        {
            var htmlDom = new HtmlDom();
            var data = new BookData(htmlDom, CreateCollection(), null);
            data.Set("1", XmlString.FromXml("one"), "en");
            data.Set("1", XmlString.Empty, "en");
            Assert.AreEqual(null, data.GetVariableOrNull("1", "en").Xml);
            AssertThatXmlIn
                .Dom(htmlDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 0);
            var roundTripData = new BookData(htmlDom, CreateCollection(), null);
            Assert.IsNull(roundTripData.GetVariableOrNull("1", "en").Xml);
        }

        [Test]
        public void Set_Null_Removes()
        {
            var htmlDom = new HtmlDom();
            var data = new BookData(htmlDom, CreateCollection(), null);
            data.Set("1", XmlString.FromXml("one"), "en");
            data.Set("1", null, "en");
            Assert.AreEqual(null, data.GetVariableOrNull("1", "en").Xml);
            AssertThatXmlIn
                .Dom(htmlDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 0);
            var roundTripData = new BookData(htmlDom, CreateCollection(), null);
            Assert.IsNull(roundTripData.GetVariableOrNull("1", "en").Xml);
        }

        [Test]
        public void RemoveSingleForm_HasForm_Removed()
        {
            var htmlDom = new HtmlDom();
            var data = new BookData(htmlDom, CreateCollection(), null);
            data.Set("1", XmlString.FromXml("one"), "en");
            var data2 = new BookData(htmlDom, CreateCollection(), null);
            data2.RemoveSingleForm("1", "en");
            Assert.IsNull(data2.GetVariableOrNull("1", "en").Xml);
        }

        [Test]
        public void RemoveDataDivVariableForOneLanguage_DoesNotHaveForm_OK()
        {
            var htmlDom = new HtmlDom();
            var data = new BookData(htmlDom, CreateCollection(), null);
            data.RemoveSingleForm("1", "en");
            Assert.AreEqual(null, data.GetVariableOrNull("1", "en").Xml);
            AssertThatXmlIn
                .Dom(htmlDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 0);
            var roundTripData = new BookData(htmlDom, CreateCollection(), null);
            Assert.IsNull(roundTripData.GetVariableOrNull("1", "en").Xml);
        }

        [Test]
        public void RemoveDataDivVariableForOneLanguage_WasLastForm_WholeElementRemoved()
        {
            var htmlDom = new HtmlDom();
            var data = new BookData(htmlDom, CreateCollection(), null);
            data.Set("1", XmlString.FromXml("one"), "en");
            var roundTripData = new BookData(htmlDom, CreateCollection(), null);
            roundTripData.RemoveSingleForm("1", "en");
            Assert.IsNull(roundTripData.GetVariableOrNull("1", "en").Xml);
        }

        [Test]
        public void RemoveDataDivVariableForOneLanguage_WasTwoForms_OtherRemains()
        {
            var htmlDom = new HtmlDom();
            var data = new BookData(htmlDom, CreateCollection(), null);
            data.Set("1", XmlString.FromXml("one"), "en");
            data.Set("1", XmlString.FromXml("uno"), "es");
            var roundTripData = new BookData(htmlDom, CreateCollection(), null);
            roundTripData.RemoveSingleForm("1", "en");
            Assert.IsNull(roundTripData.GetVariableOrNull("1", "en").Xml);
            Assert.AreEqual("uno", roundTripData.GetVariableOrNull("1", "es").Xml);
        }

        [Test]
        public void Set_CalledTwiceWithDIfferentLangs_HasBoth()
        {
            var htmlDom = new HtmlDom();
            var data = new BookData(htmlDom, CreateCollection(), null);
            data.Set("1", XmlString.FromXml("one"), "en");
            data.Set("1", XmlString.FromXml("uno"), "es");
            Assert.AreEqual(2, data.GetMultiTextVariableOrEmpty("1").Forms.Count());
        }

        [Test]
        public void UpdateVariablesAndDataDivThroughDOM_VariableIsNull_DataDivForItRemoved()
        {
            var htmlDom = new HtmlDom();
            var data = new BookData(htmlDom, CreateCollection(), null);
            data.Set("1", XmlString.FromXml("one"), "en");
            data.Set("1", null, "es");
            data.UpdateVariablesAndDataDivThroughDOM();
            AssertThatXmlIn
                .Dom(htmlDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("html/body/div/div[@lang='en']", 1);
            AssertThatXmlIn
                .Dom(htmlDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("html/body/div/div[@lang='es']", 0);
        }

        [Test]
        public void PrettyPrintLanguage_DoesNotModifyUnknownCodes()
        {
            var htmlDom = new HtmlDom();
            var settingsettings = CreateCollection(
                Language1LangTag: "pdc",
                Language1Name: "German, Kludged"
            );
            var data = new BookData(htmlDom, settingsettings, null);
            Assert.That(data.GetDisplayNameForLanguage("xyz"), Is.EqualTo("xyz"));
        }

        [Test]
        public void PrettyPrintLanguage_AdjustsLang1()
        {
            var htmlDom = new HtmlDom();
            var settingsettings = CreateCollection(
                Language1LangTag: "pdc",
                Language1Name: "German, Kludged"
            );
            var data = new BookData(htmlDom, settingsettings, null);
            Assert.That(data.GetDisplayNameForLanguage("pdc"), Is.EqualTo("German, Kludged"));
        }

        [Test]
        public void PrettyPrintLanguage_AdjustsKnownLanguages_German()
        {
            var htmlDom = new HtmlDom();
            var settings = CreateCollection(
                Language1Name: "German, Kludged",
                Language1LangTag: "pdc",
                Language2Tag: "de",
                Language2Name: null,
                Language3Tag: "fr",
                Language3Name: null
            );
            var data = new BookData(htmlDom, settings, null);
            Assert.That(data.GetDisplayNameForLanguage("de"), Is.EqualTo("Deutsch"));
            Assert.That(data.GetDisplayNameForLanguage("fr"), Is.EqualTo("français"));
            Assert.That(data.GetDisplayNameForLanguage("en"), Is.EqualTo("English"));
            Assert.That(data.GetDisplayNameForLanguage("es"), Is.EqualTo("español"));
        }

        [Test]
        public void PrettyPrintLanguage_AdjustsKnownLanguages_English()
        {
            var htmlDom = new HtmlDom();
            var settings = CreateCollection(
                Language1LangTag: "pdc",
                Language1Name: "German, Kludged",
                Language2Tag: "en",
                Language2Name: null,
                Language3Tag: "fr",
                Language3Name: null
            );
            var data = new BookData(htmlDom, settings, null);
            Assert.That(data.GetDisplayNameForLanguage("de"), Is.EqualTo("German"));
            Assert.That(data.GetDisplayNameForLanguage("fr"), Is.EqualTo("French"));
            Assert.That(data.GetDisplayNameForLanguage("en"), Is.EqualTo("English"));
            Assert.That(data.GetDisplayNameForLanguage("es"), Is.EqualTo("Spanish"));
        }

        [Test]
        [TestCase("nsk-Latn", "Naskapi-Latn (Naskapi)")]
        [TestCase("nsk-Latn-easy", "Naskapi-Latn-easy (Naskapi)")]
        [TestCase("nsk-Latn-easy-AB", "Naskapi-Latn-easy-AB (Naskapi)")]
        [TestCase("nsk-", "Naskapi")]
        [TestCase("zh-CN", "Chinese (Simplified)")]
        public void PrettyPrintLanguage_ShowsScriptVariantDistinctions(
            string lg3Code,
            string expectedResult
        )
        {
            var htmlDom = new HtmlDom();
            var settings = CreateCollection(
                Language1LangTag: "nsk",
                Language1Name: "Naskapi",
                Language2Tag: "en",
                Language2Name: "English",
                Language3Tag: lg3Code,
                Language3Name: null
            );
            var data = new BookData(htmlDom, settings, null);
            Assert.That(data.GetDisplayNameForLanguage("nsk"), Is.EqualTo("Naskapi"));
            Assert.That(data.GetDisplayNameForLanguage("en"), Is.EqualTo("English"));
            Assert.That(data.GetDisplayNameForLanguage(lg3Code), Is.EqualTo(expectedResult));
        }

        [Test]
        [TestCase("nsk-Latn", "Naskapi Roman", "Naskapi Roman (Naskapi)")]
        // Check that additional codes don't affect the custom name
        [TestCase("nsk-Latn-easy", "Simplified Naskapi", "Simplified Naskapi (Naskapi)")]
        [TestCase("sok", "Songorong", "Songorong")] // test custom name with no additional Script/Region/Variant codes
        [TestCase("zh-CN", "Mainland Chinese", "Mainland Chinese")] // special case for 'zh-CN'
        public void PrettyPrintLanguage_WithCustomLanguageName_DoesNotInsertSubtags(
            string lg1Code,
            string customName,
            string expectedResult
        )
        {
            var htmlDom = new HtmlDom();
            var settings = CreateCollection(
                Language1LangTag: lg1Code,
                Language1Name: null,
                Language2Tag: "en",
                Language2Name: null,
                Language3Tag: "fr",
                Language3Name: null
            );
            settings.Language1.SetName(customName, true);
            var data = new BookData(htmlDom, settings, null);
            Assert.That(data.GetDisplayNameForLanguage(lg1Code), Is.EqualTo(expectedResult));
        }

        [Test]
        public void MergeBrandingSettings_WithPersonalizationTemplate_ReplacesTemplateWithSubscriptionPersonalization()
        {
            // Create a temporary branding folder with proper structure
            var tempFolder = new TemporaryFolder("MergeBrandingSettingsTest");

            // Create a merge-test directory inside the temp folder
            var mergeTestDir = Path.Combine(tempFolder.Path, "merge-test");
            Directory.CreateDirectory(mergeTestDir);

            // Create branding file inside the merge-test directory
            var brandingFilePath = Path.Combine(mergeTestDir, "branding.json");

            // Create branding JSON content with personalization template in creditBranding field
            var testPersonalization = "Test Organization";
            var brandingJson =
                @"{
              ""presets"": [
                {
            ""data-book"": ""outside-back-cover-branding-bottom-html"",
            ""lang"": ""*"",
            ""content"": ""<img class='branding' src='back-cover-local.svg'/>{personalization}"",
            ""condition"": ""always""
                }
              ]
            }";

            // Write the branding JSON to the file
            File.WriteAllText(brandingFilePath, brandingJson);

            try
            {
                // Create collection settings with personalization
                var htmlDom = new HtmlDom();
                var settings = CreateCollection(
                    Language1LangTag: "en",
                    Language1Name: "English",
                    Language2Tag: "fr",
                    Language2Name: "French"
                );

                settings.Subscription = new Subscription(testPersonalization + "-LC-***-***");

                // Create BookData and process the branding file
                var bookData = new BookData(htmlDom, settings, null);

                // Pass the directory path to MergeBrandingSettings, not the file path
                bookData.MergeBrandingSettings(mergeTestDir);

                // Check that the personalization template was replaced
                var result = bookData
                    .GetVariableOrNull("outside-back-cover-branding-bottom-html", "*")
                    .Xml;
                Assert.That(
                    result,
                    Is.EqualTo(
                        $"<img class='branding' src='back-cover-local.svg'/>{testPersonalization}"
                    )
                );
            }
            finally
            {
                // Clean up
                tempFolder.Dispose();
            }
        }

        [Test]
        public void MigrateData_TopicInTokPisinButNotEnglish_ChangesLangToEnglish()
        {
            var bookDom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='topic' lang='tpi'>health</div>
				</div>
			 </body></html>"
            );
            var data = new BookData(bookDom, _collectionSettings, null);
            Assert.AreEqual("health", data.GetVariableOrNull("topic", "en").Xml);
            Assert.IsNull(data.GetVariableOrNull("topic", "tpi").Xml);
        }

        private BookData CreateBookDom(
            string topic,
            string copyright = null,
            string originalCopyright = null
        )
        {
            var topicDiv = topic != null ? $"<div data-book='topic' lang='en'>{topic}</div>" : "";
            var copyrightDiv =
                copyright != null ? $"<div data-book='copyright' lang='*'>{copyright}</div>" : "";
            var originalCopyrightDiv =
                originalCopyright != null
                    ? $"<div data-book='originalCopyright' lang='*'>{originalCopyright}</div>"
                    : "";

            var bookDom = new HtmlDom(
                @$"<html><head></head><body>
	<div id='bloomDataDiv'>
		{topicDiv}
		{copyrightDiv}
		{originalCopyrightDiv}
	</div>
 </body></html>"
            );

            return new BookData(bookDom, _collectionSettings, null);
        }

        [Test]
        public void MigrateSpiritualTopic_TopicIsNotSpiritual_NoChange()
        {
            var data = CreateBookDom(topic: "Dictionary", copyright: "Bible Society");
            Assert.AreEqual("Dictionary", data.GetVariableOrNull("topic", "en").Xml);
        }

        [TestCase("Bible Society")]
        [TestCase("Group of Bible Translators")]
        [TestCase("SIL International")]
        [TestCase("SIL Global")]
        [TestCase("Kartidaya")]
        [TestCase("WPS")]
        public void MigrateSpiritualTopic_TopicIsSpiritual_CopyrightIsForBible_MigratesTopicToBible(
            string copyright
        )
        {
            var data = CreateBookDom(topic: "Spiritual", copyright);
            Assert.AreEqual("Bible", data.GetVariableOrNull("topic", "en").Xml);
        }

        [TestCase("Some Guy", null)]
        [TestCase("Clearasil", null)] // Doesn't find "SIL"
        [TestCase("Some Guy", "Bible Society")]
        [TestCase("Clearasil", "Bible Society")] // Doesn't find "SIL"
        public void MigrateSpiritualTopic_TopicIsSpiritual_CopyrightIsNotForBible_RemovesTopic(
            string copyright,
            string originalCopyright
        )
        {
            var data = CreateBookDom(topic: "Spiritual", copyright, originalCopyright);
            Assert.AreEqual(new MultiTextBase(), data.GetMultiTextVariableOrEmpty("topic"));
        }

        [TestCase("Bible Society")]
        [TestCase("Group of Bible Translators")]
        [TestCase("SIL International")]
        [TestCase("Kartidaya")]
        [TestCase("WPS")]
        public void MigrateSpiritualTopic_TopicIsSpiritual_NoCopyright_OriginalCopyrightIsForBible_MigratesTopicToBible(
            string originalCopyright
        )
        {
            var data = CreateBookDom(topic: "Spiritual", originalCopyright: originalCopyright);
            Assert.AreEqual("Bible", data.GetVariableOrNull("topic", "en").Xml);
        }

        [Test]
        public void MigrateSpiritualTopic_TopicIsSpiritual_NoCopyrightOrOriginalCopyright_RemovesTopic()
        {
            var data = CreateBookDom(topic: "Spiritual");
            Assert.AreEqual(new MultiTextBase(), data.GetMultiTextVariableOrEmpty("topic"));
        }

        [Test]
        public void SynchronizeDataItemsThroughoutDOM_VariousTopicScenarios()
        {
            TestTopicHandling("Health", "fr", "Santé", "fr", "en", null, "Should use lang1");
            TestTopicHandling("Health", "fr", "Santé", "x", "fr", null, "Should use lang2");
            TestTopicHandling("Health", "fr", "Santé", "x", "y", "fr", "Should use lang3");
            TestTopicHandling("Health", "en", "Health", "x", "y", "z", "Should use English");
            TestTopicHandling("Health", "en", "Health", "en", "fr", "es", "Should use lang1");
            TestTopicHandling(
                "NoTopic",
                "",
                "",
                "en",
                "fr",
                "es",
                "'No Topic' should give no @lang and no text"
            );
            TestTopicHandling(
                "Bogus",
                "en",
                "Bogus",
                "z",
                "fr",
                "es",
                "Unrecognized topic should give topic in English"
            );
        }

        private void TestTopicHandling(
            string topicKey,
            string expectedLanguage,
            string expectedTranslation,
            string lang1,
            string lang2,
            string lang3,
            string description
        )
        {
            _collectionSettings.Language1.Tag = lang1;
            _collectionSettings.Language2.Tag = lang2;
            _collectionSettings.Language3.Tag = lang3;

            var bookDom = new HtmlDom(
                @"<html><body>
				<div id='bloomDataDiv'>
						<div data-book='topic' lang='en'>"
                    + topicKey
                    + @"</div>
				</div>
				<div id='somePage'>
                    <div id='test' data-derived='topic'>
					</div>
                </div>
			 </body></html>"
            );
            var data = new BookData(bookDom, _collectionSettings, null);
            data.SynchronizeDataItemsThroughoutDOM();
            try
            {
                if (string.IsNullOrEmpty(expectedLanguage))
                {
                    AssertThatXmlIn
                        .Dom(bookDom.RawDom)
                        .HasSpecifiedNumberOfMatchesForXpath(
                            "//div[@id='test' and @data-derived='topic' and not(@lang) and text()='"
                                + expectedTranslation
                                + "']",
                            1
                        );
                }
                else
                {
                    AssertThatXmlIn
                        .Dom(bookDom.RawDom)
                        .HasSpecifiedNumberOfMatchesForXpath(
                            "//div[@id='test' and @data-derived='topic' and @lang='"
                                + expectedLanguage
                                + "' and text()='"
                                + expectedTranslation
                                + "']",
                            1
                        );
                }
            }
            catch (Exception)
            {
                Assert.Fail(description);
            }
        }

        /// <summary>
        /// we use English as the one and only "key" language for topics in the datadiv
        /// </summary>
        [Test]
        public void SynchronizeDataItemsThroughoutDOM_HasMultipleTopicItems_RemovesAllButEnglish()
        {
            var bookDom = new HtmlDom(
                @"<html><body>
				<div id='bloomDataDiv'>
						<div data-book='topic' lang='en'>Health</div>
						<div data-book='topic' lang='fr'>Santé</div>
				</div>
			 </body></html>"
            );
            var data = new BookData(bookDom, _collectionSettings, null);
            data.SynchronizeDataItemsThroughoutDOM();
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasNoMatchForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='topic' and @lang='fr']"
                );
        }

        [Test]
        public void SynchronizeDataItemsThroughoutDOM_TopicHasParagraphElement_Removed()
        {
            var bookDom = new HtmlDom(
                @"<html><body>
				<div id='bloomDataDiv'>
						<div data-book='topic' lang='en'><p>Health</p></div>
				</div>
			 </body></html>"
            );
            var data = new BookData(bookDom, _collectionSettings, null);
            data.SynchronizeDataItemsThroughoutDOM();
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@data-derived='topic' and @lang='en']/p",
                    0
                );
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='topic' and @lang='en' and text()='Health']",
                    1
                );
        }

        [Test]
        public void SynchronizeDataItemsThroughoutDOM_LanguagesOfBookHasUnicode_DataSetLoadedCorrectly()
        {
            var bookDom = new HtmlDom(
                @"<html><body>
				<div id='bloomDataDiv'>
					<div data-book='languagesOfBook' lang='*'>Hakö</div>
				</div>
				<div class='bloom-page titlePage'>
					<div class='languagesOfBook' data-derived='languagesOfBook' lang='en'>PlaceholderThatWillGetUpdated</div>
				</div>
				</body></html>"
            );

            var data = new BookData(bookDom, _collectionSettings, null);

            // For BL-9972, the languagesOfBook key was somehow set again via XMLString (which generates the entity reference version of "ö".
            // Not 100% sure what normally organically causes this to happen,
            // but we'll just manually execute this to get the repro.
            data.Set("languagesOfBook", XmlString.FromUnencoded("Hakö"), false);

            // System Under Test
            data.SynchronizeDataItemsThroughoutDOM();

            // Verification
            var titlePageNode = bookDom.RawDom.SelectSingleNode(
                "//div[@data-derived='languagesOfBook']"
            );
            Assert.AreEqual("Hakö", titlePageNode.InnerText);
        }

        [Test]
        public void SetupDisplayOfLanguagesOfBook_XmatterHasNoLanguagesOfBookField_LanguagesOfBookInDataDivStaysLanguageNeutralAndSingular()
        {
            var bookDom = new HtmlDom(
                @"<html><body>
				<div id='bloomDataDiv'>
					<div data-book='languagesOfBook' lang='*'>English</div>
				</div>
				</body></html>"
            );

            var data = new BookData(bookDom, _collectionSettings, null);

            // System Under Test
            data.SetupDisplayOfLanguagesOfBook();

            // Verification
            var dataDivLanguagesOfBookNodes = bookDom.RawDom.SafeSelectNodes(
                "//div[@id='bloomDataDiv']//div[@data-book='languagesOfBook']"
            );
            // The live bug was also creating multiple copies of the languagesOfBook element.
            // I can't figure out how to make that happen in a test, but the cause of it getting the wrong lang attribute
            // and multiple copies was the same.
            Assert.That(dataDivLanguagesOfBookNodes.Length, Is.EqualTo(1));
            Assert.That(dataDivLanguagesOfBookNodes[0].GetAttribute("lang"), Is.EqualTo("*"));
            Assert.AreEqual("English", dataDivLanguagesOfBookNodes[0].InnerText);
        }

        private bool AndikaNewBasicIsInstalled()
        {
            const string fontToCheck = "andika new basic";
            return FontFamily.Families.FirstOrDefault(f => f.Name.ToLowerInvariant() == fontToCheck)
                != null;
        }

        [Test]
        public void OneTimeCheckVersionNumber_AndikaNewBasicMigration_alreadyDone()
        {
            var filepath = _collectionSettings.SettingsFilePath;
            WriteSettingsFile(filepath, _postAndikaMigrationCollection);

            // SUT
            _collectionSettings.Load();

            // Verify
            var font1 = _collectionSettings.Language1.FontName;
            var oneTimeCheckVersion = _collectionSettings.OneTimeCheckVersionNumber;
            Assert.That(Convert.ToInt32(oneTimeCheckVersion).Equals(1));
            Assert.That(font1.Equals("Andika New Basic"));
        }

        [Test]
        public void OneTimeCheckVersionNumber_AndikaNewBasicMigration_doneUserReverted()
        {
            var filepath = _collectionSettings.SettingsFilePath;
            WriteSettingsFile(filepath, _postAndikaMigrationCollectionNoANB);

            // SUT
            _collectionSettings.Load();

            // Verify
            var font1 = _collectionSettings.Language1.FontName;
            var oneTimeCheckVersion = _collectionSettings.OneTimeCheckVersionNumber;
            Assert.That(Convert.ToInt32(oneTimeCheckVersion).Equals(1));
            Assert.That(font1.Equals("Andika"));
        }

        private void WriteSettingsFile(string filepath, string xmlString)
        {
            File.WriteAllText(filepath, xmlString);
        }

        #region Collection Settings test data

        private const string _preAndikaMigrationCollection =
            @"﻿<?xml version='1.0' encoding='utf-8'?>
			<Collection version='0.2'>
				<Language1Name>Tok Pisin</Language1Name>
				<Language1Iso639Code>tpi</Language1Iso639Code>
				<Language2Iso639Code>en</Language2Iso639Code>
				<Language3Iso639Code>ara</Language3Iso639Code>
				<DefaultLanguage1FontName>Andika</DefaultLanguage1FontName>
				<DefaultLanguage2FontName>Andika</DefaultLanguage2FontName>
				<DefaultLanguage3FontName>Andika</DefaultLanguage3FontName>
				<IsLanguage1Rtl>false</IsLanguage1Rtl>
				<IsLanguage2Rtl>false</IsLanguage2Rtl>
				<IsLanguage3Rtl>true</IsLanguage3Rtl>
				<XMatterPack>Factory</XMatterPack>
				<Country></Country>
				<Province></Province>
				<District></District>
				<AllowNewBooks>True</AllowNewBooks>
			</Collection>";

        private const string _postAndikaMigrationCollection =
            @"﻿<?xml version='1.0' encoding='utf-8'?>
			<Collection version='0.2'>
				<Language1Name>Tok Pisin</Language1Name>
				<Language1Iso639Code>tpi</Language1Iso639Code>
				<Language2Iso639Code>en</Language2Iso639Code>
				<Language3Iso639Code>ara</Language3Iso639Code>
				<DefaultLanguage1FontName>Andika New Basic</DefaultLanguage1FontName>
				<DefaultLanguage2FontName>Andika New Basic</DefaultLanguage2FontName>
				<DefaultLanguage3FontName>Andika New Basic</DefaultLanguage3FontName>
				<OneTimeCheckVersionNumber>1</OneTimeCheckVersionNumber>
				<IsLanguage1Rtl>false</IsLanguage1Rtl>
				<IsLanguage2Rtl>false</IsLanguage2Rtl>
				<IsLanguage3Rtl>true</IsLanguage3Rtl>
				<XMatterPack>Factory</XMatterPack>
				<Country></Country>
				<Province></Province>
				<District></District>
				<AllowNewBooks>True</AllowNewBooks>
			</Collection>";

        private const string _postAndikaMigrationCollectionNoANB =
            @"﻿<?xml version='1.0' encoding='utf-8'?>
			<Collection version='0.2'>
				<Language1Name>Tok Pisin</Language1Name>
				<Language1Iso639Code>tpi</Language1Iso639Code>
				<Language2Iso639Code>en</Language2Iso639Code>
				<Language3Iso639Code>ara</Language3Iso639Code>
				<DefaultLanguage1FontName>Andika</DefaultLanguage1FontName>
				<DefaultLanguage2FontName>Andika</DefaultLanguage2FontName>
				<DefaultLanguage3FontName>Andika</DefaultLanguage3FontName>
				<OneTimeCheckVersionNumber>1</OneTimeCheckVersionNumber>
				<IsLanguage1Rtl>false</IsLanguage1Rtl>
				<IsLanguage2Rtl>false</IsLanguage2Rtl>
				<IsLanguage3Rtl>true</IsLanguage3Rtl>
				<XMatterPack>Factory</XMatterPack>
				<Country></Country>
				<Province></Province>
				<District></District>
				<AllowNewBooks>True</AllowNewBooks>
			</Collection>";

        #endregion

        private static string GetXpathForContributionsInLang(string lang)
        {
            // The old xpath used in most of these tests was getting datadiv results, not bloom-page results,
            // but the messages on most Asserts made it clear it was supposed to be testing the bloom-page contents.
            // This xpath tests the bloom-page contents, not the datadiv
            return "//div[@id='originalContributions']/div[@data-book='originalContributions' and @lang='"
                + lang
                + "']";
        }

        [Test]
        public void SynchronizeDataItemsThroughoutDOM_HasOnlyEnglishContributorsButEnglishIsLang3_CopiesEnglishIntoNationalLanguageSlot()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
					 <div data-book='originalContributions' lang='en'>the contributions</div>
				</div>
				<div class='bloom-page verso'>
					 <div id='originalContributions' class='bloom-translationGroup'>
						<div class='bloom-copyFromOtherLanguageIfNecessary' data-book='originalContributions' lang='fr'></div>
						<div class='bloom-copyFromOtherLanguageIfNecessary' data-book='originalContributions' lang='en'></div>
					</div>
				</div>
				</body></html>"
            );
            var collectionSettings = CreateCollection(Language1LangTag: "etr", Language2Tag: "fr");
            var data = new BookData(dom, collectionSettings, null);
            data.SynchronizeDataItemsThroughoutDOM();
            var englishContributions = dom.SelectSingleNodeHonoringDefaultNS(
                GetXpathForContributionsInLang("en")
            );
            Assert.AreEqual(
                "the contributions",
                englishContributions.InnerText,
                "Should copy English into body of course, as normal"
            );
            var frenchContributions = dom.SelectSingleNodeHonoringDefaultNS(
                GetXpathForContributionsInLang("fr")
            );
            Assert.AreEqual(
                "the contributions",
                frenchContributions.InnerText,
                "Should copy English into French Contributions becuase it's better than just showing nothing"
            );
        }

        [Test]
        public void SynchronizeDataItemsThroughoutDOM_HasOnlyEnglishContributorsInDataDivButFrenchInBody_DoesNotCopyEnglishIntoFrenchSlot()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
					 <div data-book='originalContributions' lang='en'>the contributions</div>
				</div>
				<div class='bloom-page verso'>
					 <div id='originalContributions' class='bloom-translationGroup'>
						<div data-book='originalContributions' lang='fr'>les contributeurs</div>
						<div data-book='originalContributions' lang='xyz'></div>
					</div>
				</div>
				</body></html>"
            );
            var collectionSettings = CreateCollection(Language1LangTag: "etr", Language2Tag: "fr");
            var data = new BookData(dom, collectionSettings, null);
            data.SynchronizeDataItemsThroughoutDOM();
            var frenchContributions = dom.SelectSingleNodeHonoringDefaultNS(
                GetXpathForContributionsInLang("fr")
            );
            Assert.AreEqual(
                "les contributeurs",
                frenchContributions.InnerText,
                "Should not touch existing French Contributions"
            );
        }

        [Test]
        public void SynchronizeDataItemsThroughoutDOM_HasFrenchAndEnglishContributorsInDataDiv_DoesNotCopyEnglishIntoFrenchSlot()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
					 <div data-book='originalContributions' lang='en'>the contributions</div>
					<div data-book='originalContributions' lang='fr'>les contributeurs</div>
				</div>
				<div class='bloom-page verso'>
					 <div id='originalContributions' class='bloom-translationGroup'>
						<div data-book='originalContributions' lang='fr'></div>
						<div data-book='originalContributions' lang='xyz'></div>
					</div>
				</div>
				</body></html>"
            );
            var collectionSettings = CreateCollection(Language1LangTag: "xyz", Language2Tag: "fr");
            var data = new BookData(dom, collectionSettings, null);
            data.SynchronizeDataItemsThroughoutDOM();
            var frenchContributions = dom.SelectSingleNodeHonoringDefaultNS(
                GetXpathForContributionsInLang("fr")
            );
            Assert.AreEqual(
                "les contributeurs",
                frenchContributions.InnerText,
                "Should use the French, not the English even though the French in the body was empty"
            );
            var vernacularContributions = dom.SelectSingleNodeHonoringDefaultNS(
                GetXpathForContributionsInLang("xyz")
            );
            Assert.AreEqual(
                "",
                vernacularContributions.InnerText,
                "Should not copy Edolo into Vernacular Contributions. Only national language fields get this treatment"
            );
        }

        [Test]
        public void SynchronizeDataItemsThroughoutDOM_HasOnlyEdoloContributors_CopiesItIntoL2ButNotL1()
        {
            // empty french datadiv element, but has self-closing paragraph tag
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
					 <div data-book='originalContributions' lang='etr'><p>the contributions</p></div>
					 <div data-book='originalContributions' lang='fr'><p /></div>
				</div>
				<div class='bloom-page verso'>
					 <div id='originalContributions' class='bloom-translationGroup'>
						<div class='bloom-copyFromOtherLanguageIfNecessary' data-book='originalContributions' lang='fr'></div>
						<div class='bloom-copyFromOtherLanguageIfNecessary' data-book='originalContributions' lang='xyz'></div>
					</div>
				</div>
				</body></html>"
            );
            var collectionSettings = CreateCollection(Language1LangTag: "xyz", Language2Tag: "fr");
            var data = new BookData(dom, collectionSettings, null);
            data.SynchronizeDataItemsThroughoutDOM();
            var frenchContributions = dom.SelectSingleNodeHonoringDefaultNS(
                GetXpathForContributionsInLang("fr")
            );
            Assert.AreEqual(
                "the contributions",
                frenchContributions.InnerText,
                "Should copy Edolo into French Contributions because it's better than just showing nothing"
            );
            var vernacularContributions = dom.SelectSingleNodeHonoringDefaultNS(
                GetXpathForContributionsInLang("xyz")
            );
            Assert.AreEqual(
                "",
                vernacularContributions.InnerText,
                "Should not copy Edolo into Vernacular Contributions. Only national language fields get this treatment"
            );
        }

        [Test]
        public void SynchronizeDataItemsThroughoutDOM_HasOnlyFrenchContributors_CopiesItIntoEnglishIfNoEnglish()
        {
            // empty english datadiv element, but has empty paragraph tag
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
					 <div data-book='originalContributions' lang='fr'><p>les contributeurs</p></div>
					 <div data-book='originalContributions' lang='en'><p></p></div>
				</div>
				<div class='bloom-page verso'>
					 <div id='originalContributions' class='bloom-translationGroup'>
						<div class='bloom-copyFromOtherLanguageIfNecessary' data-book='originalContributions' lang='en'><p></p></div>
						<div class='bloom-copyFromOtherLanguageIfNecessary' data-book='originalContributions' lang='xyz'></div>
					</div>
				</div>
				</body></html>"
            );
            var collectionSettings = CreateCollection(Language1LangTag: "xyz", Language2Tag: "en");
            var data = new BookData(dom, collectionSettings, null);
            data.SynchronizeDataItemsThroughoutDOM();
            var englishContributions = dom.SelectSingleNodeHonoringDefaultNS(
                GetXpathForContributionsInLang("en")
            );
            Assert.AreEqual(
                "les contributeurs",
                englishContributions.InnerText,
                "Should copy French into English Contributions because it's better than just showing nothing"
            );
            var vernacularContributions = dom.SelectSingleNodeHonoringDefaultNS(
                GetXpathForContributionsInLang("xyz")
            );
            Assert.AreEqual(
                "",
                vernacularContributions.InnerText,
                "Should not copy Edolo into Vernacular Contributions. Only national language fields get this treatment"
            );
        }

        [TestCase("", ExpectedResult = true)]
        [TestCase("  \t  ", ExpectedResult = true)]
        [TestCase("<p></p>", ExpectedResult = true)]
        [TestCase("Bob", ExpectedResult = false)]
        [TestCase("<p>Bob</p>", ExpectedResult = false)]
        [TestCase("<p/><p />", ExpectedResult = true)]
        [TestCase("<br></br>", ExpectedResult = true)]
        [TestCase("<br/>Bob", ExpectedResult = false)]
        [TestCase("<br />", ExpectedResult = true)]
        [TestCase("  <p> </p>  ", ExpectedResult = true)]
        //[TestCase("\xFEFF", ExpectedResult = true)] // non-breaking zero-width Unicode character
        public bool StringAlternativeHasNoText_VariousCases(string input)
        {
            return BookData.StringAlternativeHasNoText(input);
        }

        private static string NewLines
        {
            get
            {
                return Environment.NewLine
                    + "  "
                    + Environment.NewLine
                    + "  "
                    + Environment.NewLine;
            }
        }

        // A separate test was required, since I wanted to use Environment.NewLine and the
        // TestCase attribute requires a constant string.
        [Test]
        public void StringAlternativeHasNoText_NewLines()
        {
            Assert.IsTrue(BookData.StringAlternativeHasNoText(NewLines));
        }

        /// <summary>
        /// BL-3078 where when xmatter was injected and updated, the text stored in data-book overwrote
        /// the innerxml of the div, knocking out the <label></label> in there, which lead to losing
        /// the side bubbles explaining what the field was for.
        /// </summary>
        [Test]
        public void SynchronizeDataItemsThroughoutDOM_EditableHasLabelElement_LabelPreserved()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
					 <div data-book='insideBackCover' lang='en'><p/></div>
				</div>
				<div class='bloom-page'>
					 <div id='foo' class='bloom-content1 bloom-editable' data-book='insideBackCover' lang='en'>
						<label>some label</label>
					</div>
				</div>
				</body></html>"
            );
            var data = new BookData(dom, _collectionSettings, null);
            data.SynchronizeDataItemsThroughoutDOM();
            var foo = (SafeXmlElement)dom.SelectSingleNodeHonoringDefaultNS("//*[@id='foo']");
            Assert.That(foo.InnerXml, Contains.Substring("<label>some label</label>"));
        }

        /// <summary>
        /// BL-9460 we had to reintroduce the ability to set the width & height of the cover image, instead of relying on object-fit:cover
        /// </summary>
        [TestCase("bloom-scale-with-code", true)]
        [TestCase("", false)]
        public void SynchronizeDataItemsThroughoutDOM_ParentHasSpecialClassAndImageHasStyle_StyleCopiedToImg(
            string containerClass,
            bool expectStyle
        )
        {
            var dom = new HtmlDom(
                $@"<html ><head></head><body>
				<div id='bloomDataDiv'>
					<div  data-book='coverImage' src='new.png' style='width: 233.719px; height:100%;'>new.png</div>
				</div>
				<div class='bloom-page'>
					 <div class='bloom-canvas {containerClass}'>
						<img data-book='coverImage' src='placeholder.png' ></img>
					</div>
				</div>
				</body></html>"
            );
            var data = new BookData(dom, _collectionSettings, null);
            data.SynchronizeDataItemsThroughoutDOM();
            var dataDivImage = (SafeXmlElement)
                dom.SelectSingleNodeHonoringDefaultNS("//img[@data-book='coverImage']");
            Assert.That(dataDivImage.GetAttribute("src"), Contains.Substring("new.png"));
            if (expectStyle)
                Assert.That(dataDivImage.GetAttribute("style"), Contains.Substring("height:100%"));
            else
            {
                Assert.That(dataDivImage.GetAttribute("style"), Is.Empty);
            }
        }

        //  BL-9460 Same as the above ( SynchronizeDataItemsThroughoutDOM_ParentHasSpecialClassAndImageHasStyle_StyleCopiedToImg() ), but in the other direction
        [TestCase("bloom-scale-with-code", true)]
        /* Review: we don't actually have code to prevent copying up to dataDiv [TestCase("", false)]*/
        public void SuckInDataFromEditedDom_ParentHasSpecialClassAndImageHasStyle_StyleCopiedToDataDiv(
            string containerClass,
            bool expectStyle
        )
        {
            var bookDom = new HtmlDom(
                $@"<html ><head></head><body>
				<div id='bloomDataDiv'>
					<div  data-book='coverImage' src='old.png'>old.png</div>
				</div>
				<div class='bloom-page'>
					 <div class='bloom-canvas {containerClass}'>
						<img data-book='coverImage' src='old.png'></img>
					</div>
				</div>
				</body></html>"
            );
            var data = new BookData(bookDom, _collectionSettings, null);

            var editedPageDom = new HtmlDom(
                $@"<html ><head></head><body>
				<div class='bloom-page'>
					 <div class='bloom-canvas {containerClass}'>
						<img data-book='coverImage' src='new.png' style='width: 233.719px; height:100%;'></img>
					</div>
				</div>
			 </body></html>"
            );

            data.SuckInDataFromEditedDom(editedPageDom);
            var dataDivImage = bookDom.SelectSingleNodeHonoringDefaultNS(
                "//div[@id='bloomDataDiv']/div[@data-book='coverImage']"
            );
            Assert.That(dataDivImage.GetAttribute("src"), Is.EqualTo("new.png"));

            if (expectStyle)
            {
                Assert.That(dataDivImage.GetAttribute("style"), Contains.Substring("height:100%"));
            }
            else
            {
                Assert.That(dataDivImage.GetAttribute("style"), Is.Empty);
            }
        }

        [Test]
        public void SynchronizeDataItemsThroughoutDOM_CopiesTextBoxAudioData_ButNotJunkData_RemovesUnwantedItems()
        {
            // This is not very realistic. We expect that the junk attributes (aria-label, role, spellcheck) will NOT ever get into the bloomDataDiv.
            // But we're trying to test what happens when they are present on the source element that SynchronizeDataItemsThroughoutDOM
            // copies FROM, and that is the FIRST element it encounters in the document with a given data-book.
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
					 <div data-book='bookTitle' lang='en' data-duration='5.839433' id='i6a720491' data-audiorecordingmode='TextBox' aria-describedby='qtip-3' aria-label='false' role='textbox' spellcheck='true' tabindex='0' data-hasqtip='true' data-languagetipcontent='English' class='bloom-editable bloom-nodefaultstylerule testClass Title-On-Cover-style bloom-padForOverflow audio-sentence bloom-content1 bloom-visibility-code-on'><p>something</p></div>
				</div>
				<div class='bloom-page'>
					 <div findMe='foo' data-book='bookTitle' data-duration='1.0' lang='en' keepMe='keep me' aria-describedby='qtip-5' data-audiorecordingendtimes='1.640 4.640' class='bloom-editable bloom-nodefaultstylerule bloom-postAudioSplit Title-On-Title-style bloom-padForOverflow'><p/></div>
				</div>
				</body></html>"
            );
            var data = new BookData(dom, _collectionSettings, null);
            data.SynchronizeDataItemsThroughoutDOM();
            var foo = (SafeXmlElement)dom.SelectSingleNodeHonoringDefaultNS("//*[@findMe='foo']");
            // an attribute that isn't present on the destination
            Assert.That(foo.GetAttribute("id"), Is.EqualTo("i6a720491"));
            // an attribute that needs to be overwritten.
            Assert.That(foo.GetAttribute("data-duration"), Is.EqualTo("5.839433"));
            // One that should not be copied
            Assert.That(foo.GetOptionalStringAttribute("tabindex", null), Is.Null);
            // One that should be removed
            Assert.That(
                foo.GetOptionalStringAttribute("data-audiorecordingendtimes", null),
                Is.Null
            );
            // One that should not be overwritten (though more commonly it wouldn't be in the destination)
            Assert.That(foo.GetAttribute("aria-describedby"), Is.EqualTo("qtip-5"));
            // One that is not in the source and should be left alone
            Assert.That(foo.GetAttribute("keepMe"), Is.EqualTo("keep me"));

            var classes = new HashSet<string>(foo.GetAttribute("class").Split());
            // a class that should be added to the destination
            Assert.That(classes, Does.Contain("testClass"));
            // Classes that should not be added
            Assert.That(classes, Does.Not.Contain("bloom-content1"));
            Assert.That(classes, Does.Not.Contain("bloom-visibility-code-on"));
            // Some important ones that should not be messed with
            Assert.That(classes, Does.Contain("bloom-editable"));
            Assert.That(classes, Does.Contain("bloom-padForOverflow"));
            // Style classes are specific to locations in the book
            Assert.That(classes, Does.Not.Contain("Title-On-Cover-style"));
            Assert.That(classes, Does.Contain("Title-On-Title-style"));
            // Classes that should be removed
            Assert.That(classes, Does.Not.Contain("bloom-postAudioSplit"));
        }

        [Test]
        public void SynchronizeDataItemsThroughoutDOM_DoesNotRemoveRemovableAttrsAndClasses_IfInSource()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
					 <div data-book='bookTitle' lang='en' data-duration='5.839433' id='i6a720491' data-audiorecordingmode='TextBox' data-audiorecordingendtimes='1.640 4.640' aria-describedby='qtip-3' aria-label='false' role='textbox' spellcheck='true' tabindex='0' data-hasqtip='true' data-languagetipcontent='English' class='bloom-editable bloom-postAudioSplit bloom-nodefaultstylerule testClass Title-On-Cover-style bloom-padForOverflow audio-sentence bloom-content1 bloom-visibility-code-on'><p>something</p></div>
				</div>
				<div class='bloom-page'>
					 <div findMe='foo' data-book='bookTitle' data-duration='1.0' lang='en' keepMe='keep me' aria-describedby='qtip-5' data-audiorecordingendtimes='2.7' class='bloom-editable bloom-postAudioSplit bloom-nodefaultstylerule Title-On-Title-style bloom-padForOverflow'><p/></div>
				</div>
				<div class='bloom-page'>
					 <div findMe='foo2' data-book='bookTitle' data-duration='1.0' lang='en' keepMe='keep me' aria-describedby='qtip-5' class='bloom-editable bloom-nodefaultstylerule Title-On-Title-style bloom-padForOverflow'><p/></div>
				</div>

				</body></html>"
            );
            var data = new BookData(dom, _collectionSettings, null);
            data.SynchronizeDataItemsThroughoutDOM();
            var foo = (SafeXmlElement)dom.SelectSingleNodeHonoringDefaultNS("//*[@findMe='foo']");
            var foo2 = (SafeXmlElement)dom.SelectSingleNodeHonoringDefaultNS("//*[@findMe='foo2']");
            // an attribute that is potentially deletable, but present in source, gets copied to destination, overwriting old value
            Assert.That(foo.GetAttribute("data-audiorecordingendtimes"), Is.EqualTo("1.640 4.640"));
            // also copied to destination that previously didn't have it.
            Assert.That(
                foo2.GetAttribute("data-audiorecordingendtimes"),
                Is.EqualTo("1.640 4.640")
            );

            var classes = new HashSet<string>(foo.GetAttribute("class").Split());
            var classes2 = new HashSet<string>(foo2.GetAttribute("class").Split());
            // a class that is potentially deletable, but present in source and destination, is kept
            Assert.That(classes, Does.Contain("bloom-postAudioSplit"));
            // a class that is potentially deletable, present in source but not destination, is added
            Assert.That(classes2, Does.Contain("bloom-postAudioSplit"));
        }

        [Test]
        public void GatherDataItemsFromXElement_OmitsDataPageNumber()
        {
            var dom = new HtmlDom(
                @"<html ><head></head><body>
				<div id='bloomDataDiv'>
					<div data-xmatter-page='insideBackCover' data-page='required singleton' data-export='back-matter-inside-back-cover' data-page-number='3'></div>
 					<div data-xmatter-page='outsideBackCover' data-page='required singleton' data-export='back-matter-back-cover' data-page-number=''></div>
				</div>
				<div class='bloom-page'>
					 <div id='foo' class='bloom-content1 bloom-editable' data-book='insideBackCover' lang='en'>
						<label>some label</label>
					</div>
				</div>
				</body></html>"
            );
            var data = new BookData(dom, _collectionSettings, null);

            var pageNumber = data.GetXmatterPageDataAttributeValue(
                "insideBackCover",
                "data-page-number"
            );
            var dataPage = data.GetXmatterPageDataAttributeValue("insideBackCover", "data-page");
            Assert.That(pageNumber, Is.EqualTo(""));
            Assert.That(dataPage, Is.EqualTo("required singleton"));
            pageNumber = data.GetXmatterPageDataAttributeValue(
                "outsideBackCover",
                "data-page-number"
            );
            dataPage = data.GetXmatterPageDataAttributeValue("outsideBackCover", "data-page");
            Assert.That(pageNumber, Is.EqualTo(""));
            Assert.That(dataPage, Is.EqualTo("required singleton"));
        }

        public static CollectionSettings CreateCollection(
            string Language1LangTag = "tpi",
            string Language1Name = "Tok Pisin",
            string Language2Tag = "fr",
            string Language2Name = "French",
            string Language3Tag = "en",
            string Language3Name = "English",
            string CountryName = null
        )
        {
            var c = new CollectionSettings();
            if (Language1LangTag != null)
            {
                c.Language1.Tag = Language1LangTag;
            }

            if (Language1Name != null)
            {
                c.Language1.SetName(Language1Name, false);
            }

            if (Language2Tag != null)
            {
                c.Language2.Tag = Language2Tag;
            }

            if (Language2Name != null)
            {
                c.Language2.SetName(Language2Name, false);
            }

            if (Language3Tag != null)
            {
                c.Language3.Tag = Language3Tag;
            }

            if (Language3Name != null)
            {
                c.Language3.SetName(Language3Name, false);
            }

            if (CountryName != null)
            {
                c.Country = CountryName;
            }

            return c;
        }
    }
}
