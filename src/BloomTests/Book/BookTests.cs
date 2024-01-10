using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web;
using System.Xml;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Publish;
using Bloom.web.controllers;
using BloomTemp;
using Moq;
using NUnit.Framework;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;
using SIL.Windows.Forms.ClearShare;
using SIL.Xml;

namespace BloomTests.Book
{
    [TestFixture]
    public class BookTests : BookTestsBase
    {
        /// <summary>
        /// this test is weak... it doesn't *really* tell us that the preview will look right (e.g., that
        /// the css will be properly found, based on the <base></base>, etc.)
        /// </summary>
        [Test]
        public void GetPreviewHtmlFileForWholeBook_what_UsesPreviewCss()
        {
            Assert.IsTrue(
                CreateBook().GetPreviewHtmlFileForWholeBook().InnerXml.Contains("previewMode.css")
            );
        }

        [Test]
        public void GetPreviewHtmlFileForWholeBook_BookHasThreePages_ResultHasAll()
        {
            var result = CreateBook().GetPreviewHtmlFileForWholeBook().RawDom.StripXHtmlNameSpace();
            AssertThatXmlIn
                .Dom(result)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'bloom-page') and not(contains(@class,'bloom-frontMatter') or contains(@class,'bloom-backMatter') )]",
                    3
                );
        }

        [Test]
        public void GetPreviewHtmlFileForWholeBook_BookHasVideo_PreviewMarksItPreloadNone()
        {
            var htmlSourceBook =
                $@"<html><head></head><body>
					<div class='bloom-page numberedPage' id='page1' data-page-number='1'>
						<div class='bloom-videoContainer'>
							<video>
								<source src='video/fakeVideo.mp4'>
								</source>
							</video>
						</div>
					</div>
				</body></html>";
            var doc = new XmlDocument();
            doc.LoadXml(htmlSourceBook);
            var dom = new HtmlDom(doc);
            _storage.SetupGet(x => x.Dom).Returns(() => dom);

            // System Under Test
            var result = CreateBook().GetPreviewHtmlFileForWholeBook();

            // Verification
            AssertThatXmlIn
                .Dom(result.RawDom.StripXHtmlNameSpace())
                .HasSpecifiedNumberOfMatchesForXpath("//video[@preload='none']", 1);
        }

        [Test]
        public void SetCoverColor_WorksWithCaps()
        {
            var newValue = "#777777";
            SetDom(
                "",
                @"<style type='text/css'>
				</style>
				<style type='text/css'>
					DIV.coverColor  TEXTAREA {
						background-color: #B2CC7D !important;
					}
					DIV.bloom-page.coverColor {
						background-color: #B2CC7D !important;
					}
				</style>
				<style type='text/css'>
				</style>"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            book.SetCoverColorInternal(newValue);
            var coverColorText = dom.SafeSelectNodes("//style[text()]")[0].InnerText;
            var first = coverColorText.IndexOf(newValue, StringComparison.InvariantCulture);
            var last = coverColorText.LastIndexOf(newValue, StringComparison.InvariantCulture);
            Assert.That(first > 0);
            Assert.That(last > 0 && last != first);
        }

        [Test]
        public void SetCoverColor_WorksWithLowercase()
        {
            var newValue = "#777777";
            SetDom(
                "",
                @"<style type='text/css'>
				</style>
				<style type='text/css'>
					div.coverColor  textarea {
						background-color: #B2CC7D !important;
					}
					div.bloom-page.coverColor {
						background-color: #B2CC7D !important;
					}
				</style>
				<style type='text/css'>
				</style>"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            book.SetCoverColorInternal(newValue);
            var coverColorText = dom.SafeSelectNodes("//style[text()]")[0].InnerText;
            var first = coverColorText.IndexOf(newValue, StringComparison.InvariantCulture);
            var last = coverColorText.LastIndexOf(newValue, StringComparison.InvariantCulture);
            Assert.That(first > 0);
            Assert.That(last > 0 && last != first);
        }

        [Test]
        public void BringBookUpToDate_EmbeddedXmlImgTagRemoved()
        {
            // Some older books had XML img tags inside the coverImage data-book value. This resulted in an
            // html-encoded background-image url with XML inside it.
            // BL-4586 and old Thai Big Book had this in it. Need to handle it for backwards compatibility.
            const string imgTag =
                "<img style='width: 360px;' src='myImage.png' height='360' alt='missing'></img>";
            SetDom(
                @"<div id='bloomDataDiv'>
						<div data-book='coverImage' lang='*'>
							"
                    + imgTag
                    + @"
						</div>
					</div>
					<div class='bloom-page bloom-frontMatter'>
						<div class='marginBox'>
							<div class='bloom-imageContainer' data-book='coverImage'>
								"
                    + imgTag
                    + @"
							</div>
						</div>
					</div>"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            book.BringBookUpToDate(new NullProgress());
            var dataBookImage = dom.SelectSingleNodeHonoringDefaultNS(
                "//div[@id='bloomDataDiv']/div[@data-book='coverImage']"
            );
            Assert.AreEqual("myImage.png", dataBookImage.InnerText);
            var pageImage = dom.SelectSingleNodeHonoringDefaultNS(
                "//div[contains(@class,'bloom-imageContainer')]/img[@data-book='coverImage']"
            );
            Assert.IsTrue(pageImage.Attributes["src"].Value.Equals("myImage.png"));
        }

        [Test]
        public void BringBookUpToDate_DataCkeTempRemoved()
        {
            // Some books got corrupted with CKE temp data, possibly before we prevented this happening when
            // pasting HTML (e.g., from Word). This tests that we clean it up.
            SetDom(
                @"<div class='bloom-page numberedPage customPage A5Portrait'>
						<div id='testDiv' class='marginBox'>
							<div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement normal-style'>
								<div class='bloom-editable normal-style cke_focus bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='xyz'>
									<div data-cke-hidden-sel='1' data-cke-temp='1' style='position:fixed;top:0;left:-1000px' class='bloom-contentNational2'>
										<br />
									</div>
								</div>
							</div>
						</div>
					</div>"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            book.BringBookUpToDate(new NullProgress());
            //AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath("//div[@data-cke-hidden-sel]");
            AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath("//div[@id='testDiv']//br");
        }

        [Test]
        public void BringBookUpToDate_EmbeddedEmptyImgTagRemoved()
        {
            const string imgTag = "<img>bad tag contents</img>";
            const string placeHolderFile = "placeHolder.png";
            SetDom(
                @"<div id='bloomDataDiv'>
						<div data-book='coverImage' lang='*'>
							"
                    + imgTag
                    + @"
						</div>
					</div>
					<div class='bloom-page bloom-frontMatter'>
						<div class='marginBox'>
							<div class='bloom-imageContainer' data-book='coverImage'>
							</div>
						</div>
					</div>"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            book.BringBookUpToDate(new NullProgress());
            var dataBookImage = dom.SelectSingleNodeHonoringDefaultNS(
                "//div[@id='bloomDataDiv']/div[@data-book='coverImage']"
            );
            if (dataBookImage != null) // used to just set the src of the img, but removing the dataDiv element altogether is better still.
                Assert.AreEqual(placeHolderFile, dataBookImage.InnerText);
            var pageImage = dom.SelectSingleNodeHonoringDefaultNS(
                "//div[contains(@class,'bloom-imageContainer')]/img[@data-book='coverImage']"
            );
            Assert.IsTrue(pageImage.Attributes["src"].Value.Equals(placeHolderFile));
        }

        // Unless it's part of an image container that has an image description, an image
        // should have an alt attr that is exactly an empty string.
        [Test]
        public void BringBookUpToDate_AltNotImageDescription_SetEmpty()
        {
            SetDom(
                @"
					<div class='bloom-page bloom-frontMatter'>
						<div class='marginBox'>
							<img class='branding' src='title-page.svg' type='image/svg' alt='loading slowly'></img>
							<img class='licenseImage' src='license.png' data-derived='licenseImage' alt='License image'></img>
						</div>
					</div>
					<div data-book='title-page-branding-bottom-html'>
						<div class='marginBox'>
							<img src='imageWithCustomAlt.svg' type='image/svg' alt='Custom Alt'></img>
							<img src='title-page.svg' type='image/svg' alt='This picture, title-page.svg,  is missing or was loading too slowly'></img>
						</div>
					</div>
					<div class='bloom-page numberedPage customPage A5Portrait'>
						<div class='marginBox'>
							<img src='junk' alt = 'more junk'></img>
							<img src='rubbish'></img>
							<div style='min-height: 42px;' class='split-pane horizontal-percent'>
								<div title='aor_1B-E1.png' data-hasqtip='true' class='bloom-imageContainer bloom-leadingElement'>
									 <img data-license='cc-by-sa' data-creator='Susan Rose' data-copyright='Copyright SIL International 2009' src='aor_1B-E1.png' alt='This picture, aor_1B-E1.png, is missing or was loading too slowly.'></img>
								</div>
							</div>
						</div>
					</div>"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            book.BringBookUpToDate(new NullProgress());
            var images = dom.SelectNodes("//img");
            Assert.That(images, Has.Count.AtLeast(4)); // may get extras from xmatter update
            foreach (XmlElement img in images)
            {
                Assert.That(img.Attributes["alt"], Is.Not.Null);

                string expectedAltText = "";
                if (img.Attributes["src"].Value == "imageWithCustomAlt.svg")
                {
                    expectedAltText = "Custom Alt";
                }
                Assert.That(img.Attributes["alt"].Value, Is.EqualTo(expectedAltText));
            }
        }

        [Test]
        public void BringBookUpToDate_ImgWithDescription_CopiedToAlt()
        {
            SetDom(
                @"
					<div class='bloom-page numberedPage customPage A5Portrait'>
						<div class='marginBox'>
							<div style='min-height: 42px;' class='split-pane horizontal-percent'>
								<div title='aor_1B-E1.png' data-hasqtip='true' class='bloom-imageContainer'>
									 <img data-license='cc-by-sa' data-creator='Susan Rose' data-copyright='Copyright SIL International 2009' src='aor_1B-E1.png' alt='This picture, aor_1B-E1.png, is missing or was loading too slowly.'></img>
									<div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement normal-style'>
										<div class='bloom-editable normal-style cke_focus bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='xyz'>
											<p>Bird with wings stretched wide</p>
										</div>
									</div>
								</div>
							</div>
						</div>
					</div>"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            book.BringBookUpToDate(new NullProgress());
            var img = dom.SelectSingleNode(
                "//div[@class='bloom-imageContainer']/img[@src='aor_1B-E1.png']"
            );
            Assert.That(img.Attributes["alt"].Value, Is.EqualTo("Bird with wings stretched wide"));
        }

        [Test]
        public void BringBookUpToDate_EmbeddedEncodedXmlImgTagRemoved()
        {
            // I haven't seen this, but it could happen that the src attribute in the embedded img tag was html encoded.
            // So we'll make sure it works.
            const string imageFilename = "my ǆñImageﭳ.png";
            var encodedFilename = HttpUtility.UrlEncode(imageFilename);
            var noPlusEncodedName = encodedFilename.Replace("+", "%20");
            var imgTag =
                "<img style='width: 360px;' src='"
                + encodedFilename
                + "' height='360' alt='missing'></img>";
            SetDom(
                @"<div id='bloomDataDiv'>
						<div data-book='coverImage' lang='*'>
							"
                    + imgTag
                    + @"
						</div>
					</div>
					<div class='bloom-page bloom-frontMatter'>
						<div class='marginBox'>
							<div class='bloom-imageContainer' data-book='coverImage'>
							</div>
						</div>
					</div>"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            book.BringBookUpToDate(new NullProgress());
            var dataBookImage = dom.SelectSingleNodeHonoringDefaultNS(
                "//div[@id='bloomDataDiv']/div[@data-book='coverImage']"
            );
            Assert.AreEqual(imageFilename, dataBookImage.InnerText);
            var pageImage = dom.SelectSingleNodeHonoringDefaultNS(
                "//div[contains(@class,'bloom-imageContainer')]/img[@data-book='coverImage']"
            );
            Assert.IsTrue(pageImage.Attributes["src"].Value.Equals(noPlusEncodedName));

            // Test that obsolete img attributes are gone.
            Assert.IsNull(pageImage.GetOptionalStringAttribute("style", null));
            Assert.IsNull(pageImage.GetOptionalStringAttribute("height", null));
        }

        [Test]
        public void BringBookUpToDate_VernacularTitleChanged_TitleCopiedToTextAreaOnAnotherPage()
        {
            var book = CreateBook();
            var dom = book.RawDom; // book.GetEditableHtmlDomForPage(book.GetPages().First());
            var textarea1 = dom.SelectSingleNodeHonoringDefaultNS(
                "//textarea[@id='2' and @lang='xyz']"
            );
            textarea1.InnerText = "peace";
            book.BookData.SuckInDataFromEditedDom(
                new HtmlDom(dom.SelectSingleNode("//div[@id='guid1']").OuterXml)
            );
            book.BringBookUpToDate(new NullProgress());
            var textarea2 = dom.SelectSingleNodeHonoringDefaultNS(
                "//textarea[@idc='copyOfVTitle'  and @lang='xyz']"
            );
            Assert.AreEqual("peace", textarea2.InnerText);
        }

        [Test]
        public void BringBookUpToDate_RemovesObsoleteImageAttributes()
        {
            SetDom(
                @"<div class='bloom-page numberedPage' data-page='' id='0bc78841-6372-4e47-bacf-d11038d538d2' data-pagelineage='adcd48df-e9ab-4a07-afd4-6a24d0398382;87768516-08f4-41db-a33e-767322137e5a' lang='' data-page-number='1'>
  <div class='something-or-other'>
    <div title='The Moon and The Cap_Page 021.jpg' class='bloom-imageContainer bloom-leadingElement'>
      <img style='width: 404px; height: 334px; margin-left: 1px; margin-top: 0px;' data-license='cc-by' data-creator='Angie and Upesh' data-copyright='Copyright © 2007, Pratham Books' src='The%20Moon%20and%20The%20Cap_Page%20021.jpg' alt='' height='334' width='404' />
    </div>
    <div title='The Moon and The Cap_Page 021.jpg' class='bloom-imageContainer bloom-trailingElement'>
      <img data-license='cc-by' data-creator='Angie and Upesh' data-copyright='Copyright © 2007, Pratham Books' src='The%20Moon%20and%20The%20Cap_Page%20021.jpg' alt='' height='334' width='404' />
    </div>
    <div title='The Moon and The Cap_Page 021.jpg' class='bloom-imageContainer bloom-leadingElement'>
      <img style='width: 404px; height: 334px; padding: 3px; margin-left: 1px; margin-top: 0px;' data-license='cc-by' data-creator='Angie and Upesh' data-copyright='Copyright © 2007, Pratham Books' src='The%20Moon%20and%20The%20Cap_Page%20021.jpg' alt='' height='334' width='404' />
    </div>
    <div class='branding-Test'>
      <img style='margin-top: 1em;' data-license='cc-by' data-creator='Joe' data-copyright='© 2007, Joe' src='BrandingTest.png' alt='' />
    </div>
  </div>
</div>
"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            var imgsBefore = dom.SafeSelectNodes(
                    "//div[contains(@class,'bloom-imageContainer')]/img[@style|@width|@height]"
                )
                .Cast<XmlElement>();
            Assert.AreEqual(
                3,
                imgsBefore.Count(),
                "3 bloom-imageContainer images had style/size attributes"
            );
            imgsBefore = dom.SafeSelectNodes("//img[@style]").Cast<XmlElement>();
            Assert.AreEqual(3, imgsBefore.Count(), "3 images had style attributes");

            book.BringBookUpToDate(new NullProgress());

            var imgsAfter = dom.SafeSelectNodes(
                    "//div[contains(@class,'bloom-imageContainer')]/img[@style|@width|@height]"
                )
                .Cast<XmlElement>();
            Assert.AreEqual(
                1,
                imgsAfter.Count(),
                "1 bloom-imageContainer image has style/size attributes after updating"
            );
            var img = imgsAfter.First<XmlElement>();
            var updatedStyle = img.GetOptionalStringAttribute("style", null);
            Assert.AreEqual(
                "padding: 3px;",
                updatedStyle,
                "unrecognized style css items are preserved"
            );
            imgsAfter = dom.SafeSelectNodes("//img[@style]").Cast<XmlElement>();
            Assert.AreEqual(2, imgsAfter.Count(), "2 images have style attribute after updating");
        }

        [Test]
        public void UpdateTextsNewlyChangedToRequiresParagraph_HasOneBR()
        {
            SetDom(
                @"<div class='bloom-page'>
						<div id='somewrapper'>
							<div id='test' class='bloom-translationGroup bloom-requiresParagraphs'>
								<div class='bloom-editable' lang='en'>
									a<br/>c
								</div>
							</div>
						</div>
					</div>"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            book.BringBookUpToDate(new NullProgress());
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-editable') and @lang='en']/p",
                    2
                );
        }

        [Test]
        public void BookWithUnknownLayout_GetsUpdatedToA5Portrait()
        {
            SetDom(
                @"<div class='bloom-page bloom-frontMatter QX9Landscape'>
						<div id='somewrapper'>
							<div id='test' class='bloom-translationGroup bloom-requiresParagraphs'>
								<div class='bloom-editable' lang='en'>
									a<br/>c
								</div>
							</div>
						</div>
					</div>
					<div class='bloom-page QX9Landscape'>
						<div id='somewrapper'>
							<div id='test' class='bloom-translationGroup bloom-requiresParagraphs'>
								<div class='bloom-editable' lang='en'>
									a<br/>c
								</div>
							</div>
						</div>
					</div>"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            book.BringBookUpToDate(new NullProgress());
            var assertThat = AssertThatXmlIn.Dom(dom);
            // All bloom-page divs should now have class A5Portrait. Can't predict the exact number, it depends exactly
            // what is in the currently inserted Xmatter.
            assertThat.HasSpecifiedNumberOfMatchesForXpath(
                "//div[contains(@class,'A5Portrait') and contains(@class,'bloom-page')]",
                assertThat.CountOfMatchesForXPath("//div[contains(@class,'A5Portrait')]")
            );
            // And there should be none left with the unknown class.
            AssertThatXmlIn.Dom(dom).HasNoMatchForXpath("//div[contains(@class,'QX9Landscape')]");
        }

        //Removing extra lines is of interest in case the user was entering blank lines by hand to separate the paragraphs, which now will
        //be separated by the styling of the new paragraphs
        [Test]
        public void UpdateTextsNewlyChangedToRequiresParagraph_RemovesEmptyLines()
        {
            SetDom(
                @"<div class='bloom-page'>
						<div id='somewrapper'>
							<div id='test' class='bloom-translationGroup bloom-requiresParagraphs'>
								<div class='bloom-editable' lang='en'>
									<br/>a<br/>
								</div>
							</div>
						</div>
					</div>"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            book.BringBookUpToDate(new NullProgress());
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-editable') and @lang='en']/p",
                    1
                );
        }

        // We have currently removed the code that sets nameOfNationalLangauge2 as we can't find
        // anything that uses it. Keeping the test around in case we decide to reinstate.
        //[Test]
        //public void BringBookUpToDate_InsertsRegionalLanguageNameInAsWrittenInNationalLanguage1()
        //{
        //	SetDom(@"<div class='bloom-page'>
        //				 <span data-collection='nameOfNationalLanguage2' lang='en'>{Regional}</span>
        //			</div>
        //	");
        //	var book = CreateBook();
        //	book.SetMultilingualContentLanguages("xyz", "en", "fr");
        //	var dom = book.RawDom;
        //	book.BringBookUpToDate(new NullProgress());
        //	AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//span[text()='French']",1);
        //}


        [Test]
        public void SetMultilingualContentLanguages_UpdatesLanguagesOfBookFieldInDOM()
        {
            SetDom(
                @"<div class='bloom-page'>
						 <div data-derived='languagesOfBook' lang='*'></div>
					</div>
			"
            );

            _collectionSettings = new CollectionSettings(
                new NewCollectionSettings()
                {
                    PathToSettingsFile = CollectionSettings.GetPathForNewSettings(
                        _testFolder.Path,
                        "test"
                    ),
                    Language1Tag = "th",
                    Language2Tag = "fr",
                    Language3Tag = "es"
                }
            );
            var bookData = new BookData(_bookDom, _collectionSettings, null);
            bookData.SetMultilingualContentLanguages("es", "th", "fr");
            bookData.Language2.SetName("ไทย", false);
            var book = new Bloom.Book.Book(
                _metadata,
                _storage.Object,
                _templateFinder.Object,
                _collectionSettings,
                _pageSelection.Object,
                _pageListChangedEvent,
                new BookRefreshEvent()
            );

            book.SetMultilingualContentLanguages("es", "th", "fr");

            //note: our code currently only knows how to display Thai *in Thai*, French *in French*, and Spanish *in Spanish*.
            //It may be better to be writing "Thai" and "Spanish" in French.
            //That's not part of this test, and will have to be changed as we improve that aspect of things.
            // There should be two matches here because it should update both the data-div languagesOfBook element and the page data-derived one.
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[text()='español, ไทย, français']", 2);
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@data-derived='languagesOfBook' and text()='español, ไทย, français']",
                    1
                );

            book.SetMultilingualContentLanguages("th", "fr");
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[text()='ไทย, français']", 2);

            book.SetMultilingualContentLanguages("th");
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@data-derived='languagesOfBook' and text()='ไทย']",
                    1
                );
        }

        [Test]
        public void SavePage_ChangeMade_StorageToldToSave()
        {
            var book = CreateBook();
            var dom = book.GetEditableHtmlDomForPage(book.GetPages().First());
            book.SavePage(dom);
            _storage.Verify(s => s.Save(), Times.AtLeastOnce());
        }

        [Test]
        public void SavePage_ChangeMadeToSrcOfImg_StorageUpdated()
        {
            var book = CreateBook();
            var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[1]);
            var imgInEditingDom =
                dom.SelectSingleNodeHonoringDefaultNS("//img[@id='img1']") as XmlElement;
            imgInEditingDom.SetAttribute("src", "changed.png");

            book.SavePage(dom);
            var imgInStorage =
                _storage.Object.Dom.RawDom.SelectSingleNodeHonoringDefaultNS("//img[@id='img1']")
                as XmlElement;

            Assert.AreEqual("changed.png", imgInStorage.GetAttribute("src"));
        }

        [Test]
        public void SavePage_ChangeMadeToTextAreaOfFirstTwin_StorageUpdated()
        {
            SetDom(
                @"<div class='bloom-page' id='guid2'>
						<p>
							<textarea lang='en' id='1'>english</textarea>
							<textarea lang='xyz' id='2'>originalVernacular</textarea>
						</p>
					</div>
					<div class='bloom-page' id='guid3'>
						<p>
							<textarea  lang='xyz' id='3'>original2</textarea>
						</p>
					</div>
			"
            );
            var book = CreateBook();
            var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[0]);
            var textArea = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='2']");
            Assert.AreEqual(
                "originalVernacular",
                textArea.InnerText,
                "the test conditions aren't correct"
            );
            textArea.InnerText = "changed";
            book.SavePage(dom);
            var vernacularTextNodesInStorage = _storage.Object.Dom.RawDom.SafeSelectNodes(
                "//textarea[@lang='xyz']"
            );

            Assert.AreEqual(
                "changed",
                vernacularTextNodesInStorage.Item(0).InnerText,
                "the value didn't get copied to  the storage dom"
            );
            Assert.AreEqual(
                "original2",
                vernacularTextNodesInStorage.Item(1).InnerText,
                "the second copy of this page should not have been changed"
            );
        }

        [Test]
        public void SavePage_ChangeMadeToTextAreaOfSecondTwin_StorageUpdated()
        {
            SetDom(
                @"<div class='bloom-page' id='guid2'>
						<p>
							<textarea lang='en' id='testText'>english</textarea>
							<textarea lang='xyz' id='testText'>original1</textarea>
						</p>
					</div>
					<div class='bloom-page' id='guid3'>
						<p>
							<textarea  lang='xyz' id='testText'>original2</textarea>
						</p>
					</div>
			"
            );
            var book = CreateBook();
            var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[1]);
            var textArea = dom.SelectSingleNodeHonoringDefaultNS(
                "//textarea[@id='testText' and @lang='xyz']"
            );
            Assert.AreEqual("original2", textArea.InnerText, "the test conditions aren't correct");
            textArea.InnerText = "changed";
            book.SavePage(dom);
            var textNodesInStorage = _storage.Object.Dom.RawDom.SafeSelectNodes(
                "//textarea[@id='testText' and @lang='xyz']"
            );

            Assert.AreEqual(
                "original1",
                textNodesInStorage.Item(0).InnerText,
                "the first copy of this page should not have been changed"
            );
            Assert.AreEqual(
                "changed",
                textNodesInStorage.Item(1).InnerText,
                "the value didn't get copied to  the storage dom"
            );
        }

        [Test]
        public void SavePage_ChangeMadeToTextAreaWithMultipleLanguages_CorrectOneInStorageUpdated()
        {
            SetDom(
                @"<div class='bloom-page' id='guid2'>
						<p>
							<textarea lang='en' id='1'>english</textarea>
							<textarea lang='xyz' id='2'>originalVernacular</textarea>
							<textarea lang='tpi' id='3'>tokpsin</textarea>
						</p>
					</div>
			"
            );
            var book = CreateBook();
            var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[0]);
            var textArea = dom.SelectSingleNodeHonoringDefaultNS("//textarea[ @lang='xyz']");
            Assert.AreEqual(
                "originalVernacular",
                textArea.InnerText,
                "the test conditions aren't correct"
            );
            textArea.InnerText = "changed";
            book.SavePage(dom);
            var vernacularTextNodesInStorage = _storage.Object.Dom.RawDom.SafeSelectNodes(
                "//textarea[@id='2' and @lang='xyz']"
            );

            Assert.AreEqual(
                "changed",
                vernacularTextNodesInStorage.Item(0).InnerText,
                "the value didn't get copied to  the storage dom"
            );
        }

        [Test]
        public void GetEditableHtmlDomForPage_BasicBook_HasA5PortraitClass()
        {
            var book = CreateBook();
            book.SetLayout(
                new Layout() { SizeAndOrientation = SizeAndOrientation.FromString("A5Portrait") }
            );
            var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[2]);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'A5Portrait') and contains(@class,'bloom-page')]",
                    1
                );
        }

        [Test]
        public void GetEditableHtmlDomForPage_TemplateBook_NonXMatterLabelMadeEditable()
        {
            SetDom(
                @"<div class='bloom-page bloom-frontMatter' id='guid1'>
						<div class='pageLabel'></div>
						<p>
						</p>
					</div>
					<div class='bloom-page' id='guid2'>
						<div class='pageLabel'></div>
						<p>
						</p>
					</div>
					<div class='bloom-page bloom-backMatter' id='guid3'>
						<div class='pageLabel'></div>
						<p>
						</p>
					</div>
			"
            );
            var book = CreateBook();
            // Even a content page doesn't get this unless it's a template book
            var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[1]);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath("//div[@class='pageLabel' and @contenteditable='true']");
            book.IsSuitableForMakingShells = true;
            // content page in template should get editable label
            dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[1]);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='pageLabel' and @contenteditable='true']",
                    1
                );
            // but not in front or back matter.
            dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[0]);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath("//div[@class='pageLabel' and @contenteditable='true']");
            dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[2]);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath("//div[@class='pageLabel' and @contenteditable='true']");
        }

        [Test]
        public void InsertPageAfter_OnFirstPage_NewPageInsertedAsSecond()
        {
            var book = CreateBook();
            var existingPage = book.GetPages().First();
            TestTemplateInsertion(
                book,
                existingPage,
                "<div class='bloom-page somekind'>hello</div>"
            );
        }

        [Test]
        public void InsertPageAfter_FirstPage_DuplicatedTwice()
        {
            var book = CreateBook();
            var expectedPageCount = 3;
            AssertPageCount(book, expectedPageCount);
            var existingPage = book.GetPages().First();
            var id = existingPage.Id;

            // SUT
            book.InsertPageAfter(existingPage, existingPage, 2);
            expectedPageCount += 2;

            AssertPageCount(book, expectedPageCount);
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@id='" + id + "']", 1);
            var id1 = book.GetPageByIndex(2).Id; // first new page
            var id2 = book.GetPageByIndex(3).Id; // second new page
            Assert.AreNotEqual(id1, id2, "IDs for different pages should not be the same");
            Assert.AreNotEqual(
                id,
                id1,
                "IDs of duplicated pages should not be the same as the source page"
            );
        }

        [Test]
        public void InsertPageAfter_OnLastPage_NewPageInsertedAtEnd()
        {
            var book = CreateBook();
            var existingPage = book.GetPages().First();
            TestTemplateInsertion(
                book,
                existingPage,
                "<div class='bloom-page somekind'>hello</div>"
            );
        }

        /// <summary>
        /// a page might be "extra" as far as the template is concerned, but
        /// once a page is inserted into book (which may become a shell), it's
        /// just a normal page
        /// </summary>
        [Test]
        public void InsertPageAfter_PageWasMarkedExtra_NewPageIsNotMarkedExtra()
        {
            //enhance: move to book starter tests, since that's what implements the actual behavior
            var book = CreateBook();
            var existingPage = book.GetPages().First();
            Mock<IPage> templatePage = CreateTemplatePage(
                "<div class='bloom-page'  data-page='extra' >hello</div>"
            );
            book.InsertPageAfter(existingPage, templatePage.Object);
            Assert.IsTrue(
                GetPageFromBookDom(book, 1)
                    .GetStringAttribute("class")
                    .Contains("bloom-page bloom-monolingual A5Portrait")
            );
        }

        [Test]
        public void InsertPageAfter_InTemplateBook_NewPageIsMarkedExtra()
        {
            var book = CreateBook();
            var existingPage = book.GetPages().First();
            Mock<IPage> templatePage = CreateTemplatePage("<div class='bloom-page'>hello</div>");
            book.IsSuitableForMakingShells = true;
            book.InsertPageAfter(existingPage, templatePage.Object);
            Assert.That(
                GetPageFromBookDom(book, 1).GetStringAttribute("data-page"),
                Is.EqualTo("extra")
            );
        }

        [Test]
        public void InsertPageAfter_FromDifferentBook_MergesStyles()
        {
            using (
                var destBookWrapper = new TestBook(
                    "current book",
                    @"<!DOCTYPE html>
<html>
<head>
	<style type='text/css' title='userModifiedStyles'>
	/*<![CDATA[*/
	.BigWords-style { font-size: 45pt ! important; text-align: center ! important; }
	/*]]>*/
	</style>
</head>

<body>
	<div class='bloom-page'><div class='bloom-translationGroup BigWords-style'></div></div>
</body>
</html>"
                )
            )
            {
                var destBook = destBookWrapper.Book;
                using (
                    var sourceBookWrapper = new TestBook(
                        "source book",
                        @"<!DOCTYPE html>
<html>
<head>
	<style type='text/css' title='userModifiedStyles'>
	/*<![CDATA[*/
	.FancyText-style { font-size: 45pt ! important; text-align: center ! important; }
	.FancyText-style > p {margin-left: 20px !important}
	.FancyText-style[lang='en'] {font-size: 42pt; }
	.FancyText-style[lang='he'] {font-size: 50pt; }
	.BigWords-style {font-size:70pt !important; }
	.BigWords-style[lang='en'] {font-size:65pt !important; }
	.BigWords-style > p {margin-left: 20px !important}
	/*]]>*/
	</style>
</head>

<body>
	<div class='bloom-page'><div class='bloom-translationGroup FancyText-style'></div></div>
</body>
</html>"
                    )
                )
                {
                    var sourceBook = sourceBookWrapper.Book;
                    var existingPage = destBook.GetPages().First();
                    var templatePage = sourceBook.GetPages().First();
                    destBook.InsertPageAfter(existingPage, templatePage);
                    var dom = destBook.RawDom.StripXHtmlNameSpace();
                    var style = dom.SafeSelectNodes("//style")[0];
                    Assert.That(
                        style.InnerText,
                        Does.Contain(
                            ".FancyText-style { font-size: 45pt ! important; text-align: center ! important; }"
                        )
                    );
                    Assert.That(
                        style.InnerText,
                        Does.Contain(".FancyText-style > p {margin-left: 20px !important}")
                    );
                    Assert.That(
                        style.InnerText,
                        Does.Contain(".FancyText-style[lang='en'] {font-size: 42pt; }")
                    );
                    Assert.That(
                        style.InnerText,
                        Does.Contain(".FancyText-style[lang='he'] {font-size: 50pt; }")
                    );
                    // Original BigWords style should survive unchanged.
                    Assert.That(
                        style.InnerText,
                        Does.Contain(
                            ".BigWords-style { font-size: 45pt ! important; text-align: center ! important; }"
                        )
                    );
                    Assert.That(
                        style.InnerText,
                        Does.Not.Contain(".BigWords-style {font-size:70pt !important; }")
                    );
                    Assert.That(style.InnerText, Does.Not.Contain(".BigWords-style[lang='en']"));
                    Assert.That(style.InnerText, Does.Not.Contain(".BigWords-style > p"));
                }
            }
        }

        [Test]
        public void InsertPageAfter_TemplateRefsPicture_PictureCopied()
        {
            var book = CreateBook();
            var existingPage = book.GetPages().First();
            Mock<IPage> templatePage = CreateTemplatePage(
                "<div class='bloom-page'  data-page='extra' >hello<img src='read.png'/></div>"
            );
            using (
                var tempFolder = new TemporaryFolder(
                    "InsertPageAfter_TemplateRefsPicture_PictureCopied"
                )
            )
            {
                File.WriteAllText(
                    Path.Combine(tempFolder.FolderPath, "read.png"),
                    "This is a test"
                );
                var mockTemplateBook = new Moq.Mock<Bloom.Book.Book>();
                mockTemplateBook.Setup(x => x.FolderPath).Returns(tempFolder.FolderPath);
                mockTemplateBook
                    .Setup(x => x.OurHtmlDom.GetTemplateStyleSheets())
                    .Returns(new string[] { });
                templatePage.Setup(x => x.Book).Returns(mockTemplateBook.Object);
                book.InsertPageAfter(existingPage, templatePage.Object);
            }
            Assert.That(File.Exists(Path.Combine(book.FolderPath, "read.png")));
        }

        [Test]
        public void InsertPageAfter_TemplateRefsScript_ScriptCopied()
        {
            var book = CreateBook();
            var existingPage = book.GetPages().First();
            Mock<IPage> templatePage = CreateTemplatePage(
                "<div class='bloom-page'  data-page='extra' >hello<script src='something.js'/></div>"
            );
            using (
                var tempFolder = new TemporaryFolder(
                    "InsertPageAfter_TemplateRefsScript_ScriptCopied"
                )
            )
            {
                File.WriteAllText(
                    Path.Combine(tempFolder.FolderPath, "something.js"),
                    @"Console.log('here we go')"
                );
                var mockTemplateBook = new Moq.Mock<Bloom.Book.Book>();
                mockTemplateBook.Setup(x => x.FolderPath).Returns(tempFolder.FolderPath);
                mockTemplateBook
                    .Setup(x => x.OurHtmlDom.GetTemplateStyleSheets())
                    .Returns(new string[] { });
                templatePage.Setup(x => x.Book).Returns(mockTemplateBook.Object);
                book.InsertPageAfter(existingPage, templatePage.Object);
            }
            Assert.That(File.Exists(Path.Combine(book.FolderPath, "something.js")));
        }

        [Test]
        public void InsertPageAfter_SourcePageHasLineage_GetsLineageOfSourcePlusItsAncestor()
        {
            //enhance: move to book starter tests, since that's what implements the actual behavior
            var book = CreateBook();
            var existingPage = book.GetPages().First();
            Mock<IPage> templatePage = CreateTemplatePage(
                "<div class='bloom-page'  data-page='extra'  data-pagelineage='grandma' id='ma'>hello</div>"
            );
            book.InsertPageAfter(existingPage, templatePage.Object);
            XmlElement page = (XmlElement)GetPageFromBookDom(book, 1);
            AssertThatXmlIn
                .String(page.OuterXml)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@data-pagelineage]", 1);
            string[] guids = GetLineageGuids(page);
            Assert.AreEqual("grandma", guids[0]);
            Assert.AreEqual("ma", guids[1]);
            Assert.AreEqual(2, guids.Length);
        }

        private string[] GetLineageGuids(XmlElement page)
        {
            XmlAttribute node = (XmlAttribute)
                page.SelectSingleNodeHonoringDefaultNS("//div/@data-pagelineage");
            return node.Value.Split(new char[] { ';' });
        }

        [Test]
        public void InsertPageAfter_SourcePageHasNoLineage_IdOfSourceBecomesLineageOfNewPage()
        {
            //enhance: move to book starter tests, since that's what implements the actual behavior
            var book = CreateBook();
            var existingPage = book.GetPages().First();
            Mock<IPage> templatePage = CreateTemplatePage(
                "<div class='bloom-page' data-page='extra' id='ma'>hello</div>"
            );
            book.InsertPageAfter(existingPage, templatePage.Object);
            XmlElement page = (XmlElement)GetPageFromBookDom(book, 1);
            AssertThatXmlIn
                .String(page.OuterXml)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@data-pagelineage='ma']", 1);
            string[] guids = GetLineageGuids(page);
            Assert.AreEqual("ma", guids[0]);
            Assert.AreEqual(1, guids.Length);
        }

        [Test]
        public void InsertPageAfter_PageRequiresStylesheetWeDontHave_StylesheetLinkAdded()
        {
            using (
                var bookFolder = new TemporaryFolder(
                    "InsertPageAfter_PageRequiresStylesheetWeDontHave_StylesheetLinkAdded"
                )
            )
            {
                var templatePage = MakeTemplatePageThatHasABookWithStylesheets(
                    bookFolder,
                    new[] { "foo.css" }
                );
                SetDom("<div class='bloom-page' id='1'></div>", ""); //but no special stylesheets in the target book
                var targetBook = CreateBook();
                targetBook.InsertPageAfter(targetBook.GetPages().First(), templatePage);

                Assert.NotNull(
                    targetBook.OurHtmlDom.GetTemplateStyleSheets().First(name => name == "foo.css")
                );
            }
        }

        [Test]
        public void InsertPageAfter_PageRequiresStylesheetWeDontHave_StylesheetFileCopied()
        {
            //we need an actual templateBookFolder to contain the stylesheet we need to see copied into the target book
            using (
                var templateBookFolder = new TemporaryFolder(
                    "InsertPageAfter_PageRequiresStylesheetWeDontHave_StylesheetFileCopied"
                )
            )
            {
                //just a boring simple target book
                SetDom("<div class='bloom-page' id='1'></div>", "");
                var targetBook = CreateBook();

                //our template folder will have this stylesheet file
                File.WriteAllText(templateBookFolder.Combine("foo.css"), ".dummy{width:100px}");

                //we're going to reference one stylesheet that is actually available in the template folder, and one that isn't

                var templatePage = MakeTemplatePageThatHasABookWithStylesheets(
                    templateBookFolder,
                    new[] { "foo.css", "notthere.css" }
                );

                targetBook.InsertPageAfter(targetBook.GetPages().First(), templatePage);

                Assert.True(File.Exists(targetBook.FolderPath.CombineForPath("foo.css")));

                //Now add it again, to see if that causes problems
                targetBook.InsertPageAfter(targetBook.GetPages().First(), templatePage);

                //Have the template list a file it doesn't actually have
                var templatePage2 = MakeTemplatePageThatHasABookWithStylesheets(
                    templateBookFolder,
                    new[] { "notthere.css" }
                );

                //for now, we just want it to not crash
                targetBook.InsertPageAfter(targetBook.GetPages().First(), templatePage2);
            }
        }

        [Test]
        public void InsertPageAfter_PageRequiresStylesheetWeAlreadyHave_StylesheetNotAdded()
        {
            using (
                var templateBookFolder = new TemporaryFolder(
                    "InsertPageAfter_PageRequiresStylesheetWeAlreadyHave_StylesheetNotAdded"
                )
            )
            {
                var templatePage = MakeTemplatePageThatHasABookWithStylesheets(
                    templateBookFolder,
                    new string[] { "foo.css" }
                );
                //it's in the template
                var link = "<link rel='stylesheet' href='foo.css' type='text/css'></link>";
                SetDom("<div class='bloom-page' id='1'></div>", link); //and we already have it in the target book
                var targetBook = CreateBook();
                targetBook.InsertPageAfter(targetBook.GetPages().First(), templatePage);

                Assert.AreEqual(
                    1,
                    targetBook.OurHtmlDom.GetTemplateStyleSheets().Count(name => name == "foo.css")
                );
            }
        }

        private IPage MakeTemplatePageThatHasABookWithStylesheets(
            TemporaryFolder bookFolder,
            IEnumerable<string> stylesheetNames
        )
        {
            var headContents = "";
            foreach (var stylesheetName in stylesheetNames)
            {
                headContents +=
                    "<link rel='stylesheet' href='" + stylesheetName + "' type='text/css'></link>";
            }

            var templateDom = new HtmlDom(
                "<html><head>"
                    + headContents
                    + "</head><body><div class='bloom-page' id='1'></div></body></html>"
            );
            var templateBook = new Moq.Mock<Bloom.Book.Book>();
            templateBook.Setup(x => x.FolderPath).Returns(bookFolder.FolderPath);
            templateBook.Setup(x => x.OurHtmlDom).Returns(templateDom);
            Mock<IPage> templatePage = CreateTemplatePage("<div class='bloom-page' id='1'></div>");
            templatePage.Setup(x => x.Book).Returns(templateBook.Object);
            return templatePage.Object;
        }

        private void TestTemplateInsertion(
            Bloom.Book.Book book,
            IPage existingPage,
            string divContent
        )
        {
            Mock<IPage> templatePage = CreateTemplatePage(divContent);

            book.InsertPageAfter(existingPage, templatePage.Object);
            AssertPageCount(book, 4);
            Assert.IsTrue(
                GetPageFromBookDom(book, 1)
                    .GetStringAttribute("class")
                    .Contains("bloom-page somekind bloom-monolingual A5Portrait")
            );
        }

        private XmlNode GetPageFromBookDom(Bloom.Book.Book book, int pageNumber0Based)
        {
            var result = book.RawDom.StripXHtmlNameSpace();
            return result.SafeSelectNodes("//div[contains(@class, 'bloom-page')]", null)[
                pageNumber0Based
            ];
        }

        private void AssertPageCount(Bloom.Book.Book book, int expectedCount)
        {
            var result = book.RawDom.StripXHtmlNameSpace();
            AssertThatXmlIn
                .Dom(result)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'bloom-page')]",
                    expectedCount
                );
        }

        [Test]
        public void CreateBook_AlreadyHasCoverColor_GetsEmptyUserStyles()
        {
            var coverStyle =
                @"<style type='text/css'>
	DIV.coverColor  TEXTAREA  { background-color: #98D0B9 !important; }
	DIV.bloom-page.coverColor { background-color: #98D0B9 !important; }
			</style>";
            SetDom("<div class='bloom-page' id='1'></div>", coverStyle);

            // SUT
            var book = CreateBook();

            var styleNodes = book.OurHtmlDom.Head.SafeSelectNodes("./style");
            Assert.AreEqual(2, styleNodes.Count);
            Assert.AreEqual("userModifiedStyles", styleNodes[0].Attributes["title"].Value);
            Assert.AreEqual(string.Empty, styleNodes[0].InnerText);
            // verify that the 'coverColor' rules are still there
            Assert.IsTrue(styleNodes[1].InnerText.Contains("coverColor"));
        }

        [Test]
        public void CreateBook_AlreadyHasCoverColorAndUserStyles_KeepsExistingStyles()
        {
            var userStyle =
                @"<style type='text/css' title='userModifiedStyles'>
	.normal-style[lang='fr'] { font-size: 9pt ! important; }
	.normal-style { font-size: 9pt !important; }
			</style>";
            var coverStyle =
                @"<style type='text/css'>
	DIV.coverColor  TEXTAREA  { background-color: #98D0B9 !important; }
	DIV.bloom-page.coverColor { background-color: #98D0B9 !important; }
			</style>";
            SetDom("<div class='bloom-page' id='1'></div>", userStyle + coverStyle);

            // SUT
            var book = CreateBook();

            var styleNodes = book.OurHtmlDom.Head.SafeSelectNodes("./style");
            Assert.AreEqual(2, styleNodes.Count);
            Assert.AreEqual("userModifiedStyles", styleNodes[0].Attributes["title"].Value);
            Assert.IsTrue(
                styleNodes[0].InnerText.Contains(
                    ".normal-style[lang='fr'] { font-size: 9pt ! important; }"
                )
            );
            Assert.IsTrue(styleNodes[1].InnerText.Contains("coverColor"));
        }

        [Test]
        public void CreateBook_HasNeitherStyle_GetsEmptyUserStyles()
        {
            SetDom("<div class='bloom-page' id='1'></div>");

            // SUT
            var book = CreateBook();

            var styleNodes = book.OurHtmlDom.Head.SafeSelectNodes("./style");
            Assert.AreEqual(2, styleNodes.Count); // also gets a new 'coverColor' style element
            Assert.AreEqual("userModifiedStyles", styleNodes[0].Attributes["title"].Value);
            Assert.AreEqual(string.Empty, styleNodes[0].InnerText);
            Assert.IsTrue(styleNodes[1].InnerText.Contains("coverColor"));
        }

        [Test]
        public void CreateBook_AlreadyHasCoverColorAndUserStyles_InWrongOrder_GetsStyleElementsReversed()
        {
            var coverStyle =
                @"<style type='text/css'>
	DIV.coverColor  TEXTAREA  { background-color: #98D0B9 !important; }
	DIV.bloom-page.coverColor { background-color: #98D0B9 !important; }
			</style>";
            var userStyle =
                @"<style type='text/css' title='userModifiedStyles'>
	.normal-style[lang='fr'] { font-size: 9pt ! important; }
	.normal-style { font-size: 9pt !important; }
			</style>";
            SetDom("<div class='bloom-page' id='1'></div>", coverStyle + userStyle);

            // SUT
            var book = CreateBook();

            var styleNodes = book.OurHtmlDom.Head.SafeSelectNodes("./style");
            Assert.AreEqual(2, styleNodes.Count);
            Assert.AreEqual("userModifiedStyles", styleNodes[0].Attributes["title"].Value);
            Assert.IsTrue(
                styleNodes[0].InnerText.Contains(
                    ".normal-style[lang='fr'] { font-size: 9pt ! important; }"
                )
            );
            Assert.IsTrue(styleNodes[1].InnerText.Contains("coverColor"));
        }

        [Test]
        public void DuplicatePage()
        {
            var book = CreateBook();
            var original = book.GetPages().Count();
            var existingPage = book.GetPages().Last();
            book.DuplicatePage(existingPage);
            AssertPageCount(book, original + 1);

            var newPage = book.GetPages().Last();
            Assert.AreNotEqual(existingPage, newPage);
            Assert.AreNotEqual(existingPage.Id, newPage.Id);

            var existingDivNode = existingPage.GetDivNodeForThisPage();
            var newDivNode = newPage.GetDivNodeForThisPage();

            Assert.AreEqual(existingPage.Id, newDivNode.Attributes["data-pagelineage"].Value);
            Assert.AreEqual(existingDivNode.InnerXml, newDivNode.InnerXml);

            Assert.AreEqual(
                original.ToString(),
                existingDivNode.Attributes["data-page-number"].Value
            );
            Assert.AreEqual(
                (original + 1).ToString(),
                newDivNode.Attributes["data-page-number"].Value
            );
        }

        [Test]
        public void DuplicatePage_WithAudio_CopiesAudioAndAssignsNewId()
        {
            var book = CreateBook(); // has pages from  BookTestsBase.GetThreePageDom()
            var original = book.GetPages().Count();
            var existingPage = book.GetPages().Last();
            var pageDiv = book.GetPageElements().Cast<XmlElement>().Last();
            var extraPara = pageDiv.OwnerDocument.CreateElement("p");
            pageDiv.AppendChild(extraPara);
            var sentenceSpan = pageDiv.OwnerDocument.CreateElement("span");
            extraPara.AppendChild(sentenceSpan);
            sentenceSpan.SetAttribute("class", "audio-sentence");
            var audioId = Guid.NewGuid().ToString();
            sentenceSpan.SetAttribute("id", audioId);
            sentenceSpan.InnerText = "This was a sentence span";

            var videoDiv = pageDiv.OwnerDocument.CreateElement("div");
            videoDiv.SetAttribute("class", "bloom-videoContainer");
            pageDiv.AppendChild(videoDiv);
            var videoElt = pageDiv.OwnerDocument.CreateElement("video");
            videoDiv.AppendChild(videoElt);
            var sourceElt = pageDiv.OwnerDocument.CreateElement("source");
            videoElt.AppendChild(sourceElt);
            sourceElt.SetAttribute("src", "video/Crow.mp4"); // no timings path tested here!

            var audioFolder = Path.Combine(book.FolderPath, "audio");
            Directory.CreateDirectory(audioFolder);
            var audioFilePath = Path.Combine(audioFolder, audioId + ".wav");
            File.WriteAllText(audioFilePath, "This is a complete fake");
            var mp3FilePath = Path.ChangeExtension(audioFilePath, "mp3");
            File.WriteAllText(mp3FilePath, "This is a fake mp3");

            var videoFolder = Path.Combine(book.FolderPath, "video");
            Directory.CreateDirectory(videoFolder);
            var videoFilePath = Path.Combine(videoFolder, "Crow.mp4");
            File.WriteAllText(videoFilePath, "This is a fake video");

            book.DuplicatePage(existingPage);

            AssertPageCount(book, original + 1);

            var newPage = book.GetPages().Last();
            Assert.AreNotEqual(existingPage, newPage);
            Assert.AreNotEqual(existingPage.Id, newPage.Id);

            var newDivNode = newPage.GetDivNodeForThisPage();

            var newFirstPara = newDivNode.GetElementsByTagName("p").Cast<XmlElement>().Last();
            Assert.That(newFirstPara.InnerText, Is.EqualTo("This was a sentence span")); // kept the text
            var newSpan = newFirstPara
                .GetElementsByTagName("span")
                .Cast<XmlElement>()
                .FirstOrDefault();
            Assert.That(newSpan, Is.Not.Null);
            var id = newSpan.Attributes["id"]?.Value;
            Assert.That(id, Is.Not.Null.And.Not.Empty);
            Assert.That(id, Is.Not.EqualTo(audioId));

            var newAudioPath = Path.Combine(audioFolder, id + ".wav");
            Assert.That(File.Exists(newAudioPath));
            Assert.That(File.ReadAllText(newAudioPath), Is.EqualTo("This is a complete fake"));
            var newMp3Path = Path.ChangeExtension(newAudioPath, "mp3");
            Assert.That(File.Exists(newMp3Path));
            Assert.That(File.ReadAllText(newMp3Path), Is.EqualTo("This is a fake mp3"));

            var newVideoSrc = newDivNode.SelectSingleNode(".//source") as XmlElement;
            var srcAttrVal = newVideoSrc?.GetAttribute("src");
            Assert.That(srcAttrVal, Does.StartWith("video/"));
            Assert.That(srcAttrVal, Does.Not.Contain("#").And.Not.Contain("t="));
            Assert.That(srcAttrVal, Does.EndWith(".mp4"));
            var fileName = srcAttrVal.Substring("video/".Length);
            Assert.That(fileName, Is.Not.EqualTo("Crow.mp4"));

            var newVideoFolder = Path.Combine(book.FolderPath, "video");
            var newVideoPath = Path.Combine(newVideoFolder, fileName);
            Assert.That(RobustFile.Exists(newVideoPath));
            Assert.That(File.ReadAllText(newVideoPath), Is.EqualTo("This is a fake video"));
        }

        [TestCase(
            @"<div id='id-div1' class='bloom-editable' data-audiorecordingmode='Sentence' >
						<p>
							<span id='id-span1' class='audio-sentence'>Sentence 1.</span>
							<span id='id-span2' class='audio-sentence'>Sentence 2.</span>
						</p>
					</div>",
            TestName = "DuplicatePage_WithAudioWhichWasTextBoxModeButNowSentenceMode_CopiesAudioAndAssignsNewId"
        )]
        [TestCase(
            @"<div id='id-div1' class='bloom-editable' data-audiorecordingmode='Sentence' >
						<p>
							<span id='id-span1' class='bloom-highlightSegment'>Sentence 1.</span>
							<span id='id-span2' class='bloom-highlightSegment'>Sentence 2.</span>
						</p>
					</div>",
            TestName = "DuplicatePage_WithAudioWhichWasSoftSplit_CopiesAudioAndAssignsNewId"
        )]
        public void DuplicatePage_TalkingBookTests(string pageInnerHtml)
        {
            var divId = "id-div1";
            var span1Id = "id-span1";
            var span2Id = "id-span2";

            var htmlSourceBook =
                $@"<html><head></head><body>
					<div class='bloom-page numberedPage' id='page1' data-page-number='1'>
						{pageInnerHtml}
					</div>
				</body></html>";

            using (
                var tempFolder = new SIL.TestUtilities.TemporaryFolder(
                    _testFolder,
                    "DuplicatePage_WithAudioWhichWasTextBoxModeButNowSentenceMode_CopiesAudioAndAssignsNewId"
                )
            )
            {
                var doc = new XmlDocument();
                doc.LoadXml(htmlSourceBook);
                var dom = new HtmlDom(doc);
                var storage = MakeMockStorage(tempFolder.Path, () => dom);
                var book = new Bloom.Book.Book(
                    storage.Object.BookInfo,
                    storage.Object,
                    _templateFinder.Object,
                    CreateDefaultCollectionsSettings(),
                    _pageSelection.Object,
                    _pageListChangedEvent,
                    new BookRefreshEvent()
                );

                //var book = CreateBook(); // has pages from  BookTestsBase.GetThreePageDom()
                var originalPageCount = book.GetPages().Count();
                var existingPage = book.GetPages().Last();

                var existingDivNode = existingPage.GetDivNodeForThisPage();
                var existingTextDiv = existingDivNode.ChildNodes.Cast<XmlElement>().First();
                Assert.That(existingTextDiv.Attributes["id"]?.Value, Is.EqualTo(divId));
                var existingP = existingTextDiv.ChildNodes[0];
                var existingSpan1 = existingP.ChildNodes[0];
                Assert.That(existingSpan1.Attributes?["id"]?.Value, Is.EqualTo(span1Id));
                var existingSpan2 = existingP.ChildNodes[1];
                Assert.That(existingSpan2.Attributes?["id"]?.Value, Is.EqualTo(span2Id));

                var audioFolder = Path.Combine(book.FolderPath, "audio");
                Directory.CreateDirectory(audioFolder);
                var audioFilePath = Path.Combine(audioFolder, "id-span1.mp3");
                File.WriteAllText(audioFilePath, @"This is a fake mp3 for span 1");
                var audioFilePath2 = Path.Combine(audioFolder, "id-span2.mp3");
                File.WriteAllText(audioFilePath2, @"This is a fake mp3 for span 2");

                book.DuplicatePage(existingPage);

                AssertPageCount(book, originalPageCount + 1);

                var newPage = book.GetPages().Last();
                Assert.AreNotEqual(existingPage, newPage);
                Assert.AreNotEqual(existingPage.Id, newPage.Id);

                var newDivNode = newPage.GetDivNodeForThisPage();

                var newTextDiv = newDivNode.ChildNodes.Cast<XmlElement>().First();
                var id = newTextDiv.Attributes["id"]?.Value;
                Assert.That(id, Is.Not.Null.And.Not.Empty);
                Assert.That(id, Is.Not.EqualTo(divId));

                var newP = newTextDiv.ChildNodes[0];

                var newSpan1 = newP.ChildNodes[0];
                Assert.That(newSpan1.InnerText, Is.EqualTo("Sentence 1.")); // kept the text
                var newSpan1Id = newSpan1.Attributes?["id"]?.Value;
                Assert.That(newSpan1Id, Is.Not.Null.And.Not.Empty);
                Assert.That(newSpan1Id, Is.Not.EqualTo(span1Id));
                var newAudioFolder = Path.Combine(book.FolderPath, "audio");
                var newMp3Path = Path.Combine(newAudioFolder, newSpan1Id + ".mp3");
                Assert.That(File.Exists(newMp3Path));
                Assert.That(
                    File.ReadAllText(newMp3Path),
                    Is.EqualTo("This is a fake mp3 for span 1")
                );

                var newSpan2 = newP.ChildNodes[1];
                Assert.That(newSpan2.InnerText, Is.EqualTo("Sentence 2.")); // kept the text
                var newSpan2Id = newSpan2.Attributes?["id"]?.Value;
                Assert.That(newSpan2Id, Is.Not.Null.And.Not.Empty);
                Assert.That(newSpan2Id, Is.Not.EqualTo(span2Id));
                var newMp3Path2 = Path.Combine(newAudioFolder, newSpan2Id + ".mp3");
                Assert.That(File.Exists(newMp3Path2));
                Assert.That(
                    File.ReadAllText(newMp3Path2),
                    Is.EqualTo("This is a fake mp3 for span 2")
                );
            }
        }

        // If we're actually copying from another book, renaming the files is not important.
        // But we need that when copying from the SAME book, so we always do it, and this
        // test can cover that case also.
        [Test]
        public void InsertPageAfter_FromAnotherBook_WholeTextMode_CopiesAndRenamesAudioAndVideo()
        {
            var htmlSourceBook =
                @"<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='page1' data-page-number='1'>
						<div id='id1' class='bloom-editable audio-sentence'>
							<p>Page 1 Paragraph 1 Sentence 1</p>
							<p>Page 1 Paragraph 2 Sentence 1</p>
						</div>
						<div class='bloom-videoContainer'>
							<video>
								<source src='video/Crow.mp4#t=1.2,5.7'></source>
							</video>
						</div>
					</div>
				</body></html>";

            using (
                var tempFolder = new SIL.TestUtilities.TemporaryFolder(_testFolder, "sourceBook")
            )
            {
                var doc = new XmlDocument();
                doc.LoadXml(htmlSourceBook);
                var dom = new HtmlDom(doc);
                var storage = MakeMockStorage(tempFolder.Path, () => dom);
                var sourceBook = new Bloom.Book.Book(
                    storage.Object.BookInfo,
                    storage.Object,
                    _templateFinder.Object,
                    CreateDefaultCollectionsSettings(),
                    _pageSelection.Object,
                    _pageListChangedEvent,
                    new BookRefreshEvent()
                );
                var sourcePage = sourceBook.GetPages().Last();

                var book = CreateBook(); // has pages from  BookTestsBase.GetThreePageDom()
                var original = book.GetPages().Count();
                var existingPage = book.GetPages().Last();

                var audioFolder = Path.Combine(sourceBook.FolderPath, "audio");
                Directory.CreateDirectory(audioFolder);
                var audioFilePath = Path.Combine(audioFolder, "id1.wav");
                File.WriteAllText(audioFilePath, "This is a complete fake");
                var mp3FilePath = Path.ChangeExtension(audioFilePath, "mp3");
                File.WriteAllText(mp3FilePath, "This is a fake mp3");

                var videoFolder = Path.Combine(sourceBook.FolderPath, "video");
                Directory.CreateDirectory(videoFolder);
                var videoFilePath = Path.Combine(videoFolder, "Crow.mp4");
                File.WriteAllText(videoFilePath, "This is a fake video");

                book.InsertPageAfter(existingPage, sourcePage);

                AssertPageCount(book, original + 1);

                var newPage = book.GetPages().Last();
                Assert.AreNotEqual(existingPage, newPage);
                Assert.AreNotEqual(existingPage.Id, newPage.Id);

                var newDivNode = newPage.GetDivNodeForThisPage();

                var newTextDiv = newDivNode.ChildNodes.Cast<XmlElement>().First();
                Assert.That(
                    newTextDiv.InnerText,
                    Is.EqualTo("Page 1 Paragraph 1 Sentence 1Page 1 Paragraph 2 Sentence 1")
                ); // kept the text
                var id = newTextDiv.Attributes["id"]?.Value;
                Assert.That(id, Is.Not.Null.And.Not.Empty);
                Assert.That(id, Is.Not.EqualTo("id1"));

                var newAudioFolder = Path.Combine(book.FolderPath, "audio");
                var newAudioPath = Path.Combine(newAudioFolder, id + ".wav");
                Assert.That(File.Exists(newAudioPath));
                Assert.That(File.ReadAllText(newAudioPath), Is.EqualTo("This is a complete fake"));
                var newMp3Path = Path.ChangeExtension(newAudioPath, "mp3");
                Assert.That(File.Exists(newMp3Path));
                Assert.That(File.ReadAllText(newMp3Path), Is.EqualTo("This is a fake mp3"));

                var newVideoContainer = newDivNode.GetElementsByTagName("div")[1] as XmlElement;
                var newVideoDiv = newVideoContainer.GetElementsByTagName("video")[0] as XmlElement;
                var newVideoSource = newVideoDiv.GetElementsByTagName("source")[0] as XmlElement;
                var source = newVideoSource.Attributes["src"]?.Value;
                Assert.That(source, Does.StartWith("video/"));
                Assert.That(source, Does.EndWith(".mp4#t=1.2,5.7"));
                var fileName = source.Substring(
                    "video/".Length,
                    source.Length - "video/.mp4#t=1.2,5.7".Length
                );
                Assert.That(fileName, Is.Not.EqualTo("Crow").And.Not.Null.And.Not.Empty);

                var newVideoFolder = Path.Combine(book.FolderPath, "video");
                var newVideoPath = Path.Combine(newVideoFolder, fileName + ".mp4");
                Assert.That(RobustFile.Exists(newVideoPath));
                Assert.That(File.ReadAllText(newVideoPath), Is.EqualTo("This is a fake video"));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void InsertPageAfter_FromAnotherBook_CopiesWidget(
            bool simulateWidgetWithSameNameExists
        )
        {
            var htmlSourceBook =
                @"<html><head></head><body>
					<div class='bloom-page custom-widget-page customPage bloom-interactive-page enterprise-only no-margin-page numberedPage A5Portrait side-right bloom-monolingual' id='page1' data-page='' data-analyticscategories='widget' help-link='https://docs.bloomlibrary.org/widgets' data-pagelineage='3a705ac1-c1f2-45cd-8a7d-011c009cf406' data-page-number='1' lang='' data-activity='iframe'>					
						<div class='bloom-widgetContainer bloom-leadingElement'>
							<iframe src='activities/my%20Wid%25et/index.htm' />
						</div>
					</div>
				</body></html>";

            using (
                var tempFolder = new SIL.TestUtilities.TemporaryFolder(_testFolder, "sourceBook")
            )
            {
                var doc = new XmlDocument();
                doc.LoadXml(htmlSourceBook);
                var dom = new HtmlDom(doc);
                var storage = MakeMockStorage(tempFolder.Path, () => dom);
                var sourceBook = new Bloom.Book.Book(
                    storage.Object.BookInfo,
                    storage.Object,
                    _templateFinder.Object,
                    CreateDefaultCollectionsSettings(),
                    _pageSelection.Object,
                    _pageListChangedEvent,
                    new BookRefreshEvent()
                );
                var sourcePage = sourceBook.GetPages().Last();

                var book = CreateBook(); // has pages from  BookTestsBase.GetThreePageDom()
                var original = book.GetPages().Count();
                var existingPage = book.GetPages().Last();

                var activitiesFolderPath = Path.Combine(sourceBook.FolderPath, "activities");
                Directory.CreateDirectory(activitiesFolderPath);
                var widgetFolderPath = Path.Combine(activitiesFolderPath, "my Wid%et");
                Directory.CreateDirectory(widgetFolderPath);
                var indexFilePath = Path.Combine(widgetFolderPath, "index.htm");
                File.WriteAllText(indexFilePath, "This is a fake widget index file");
                var otherFilePath = Path.Combine(widgetFolderPath, "other.txt");
                File.WriteAllText(otherFilePath, "This is another fake widget file");
                var subfolderPath = Path.Combine(widgetFolderPath, "subfolder");
                Directory.CreateDirectory(subfolderPath);
                var filePathInSubfolder = Path.Combine(subfolderPath, "fileInSubfolder.js");
                File.WriteAllText(filePathInSubfolder, "This is a fake widget file in a subfolder");

                if (simulateWidgetWithSameNameExists)
                    Directory.CreateDirectory(
                        Path.Combine(book.FolderPath, "activities", "my Wid%et")
                    );

                //SUT
                book.InsertPageAfter(existingPage, sourcePage);

                AssertPageCount(book, original + 1);

                // Verify files were copied
                var newActivitiesFolderPath = Path.Combine(book.FolderPath, "activities");
                var expectedNewActivityName =
                    "my Wid%et" + (simulateWidgetWithSameNameExists ? "2" : "");
                var newWidgetPath = Path.Combine(newActivitiesFolderPath, expectedNewActivityName);
                var newIndexFilePath = Path.Combine(newWidgetPath, "index.htm");
                Assert.That(
                    File.ReadAllText(newIndexFilePath),
                    Is.EqualTo("This is a fake widget index file")
                );
                var newOtherFilePath = Path.Combine(newWidgetPath, "other.txt");
                Assert.That(
                    File.ReadAllText(newOtherFilePath),
                    Is.EqualTo("This is another fake widget file")
                );
                var newFilePathInSubfolder = Path.Combine(
                    newWidgetPath,
                    "subfolder",
                    "fileInSubfolder.js"
                );
                Assert.That(
                    File.ReadAllText(newFilePathInSubfolder),
                    Is.EqualTo("This is a fake widget file in a subfolder")
                );

                // Verify the dom is correct
                var newPage = book.GetPages().Last();
                Assert.AreNotEqual(existingPage, newPage);
                Assert.AreNotEqual(existingPage.Id, newPage.Id);
                var newDivNode = newPage.GetDivNodeForThisPage();
                var newWidgetContainer = newDivNode.GetElementsByTagName("div")[0] as XmlElement;
                var newWidgetIframe =
                    newWidgetContainer.GetElementsByTagName("iframe")[0] as XmlElement;
                var src = newWidgetIframe.Attributes["src"]?.Value;
                var expectedNewActivityFolderEncoded =
                    "my%20Wid%25et" + (simulateWidgetWithSameNameExists ? "2" : "");
                Assert.That(
                    src,
                    Is.EqualTo($@"activities/{expectedNewActivityFolderEncoded}/index.htm")
                );
            }
        }

        [Test]
        public void DuplicatePageAfterRelocatePage()
        {
            var book = CreateBook();
            var pages = book.GetPages().ToArray();

            book.RelocatePage(pages[1], 2);
            var rearrangedPages = book.GetPages().ToArray();

            book.DuplicatePage(pages[2]);
            var newPages = book.GetPages().ToArray();

            Assert.AreEqual(3, rearrangedPages.Length);
            Assert.AreEqual(4, newPages.Length);

            // New page (with its own, unique Id) should be directly after the page we copied it from.
            // It was getting inserted first (BL-467)
            Assert.AreEqual("guid1", rearrangedPages[0].Id);
            Assert.AreEqual("guid3", rearrangedPages[1].Id);
            Assert.AreEqual("guid2", rearrangedPages[2].Id);

            Assert.AreEqual("guid1", newPages[0].Id);
            Assert.AreEqual("guid3", newPages[1].Id);
            Assert.AreEqual("guid2", newPages[3].Id);
        }

        [Test]
        public void DeletePage_OnLastPage_Deletes()
        {
            var book = CreateBook();
            var original = book.GetPages().Count();
            var existingPage = book.GetPages().Last();
            book.DeletePage(existingPage);
            AssertPageCount(book, original - 1);
        }

        [Test]
        public void DeletePage_AttemptDeleteLastRemaingPage_DoesntDelete()
        {
            // On a book with Null xmatter (for instance), we don't want Bloom to let us delete the last page.
            var book = CreateBook();
            foreach (var page in book.GetPages())
            {
                book.DeletePage(page);
            }
            AssertPageCount(book, 1);
        }

        [Test]
        public void DeletePage_OnFirstPage_Renumbers()
        {
            var book = CreateBook();
            var original = book.GetPages().Count();
            var firstPage = book.GetPages().First();
            book.DeletePage(firstPage);
            AssertPageCount(book, original - 1);
            var newFirstPage = book.GetPages().First();
            var newFirstDiv = newFirstPage.GetDivNodeForThisPage();
            Assert.AreEqual("1", newFirstDiv.Attributes["data-page-number"].Value);
        }

        [Test]
        public void RelocatePage_FirstPageToSecond_DoesRelocate()
        {
            var book = CreateBook();
            var pages = book.GetPages().ToArray();
            book.RelocatePage(pages[0], 1);
            var newPages = book.GetPages().ToArray();
            Assert.AreEqual(pages[0].Id, newPages[1].Id);
            Assert.AreEqual(pages[1].Id, newPages[0].Id);
            Assert.AreEqual(pages[2].Id, newPages[2].Id);
            Assert.AreEqual(3, pages.Length);
        }

        [Test]
        public void RelocatePage_FirstPageToLast_DoesRelocate()
        {
            var book = CreateBook();
            var pages = book.GetPages().ToArray();
            book.RelocatePage(pages[0], 2);
            var newPages = book.GetPages().ToArray();
            Assert.AreEqual(pages[1].Id, newPages[0].Id);
            Assert.AreEqual(pages[2].Id, newPages[1].Id);
            Assert.AreEqual(pages[0].Id, newPages[2].Id);
            Assert.AreEqual(3, pages.Length);
        }

        [Test]
        public void RelocatePage_LastPageToSecond_DoesRelocate()
        {
            var book = CreateBook();
            var pages = book.GetPages().ToArray();
            book.RelocatePage(pages[2], 1);
            var newPages = book.GetPages().ToArray();
            Assert.AreEqual(pages[0].Id, newPages[0].Id);
            Assert.AreEqual(pages[2].Id, newPages[1].Id);
            Assert.AreEqual(pages[1].Id, newPages[2].Id);
            Assert.AreEqual(3, pages.Length);
        }

        /// <summary>
        /// regression test
        /// </summary>
        [Test]
        public void RelocatePage_SuccessiveRelocates_BothWork()
        {
            var book = CreateBook();
            var pages = book.GetPages().ToArray();
            book.RelocatePage(pages[1], 0);
            book.RelocatePage(pages[2], 1);
            var newPages = book.GetPages().ToArray();
            Assert.AreEqual(pages[1].Id, newPages[0].Id);
            Assert.AreEqual(pages[2].Id, newPages[1].Id);
            Assert.AreEqual(pages[0].Id, newPages[2].Id);
            Assert.AreEqual(3, pages.Length);
        }

        [Test]
        public void RelocatePage_LastPageToFirst_DoesRelocate()
        {
            var book = CreateBook();
            var pages = book.GetPages().ToArray();
            book.RelocatePage(pages[2], 0);
            var newPages = book.GetPages().ToArray();
            Assert.AreEqual(pages[2].Id, newPages[0].Id);
            Assert.AreEqual(pages[0].Id, newPages[1].Id);
            Assert.AreEqual(pages[1].Id, newPages[2].Id);
            Assert.AreEqual(3, pages.Length);
        }

        [Test]
        public void CanDelete_VernacularBook_True()
        {
            var book = CreateBook();
            Assert.IsTrue(book.CanDelete);
        }

        [Test, Ignore("broken")]
        public void CanDelete_TemplateBook_False()
        {
            var book = CreateBook();
            Assert.IsFalse(book.CanDelete);
        }

        [Test]
        public void GetBookletLayoutMethod_A5Portrait_NotCalendar_Fold()
        {
            _bookDom = new HtmlDom(
                @"<html ><head>
									</head><body></body></html>"
            );
            var book = CreateBook();
            Assert.AreEqual(
                PublishModel.BookletLayoutMethod.SideFold,
                book.GetBookletLayoutMethod(Layout.A5Portrait)
            );
        }

        [Test]
        public void GetBookletLayoutMethod_CalendarSpecifiedInBook_Calendar()
        {
            _bookDom = new HtmlDom(
                @"<html ><head>
									<meta name='defaultBookletLayout' content='Calendar'/>
									</head><body></body></html>"
            );
            var book = CreateBook();
            Assert.AreEqual(
                PublishModel.BookletLayoutMethod.Calendar,
                book.GetBookletLayoutMethod(Layout.A5Portrait)
            );
            Assert.AreEqual(
                PublishModel.BookletLayoutMethod.Calendar,
                book.GetBookletLayoutMethod(A5Landscape)
            );
        }

        [Test]
        public void GetCoverColorFromDom_RegularHexCode_Works()
        {
            const string xml =
                @"<html><head>
				<style type='text/css'>
				    DIV.bloom-page.coverColor       {               background-color: #abcdef !important;   }
				</style>
			</head><body></body></html>";
            var document = new XmlDocument();
            document.LoadXml(xml);

            // SUT
            var result = Bloom.Book.Book.GetCoverColorFromDom(document);

            Assert.AreEqual("#abcdef", result);
        }

        [Test]
        public void GetCoverColorFromDom_ColorWordWithComment_Works()
        {
            // This is from the Digital Comic Book template. (lowercase 'div' and intervening comment)
            const string xml =
                @"<html><head>
				<meta name='preserveCoverColor' content='true'></meta>
			    <style type='text/css'>
				    div.bloom-page.coverColor {
				        /* note that above, we have a meta ""preserveCoverColor"" tag to preserve this*/
				        background-color: black !important;
				    }
			    </style>
			</head><body></body></html>";
            var document = new XmlDocument();
            document.LoadXml(xml);

            // SUT
            var result = Bloom.Book.Book.GetCoverColorFromDom(document);

            Assert.AreEqual("black", result);
        }

        [Test]
        public void GetCoverColorFromDom_MoonAndCapVersion_Works()
        {
            // This is from the Moon and Cap example book. (lowercase 'div' and extraneous textarea rule)
            const string xml =
                @"<html><head>
			    <style type='text/css'>
				    div.coverColor textarea {
				    background-color: #ffd4d4 !important;
				    }
				    div.bloom-page.coverColor {
				    background-color: #ffd4d4 !important;
				    }
			    </style>
			</head><body></body></html>";
            var document = new XmlDocument();
            document.LoadXml(xml);

            // SUT
            var result = Bloom.Book.Book.GetCoverColorFromDom(document);

            Assert.AreEqual("#ffd4d4", result);
        }

        private Layout A5Landscape =>
            new Layout() { SizeAndOrientation = SizeAndOrientation.FromString("A5Landscape") };

        [Test]
        public void GetBookletLayoutMethod_A5Landscape_NotCalendar_CutAndStack()
        {
            _bookDom = new HtmlDom(
                @"<html ><head>
									</head><body></body></html>"
            );
            var book = CreateBook();
            Assert.AreEqual(
                PublishModel.BookletLayoutMethod.CutAndStack,
                book.GetBookletLayoutMethod(A5Landscape)
            );
        }

        [Test]
        public void GetDefaultBookletLayoutMethod_NotSpecified_Fold()
        {
            _bookDom = new HtmlDom(
                @"<html ><head>
									</head><body></body></html>"
            );
            var book = CreateBook();
            Assert.AreEqual(
                PublishModel.BookletLayoutMethod.SideFold,
                book.GetDefaultBookletLayoutMethod()
            );
        }

        [Test]
        public void GetDefaultBookletLayoutMethod_CalendarSpecified_Calendar()
        {
            _bookDom = new HtmlDom(
                @"<html ><head>
									<meta name='defaultBookletLayout' content='Calendar'/>
									</head><body></body></html>"
            );
            var book = CreateBook();
            Assert.AreEqual(
                PublishModel.BookletLayoutMethod.Calendar,
                book.GetDefaultBookletLayoutMethod()
            );
        }

        [Test]
        public void BringBookUpToDate_DomHas3ContentLanguages_PulledIntoBookProperties()
        {
            _bookDom = new HtmlDom(
                @"<html><head><div id='bloomDataDiv'><div data-book='contentLanguage1'>fr</div><div data-book='contentLanguage2'>xyz</div><div data-book='contentLanguage3'>en</div></div></head><body></body></html>"
            );
            var book = CreateBook();
            book.BringBookUpToDate(new NullProgress());
            Assert.AreEqual("fr", book.Language1Tag);
            Assert.AreEqual("xyz", book.Language2Tag);
            Assert.AreEqual("en", book.Language3Tag);
        }

        //regression test
        [Test]
        public void BringBookUpToDate_A4LandscapeWithNoContentPages_RemainsA4Landscape()
        {
            // We need the reference to basic book.css because that's where the list of valid page layouts lives,
            // and Bloom will force the book to A5Portrait if it can't verify that A4Landscape is valid.
            _bookDom = new HtmlDom(
                @"
				<html>
					<head>
						<meta name='xmatter' content='Traditional'/>
						<link rel='stylesheet' href='Basic Book.css' type='text / css'></link>
					</head>
					<body>
						<div class='bloom-page cover coverColor bloom-frontMatter A4Landscape' data-page='required'>
						</div>
					</body>
				</html>"
            );
            var book = CreateBook();
            // AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'A4Landscape') and contains(@class,'bloom-page')]", 5);
            book.BringBookUpToDate(new NullProgress());
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'A4Landscape') and contains(@class,'bloom-page')]",
                    6
                );
        }

        /// <summary>
        /// regression test... when we rebuild the xmatter, we also need to update the html attributes that let us
        /// know the state of the image metadata without having to open the image up (slow).
        /// </summary>
        [Test]
        [Category("SkipOnTeamCity")]
        public void BringBookUpToDate_CoverImageHasMetaData_HtmlForCoverPageHasMetaDataAttributes()
        {
            _bookDom = new HtmlDom(
                @"
				<html>
					<body>
						<div id='bloomDataDiv'>
							<div data-book='coverImage'>test.png</div>
						</div>
					</body>
				</html>"
            );

            var book = CreateBook();
            var imagePath = book.FolderPath.CombineForPath("test.png");
            MakeSamplePngImageWithMetadata(imagePath);

            book.BringBookUpToDate(new NullProgress());
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//*[@data-book='coverImage' and @data-creator='joe']",
                    1
                );
        }

        [Test]
        public void BringBookUpToDate_RepairQuestionsPages_DoesNotMessUpGoodPages()
        {
            const string xpathQuestionsPrefix = "//div[contains(@class,'questions')]";
            _bookDom = new HtmlDom(
                @"
				<html>
					<head />
					<body>
						<div class='bloom-page bloom-nonprinting questions'>
							<div class='marginBox'>
								<div>
									<div class='quizInstructions'>Some gobbledy-gook</div>
								</div>
								<div>
									<div class='bloom-translationGroup quiz-style quizContents bloom-noAudio bloom-userCannotModifyStyles'>
										<div class='bloom-editable bloom-content1 bloom-contentNational1' contenteditable='true' lang='en'>
											My test question. <br/>
											<p>‌Answer 1 </p>
											<p>‌*Answer 2 </p>
											<p>‌</p>
											<p>‌Second test question </p>
											<p>‌*Some right answer </p>
											<p>‌Some wrong answer </p>
										</div>
									</div>
								</div>
							</div>
						</div>
					</body>
				</html>"
            );

            var book = CreateBook();

            book.BringBookUpToDate(new NullProgress());
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    xpathQuestionsPrefix
                        + "//div[contains(@class,'bloom-noAudio') and contains(@class,'bloom-userCannotModifyStyles')]",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(xpathQuestionsPrefix + "//div//p", 6);
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(xpathQuestionsPrefix + "//div//br", 1);
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-nonprinting')]",
                    1
                );
        }

        [Test]
        public void BringBookUpToDate_RepairQuestionsPages_Works()
        {
            // tests migrating nonprinting class to bloom-nonprinting
            // tests cleaning out audio spans from questions
            // tests adding two classes to question content divs: 'bloom-noAudio' and 'bloom-userCannotModifyStyles'
            const string xpathQuestionsPrefix = "//div[contains(@class,'questions')]";
            _bookDom = new HtmlDom(
                @"
				<html>
					<head />
					<body>
						<div class='bloom-page questions nonprinting'>
							<div class='marginBox'>
								<div>
									<div class='quizInstructions'>Some gobbledy-gook</div>
								</div>
								<div>
									<div class='bloom-translationGroup quiz-style quizContents'>
										<div class='bloom-editable bloom-content1 bloom-contentNational1' contenteditable='true' lang='en'>
											<h1>My test question.</h1> <br/>
											<p>‌Answer 1 </p>
											<p>‌*Ans<span class='audio-sentence'>wer 2</span></p>
											<p>‌</p>
											<p>‌Second test question <em>weird stuff!</em></p>
											<p>*Some right answer</p>
											<p><span data-duration='1.600227' id='i125f143d-7c30-44c1-8d23-0e000f484e08' class='audio-sentence' recordingmd5='undefined'>My test text.</span></p>
										</div>
									</div>
								</div>
							</div>
						</div>
					</body>
				</html>"
            );

            var book = CreateBook();

            book.BringBookUpToDate(new NullProgress());
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    xpathQuestionsPrefix
                        + "//div[contains(@class,'bloom-noAudio') and contains(@class,'bloom-userCannotModifyStyles')]",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath(xpathQuestionsPrefix + "//div//span");
            AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath(xpathQuestionsPrefix + "//div//em");
            AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath(xpathQuestionsPrefix + "//div//h1");
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    xpathQuestionsPrefix + "//div//p[.='‌*Answer 2']",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    xpathQuestionsPrefix + "//div//p[.='My test text.']",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-nonprinting')]",
                    1
                );
        }

        [Test]
        public void BringBookUpToDate_LanguagesOfBookUpdated()
        {
            _bookDom = new HtmlDom(
                @"
				<html>
					<head>
						<meta name='xmatter' content='Traditional'/>
					</head>
					<body>
						<div id='bloomDataDiv'>
							<div data-book='languagesOfBook' lang='*'>
								English
							</div>
						</div>
					</body>
				</html>"
            );
            var book = CreateBook();
            book.BookData.Language1.SetName("My Language Name", true);
            book.BringBookUpToDate(new NullProgress());
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@data-book='languagesOfBook' and text()='My Language Name' and @lang='*']",
                    1
                );
            // We need to specify the language for display: see https://issues.bloomlibrary.org/youtrack/issue/BL-7968.
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@data-derived='languagesOfBook' and text()='My Language Name' and @lang='en']",
                    2
                );
        }

        private TempFile MakeTempImage(string name)
        {
            using (var x = new Bitmap(100, 100))
            {
                x.Save(Path.Combine(Path.GetTempPath(), name), ImageFormat.Png);
            }
            return TempFile.TrackExisting(name);
        }

        [Test]
        public void GetPreviewHtmlFileForWholeBook_InjectedCoverHasCorrectImage()
        {
            _bookDom = new HtmlDom(
                @"
				<html>
					<body>
						<div id='bloomDataDiv'>
							<div data-book='coverImage'>theCover.png</div>
						</div>
					</body>
				</html>"
            );

            var book = CreateBook();

            var imagePath = book.FolderPath.CombineForPath("theCover.png");
            MakeSamplePngImageWithMetadata(imagePath);

            book.BringBookUpToDate(new NullProgress());
            var dom = book.GetPreviewHtmlFileForWholeBook();

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-imageContainer')]/img[@data-book='coverImage' and @src='theCover.png']",
                    1
                );
        }

        [Test]
        [Category("SkipOnTeamCity")]
        public void UpdateImgMetdataAttributesToMatchImage_HtmlForImgGetsMetaDataAttributes()
        {
            _bookDom = new HtmlDom(
                @"
				<html>
					<body>
					   <div class='bloom-page'>
							<div class='marginBox'>
								<div class='bloom-imageContainer'>
								  <img src='test.png'/>
								</div>
							</div>
						</div>
					</body>
				</html>"
            );

            var book = CreateBook();
            var imagePath = book.FolderPath.CombineForPath("test.png");
            MakeSamplePngImageWithMetadata(imagePath);

            book.BringBookUpToDate(new NullProgress());
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//*[@src='test.png' and @data-creator='joe']",
                    1
                );
        }

        [Test]
        public void BringBookUpToDate_UpdatesRtlInMetaJson()
        {
            _bookDom = new HtmlDom(
                @"<html ><head>
									</head><body></body></html>"
            );
            var book = CreateBook();
            Assert.That(book.BookInfo.IsRtl, Is.False);
            var old = book.BookData.Language1.IsRightToLeft;
            try
            {
                book.BookData.Language1.IsRightToLeft = true;
                book.BringBookUpToDate(new NullProgress());
            }
            finally
            {
                book.BookData.Language1.IsRightToLeft = old;
            }
            Assert.That(book.BookInfo.IsRtl, Is.True);
        }

        [Test]
        public void BringBookUpToDate_MovesMetaDataToJson()
        {
            _bookDom = new HtmlDom(
                @"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<meta name='bookLineage' content='old rubbish' />
					<meta name='bloomBookLineage' content='first,second' />
					<meta name='bloomBookId' content='MyId' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='en'>my nice title</div>
					</div>
					<div class='bloom-page'>
						<div class='bloom-page' id='guid2'>
							<textarea lang='en' data-book='bookTitle'>my nice title</textarea>
						</div>
					</div>
				</body></html>"
            );

            var book = CreateBook();
            book.BringBookUpToDate(new NullProgress());

            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//meta[@name='bloomBookLineage']", 0);
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//meta[@name='bookLineage']", 0);
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//meta[@name='bloomBookId']", 0);

            Assert.That(_metadata.Id, Is.EqualTo("MyId"));
            Assert.That(_metadata.BookLineage, Is.EqualTo("first,second"));
            Assert.That(_metadata.Title, Is.EqualTo("my nice title"));
            // Checking the defaults, when not specified in the metadata
            Assert.That(_metadata.IsSuitableForMakingShells, Is.False);
            Assert.That(_metadata.IsSuitableForVernacularLibrary, Is.True);

            _bookDom = new HtmlDom(
                @"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<meta name='SuitableForMakingShells' content='yes' />
					<meta name='SuitableForMakingVernacularBooks' content='no' />
					<meta name='bloomBookId' content='MyId' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page'>
						<div class='bloom-page' id='guid2'>
							<textarea lang='en' data-book='bookTitle'>my nice title</textarea>
						</div>
					</div>
				</body></html>"
            );

            book = CreateBook();
            book.BringBookUpToDate(new NullProgress());
            // BL-2163, we are no longer migrating suitableForMakingShells
            Assert.That(_metadata.IsSuitableForMakingShells, Is.False);
            Assert.That(_metadata.IsSuitableForVernacularLibrary, Is.False);
        }

        [Test]
        public void BringBookUpToDate_FixesOldCharacterStyleMarkup()
        {
            _bookDom = new HtmlDom(
                @"<html>
<head>
	<meta content='text/html; charset=utf-8' http-equiv='content-type' />
</head>
<body>
	<div class='bloom-page'>
		<div class='bloom-page' id='guid2'>
			<div class='marginBox'>
				<div class='split-pane-component position-top' style='height: 100%'>
					<div class='split-pane-component-inner'>
						<div class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto'>
							<div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 24px;' class='bloom-editable normal-style bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
								<p><b><i>This is </i></b><b><i>a test!</i></b></p>
								<p><b>Do you like green eggs and ham?</b>"
                    + "\u00A0"
                    + @" <b>I do not like them, Sam-I-am."
                    + "\u00A0"
                    + @"</b><b> I do not like green eggs and ham.</b></p>
							</div>
							<div style='' class='bloom-editable normal-style' contenteditable='true' lang='z'>
								<p></p>
							</div>
						</div>
					</div>
				</div>
			</div>
		</div>
	</div>
</body>
</html>"
            );

            var book = CreateBook();
            // Verify initial conditions.
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-editable')]/p/b",
                    5
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-editable')]/p/b/i",
                    2
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath("//div[contains(@class,'bloom-editable')]/p/strong");
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath("//div[contains(@class,'bloom-editable')]/p/strong/em");

            book.BringBookUpToDate(new NullProgress());
            // Check that the problem reported in BL-8711 has been fixed.
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath("//div[contains(@class,'bloom-editable')]/p/b");
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath("//div[contains(@class,'bloom-editable')]/p/b/i");
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-editable')]/p/strong",
                    2
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-editable')]/p/strong/em",
                    1
                );
            Assert.That(
                book.RawDom.InnerXml,
                Does.Contain("<p><strong><em>This is a test!</em></strong></p>"),
                "new markup imposed preserving spaces"
            );
            Assert.That(
                book.RawDom.InnerXml,
                Does.Contain(
                    "<p><strong>Do you like green eggs and ham?\u00A0 I do not like them, Sam-I-am.\u00A0 I do not like green eggs and ham.</strong></p>"
                ),
                "new markup imposed preserving sentence spacing"
            );
        }

        [Test]
        public void BringBookUpToDate_FixesDuplicateAudioIds_SentenceRecording()
        {
            _bookDom = new HtmlDom(
                @"<html>
  <head>
    <meta content='text/html; charset=utf-8' http-equiv='content-type' />
  </head>
  <body>
    <div id=""bloomDataDiv"">
      <div data-book=""bookTitle"" lang=""tpi"" class="" bloom-editable bloom-nodefaultstylerule bloom-padForOverflow"" contenteditable=""true"">
        <p><span id=""c22315e7-c8f7-4d9d-89a5-d4f5c79d8777"" class=""audio-sentence"" recordingmd5=""undefined"">Primer 1 <span class=""bloom-linebreak""></span>﻿Ritim stori</span> i kam long <span class=""bloom-linebreak""></span>﻿buk bilong Mak</p>
      </div>
    </div>
    <div class=""bloom-page cover coverColor bloom-frontMatter frontCover outsideFrontCover A4Portrait side-right"" data-page=""required singleton"" data-export=""front-matter-cover"" data-xmatter-page=""frontCover"" id=""918c80f9-8ecf-4656-b4a9-e4c757459df6"" data-page-number="""" lang="""">
      <div class=""marginBox"">
        <div class=""bloom-translationGroup bookTitle"" data-default-languages=""V,N1"">
          <div class=""bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-padForOverflow bloom-content1 bloom-contentNational2 bloom-visibility-code-on"" data-book=""bookTitle"" data-languagetipcontent=""Tok Pisin"" style=""padding-bottom: 0px;"" data-hasqtip=""true"" aria-describedby=""qtip-3"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" lang=""tpi"" contenteditable=""true"">
            <p><span id=""c22315e7-c8f7-4d9d-89a5-d4f5c79d8777"" class=""audio-sentence"" recordingmd5=""undefined"">Primer 1 <span class=""bloom-linebreak""></span>﻿Ritim stori</span> i kam long <span class=""bloom-linebreak""></span>﻿buk bilong Mak</p>
          </div>
        </div>
      </div>
    </div>
    <div class=""bloom-page titlePage bloom-frontMatter countPageButDoNotShowNumber A4Portrait side-right"" data-page=""required singleton"" data-export=""front-matter-title-page"" data-xmatter-page=""titlePage"" id=""6901d777-3338-4296-945e-877b9bf73b8b"" data-page-number="""" lang="""">
      <div class=""marginBox"">
        <div class=""bloom-translationGroup"" data-default-languages=""V,N1"" id=""titlePageTitleBlock"">
          <div class=""bloom-editable bloom-nodefaultstylerule Title-On-Title-Page-style bloom-padForOverflow bloom-content1 bloom-contentNational2 bloom-visibility-code-on"" data-book=""bookTitle"" data-languagetipcontent=""Tok Pisin"" style=""padding-bottom: 0px;"" data-hasqtip=""true"" aria-describedby=""qtip-0"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" lang=""tpi"" contenteditable=""true"">
            <p><span id=""c22315e7-c8f7-4d9d-89a5-d4f5c79d8777"" class=""audio-sentence"" recordingmd5=""undefined"">Primer 1 <span class=""bloom-linebreak""></span>﻿Ritim stori</span> i kam long <span class=""bloom-linebreak""></span>﻿buk bilong Mak</p>
          </div>
        </div>
      </div>
    </div>
    <div class='bloom-page numberedPage' data-page='' id='ff4335b6-07d9-4b7a-b2fe-f836c9da4c96' data-pagelineage='5dcd48df-e9ab-4a07-afd4-6a24d0398386' lang='' data-page-number='8'>
      <div class='marginBox'>
        <div class='bloom-translationGroup bloom-trailingElement'>
          <div data-languagetipcontent='Tok Pisin' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable normal-style bloom-content1 bloom-contentNational2 bloom-visibility-code-on' contenteditable='true' lang='tpi'>
            <p><span id='i4d78a211-e1cc-483c-8b54-4256967e683f' class='audio-sentence' recordingmd5='undefined'>Jisas i wokabaut arere long raunwara Galili, orait, em i lukim tupela brata, Saimon, narapela nem em Pita, na brata bilong en Andru.</span><span id='ddcbbf14-445e-4bfa-ad0a-94a9223dff3d' class='audio-sentence' recordingmd5='undefined'>Olsem na em i kam tokim tupela, 'E, yutupela i kam bihainim mi, na bai mi lainim yutupela long kisim manmeri ol i ken bihainim mi.'</span></p>
          </div>
        </div>
      </div>
    </div>
    <div class='bloom-page numberedPage customPage A4Portrait side-right bloom-monolingual' data-page='' id='c430b078-b953-495e-a19c-db34d0a4789b' data-pagelineage='5dcd48df-e9ab-4a07-afd4-6a24d0398386;ff4335b6-07d9-4b7a-b2fe-f836c9da4c96' lang='' data-page-number='28'>
      <div class='marginBox'>
        <div class='bloom-translationGroup bloom-trailingElement'>
          <div data-languagetipcontent='Tok Pisin' data-hasqtip='true' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable normal-style bloom-content1 bloom-contentNational2 bloom-visibility-code-on' contenteditable='true' lang='tpi'>
            <p><span id='i4d78a211-e1cc-483c-8b54-4256967e683f' class='audio-sentence' recordingmd5='undefined'></span>Na klostu long dua tu i no gat hap ples i stap nating.</p>
            <p><span id='ddcbbf14-445e-4bfa-ad0a-94a9223dff3d' class='audio-sentence' recordingmd5='undefined'></span></p>
          </div>
        </div>
      </div>
    </div>
  </body>
</html>"
            );
            var book = CreateBook();

            // This has a total of seven audio-sentence spans and no audio-sentence divs.
            // check initial conditions
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath("//div[contains(@class,'audio-sentence')]");
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id!='']",
                    7
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='c22315e7-c8f7-4d9d-89a5-d4f5c79d8777']",
                    3
                ); // bookTitle
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='i4d78a211-e1cc-483c-8b54-4256967e683f']",
                    2
                ); // ERROR!
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='ddcbbf14-445e-4bfa-ad0a-94a9223dff3d']",
                    2
                ); // ERROR!

            book.BringBookUpToDate(new NullProgress()); // SUT

            // final condition with title audio id found 3 times and no duplication in content audio ids
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath("//div[contains(@class,'audio-sentence')]");
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id!='']",
                    7
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='c22315e7-c8f7-4d9d-89a5-d4f5c79d8777']",
                    3
                ); // bookTitle (same)
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='i4d78a211-e1cc-483c-8b54-4256967e683f']",
                    1
                ); // fixed!
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='ddcbbf14-445e-4bfa-ad0a-94a9223dff3d']",
                    1
                ); // fixed!

            // Check that all the audio ids outside the data-div are unique.
            var audioNodes = book.RawDom
                .SafeSelectNodes(
                    "(//div[contains(@class,'bloom-page')]//div|//div[contains(@class,'bloom-page')]//span)[contains(@class,'audio-sentence') and @id]"
                )
                .Cast<XmlNode>()
                .ToList();
            HashSet<string> uniqueIds = new HashSet<string>();
            foreach (var node in audioNodes)
            {
                var id = node.GetStringAttribute("id");
                uniqueIds.Add(id);
            }
            Assert.That(audioNodes.Count, Is.EqualTo(uniqueIds.Count + 1)); // NB: bookTitle occurs twice
        }

        [Test]
        public void BringBookUpToDate_FixesDuplicateAudioIds_TextboxRecording()
        {
            _bookDom = new HtmlDom(
                @"<html>
  <head>
    <meta content='text/html; charset=utf-8' http-equiv='content-type' />
  </head>
  <body>
    <div id='bloomDataDiv'>
      <div data-book='bookTitle' lang='en' class=' bloom-editable bloom-nodefaultstylerule bloom-padForOverflow audio-sentence bloom-postAudioSplit' contenteditable='true' data-audiorecordingmode='TextBox' data-duration='1.227734' id='ee2f4638-8892-4a13-9abd-be43417236cc' data-audiorecordingendtimes='1.16'>
        <p><span id='i03ce4c5d-c70d-4702-9d69-9ed6ccd5a9a1' class='bloom-highlightSegment'>I am sick</span></p>
      </div>
      <div data-book='smallCoverCredits' lang='en' class=' bloom-editable smallCoverCredits audio-sentence bloom-postAudioSplit' contenteditable='true' data-audiorecordingmode='TextBox' data-audiorecordingendtimes='4.000 7.560' data-duration='7.641882' id='i07d27e74-f55e-4adf-b09e-16c3aae93789'>
        <p><span id='cd909d76-2eac-4c2c-a646-449535de6d55' class='bloom-highlightSegment' recordingmd5='undefined'>Author: Namita Jacob</span></p>
        <p><span id='i178b7798-3923-4fb4-a816-136e6c9010a7' class='bloom-highlightSegment' recordingmd5='undefined'>Illustrator: Teresa Antony</span></p>
      </div>
      <div data-book='funding' lang='en' class=' bloom-editable funding bloom-copyFromOtherLanguageIfNecessary audio-sentence bloom-postAudioSplit thisOverflowingParent' contenteditable='true' data-audiorecordingmode='TextBox' data-audiorecordingendtimes='3.8' data-duration='3.880249' id='i1010a3ba-2abe-4a1b-a945-74b5cfbe0130'>
        <p><span id='c1e48f12-0270-4a40-98b4-35917b459207' class='bloom-highlightSegment'>Published by Chetana Trust, INDIA</span></p>
      </div>
      <div data-book='outsideBackCover' lang='en' data-hint='If you need somewhere to put more information about the book, you can use this page, which is the outside of the back cover.' class=' bloom-editable audio-sentence bloom-postAudioSplit' contenteditable='true' data-audiorecordingmode='TextBox' data-audiorecordingendtimes='8.840 11.600 14.400' data-duration='14.49771' id='i98cce2ab-2618-43b2-b9b6-6437eb134501'>
        <p><span id='i745d1faa-fb1a-4580-a19b-8cfed3f3042a' class='bloom-highlightSegment'>Written during the Pandemic of 2020, this book was created to help children who are ill or who have loved ones who are ill.</span></p>
        <p><span id='i3d292196-dab5-41c5-bb62-dcefb8ab2531' class='bloom-highlightSegment'>This is an original creation of</span></p>
        <p><span id='i9bc70333-4f50-47d3-9763-c5fb92922640' class='bloom-highlightSegment'><strong>Chetana Charitable Trust, Chennai</strong></span></p>
      </div>
    </div>
    <div class='bloom-page cover coverColor bloom-frontMatter frontCover outsideFrontCover Device16x9Portrait side-right' data-page='required singleton' data-export='front-matter-cover' data-xmatter-page='frontCover' id='cf4a0b00-6ef1-46ff-b883-ce68fa4140c8' data-page-number='' lang=''>
      <div class='marginBox'>
        <div class='bloom-translationGroup bookTitle' data-default-languages='V,N1'>
          <div aria-describedby='qtip-2' data-hasqtip='true' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 44.01px; padding-bottom: 0px;' data-languagetipcontent='English' class='bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-padForOverflow audio-sentence bloom-postAudioSplit bloom-content1 bloom-contentNational1 bloom-visibility-code-on' data-book='bookTitle' data-audiorecordingmode='TextBox' data-duration='1.227734' id='ee2f4638-8892-4a13-9abd-be43417236cc' data-audiorecordingendtimes='1.16' contenteditable='true' lang='en'>
            <p><span id='i03ce4c5d-c70d-4702-9d69-9ed6ccd5a9a1' class='bloom-highlightSegment'>I am sick</span></p>
          </div>
        </div>
        <div class='bottomTextContent'>
          <div class='bloom-translationGroup' data-default-languages='V'>
            <div aria-label='false' role='textbox' spellcheck='true' tabindex='0' data-languagetipcontent='English' class='bloom-editable smallCoverCredits Cover-Default-style audio-sentence bloom-postAudioSplit bloom-content1 bloom-contentNational1 bloom-visibility-code-on' data-book='smallCoverCredits' data-audiorecordingmode='TextBox' data-audiorecordingendtimes='4.000 7.560' data-duration='7.641882' id='i07d27e74-f55e-4adf-b09e-16c3aae93789' contenteditable='true' lang='en'>
              <p><span id='cd909d76-2eac-4c2c-a646-449535de6d55' class='bloom-highlightSegment' recordingmd5='undefined'>Author: Namita Jacob</span></p>
              <p><span id='i178b7798-3923-4fb4-a816-136e6c9010a7' class='bloom-highlightSegment' recordingmd5='undefined'>Illustrator: Teresa Antony</span></p>
            </div>
          </div>
        </div>
      </div>
    </div>
    <div class='bloom-page titlePage bloom-frontMatter countPageButDoNotShowNumber Device16x9Portrait side-right' data-page='required singleton' data-export='front-matter-title-page' data-xmatter-page='titlePage' id='3b22b321-bed6-4797-8b5d-638798a43d08' data-page-number='' lang=''>
      <div class='marginBox'>
        <div class='bloom-translationGroup' data-default-languages='V,N1' id='titlePageTitleBlock'>
          <div aria-describedby='qtip-2' data-languagetipcontent='English' data-hasqtip='true' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='padding-bottom: 0px;' class='bloom-editable bloom-nodefaultstylerule Title-On-Title-Page-style bloom-padForOverflow audio-sentence bloom-postAudioSplit bloom-content1 bloom-contentNational1 bloom-visibility-code-on' data-book='bookTitle' data-audiorecordingmode='TextBox' data-duration='1.227734' id='ee2f4638-8892-4a13-9abd-be43417236cc' data-audiorecordingendtimes='1.16' contenteditable='true' lang='en'>
            <p><span id='i03ce4c5d-c70d-4702-9d69-9ed6ccd5a9a1' class='bloom-highlightSegment'>I am sick</span></p>
          </div>
        </div>
        <div class='bloom-translationGroup' data-default-languages='N1' id='funding'>
          <div aria-describedby='qtip-1' data-languagetipcontent='English' data-audiorecordingendtimes='3.8' data-hasqtip='true' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable funding Content-On-Title-Page-style bloom-copyFromOtherLanguageIfNecessary audio-sentence bloom-postAudioSplit bloom-content1 bloom-contentNational1 bloom-visibility-code-on thisOverflowingParent' data-book='funding' data-audiorecordingmode='TextBox' data-duration='3.880249' id='i1010a3ba-2abe-4a1b-a945-74b5cfbe0130' contenteditable='true' lang='en'>
            <p><span id='c1e48f12-0270-4a40-98b4-35917b459207' class='bloom-highlightSegment'>Published by Chetana Trust, INDIA</span></p>
          </div>
        </div>
      </div>
    </div>
    <div class='bloom-page numberedPage customPage Device16x9Portrait side-left bloom-monolingual' data-page='' id='4abdbd70-9922-48ca-98e3-900917fed9e7' data-pagelineage='a31c38d8-c1cb-4eb9-951b-d2840f6a8bdb' data-page-number='2' lang=''>
      <div class='marginBox'>
        <div tabindex='1' class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto'>
          <div data-audiorecordingendtimes='3.800 5.860 7.340 9.120' data-languagetipcontent='English' data-duration='9.209229' id='i8a56eaf1-f6ff-46c1-b973-c1a82360f40b' data-audiorecordingmode='TextBox' data-hasqtip='true' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 62.4px;' class='bloom-editable normal-style audio-sentence bloom-postAudioSplit bloom-content1 bloom-contentNational1 bloom-visibility-code-on' contenteditable='true' lang='en'>
            <p><span id='ff22a080-da1a-40c2-9c2b-44889eb60766' class='bloom-highlightSegment'>Today when I woke up, I had a headache.</span> <span id='e5cba82b-8313-46c6-a729-2804f0afca03' class='bloom-highlightSegment'>I had a runny nose.</span> <span id='i3b5c447b-89b6-4f9e-95e7-f97836d7ad35' class='bloom-highlightSegment'>I had fever.</span> <span id='i16ef936a-0cca-4247-9c07-63034d2e1803' class='bloom-highlightSegment'>I am sick!</span></p>
          </div>
        </div>
      </div>
    </div>
    <div class='bloom-page numberedPage customPage Device16x9Portrait side-right bloom-monolingual' data-page='' id='3d75bc11-3938-481b-9d29-1a162f7fea33' data-pagelineage='a31c38d8-c1cb-4eb9-951b-d2840f6a8bdb;4abdbd70-9922-48ca-98e3-900917fed9e7' data-page-number='3' lang=''>
      <div class='marginBox'>
        <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto'>
          <div data-languagetipcontent='English' data-audiorecordingendtimes='2.160 4.380 7.800' id='i8a56eaf1-f6ff-46c1-b973-c1a82360f40b' data-audiorecordingmode='TextBox' data-hasqtip='true' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 62.4px;' class='bloom-editable normal-style audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on' contenteditable='true' lang='en'>
            <p><span id='i64ca42d8-6742-4e1b-89a8-8a1c85249ec6' class='bloom-highlightSegment' recordingmd5='undefined'>But, I am not worried!</span> <span id='i9656ff07-29c6-4961-9f62-b78e596f95f5' class='bloom-highlightSegment' recordingmd5='undefined'>I have been sick before!</span> <span id='i9dd5cac2-e700-4e6b-9c1e-e9d4b94b06c2' class='bloom-highlightSegment' recordingmd5='undefined'>I can stay in bed <em>all</em> day!</span></p>
          </div>
        </div>
      </div>
    </div>
    <div class='bloom-page cover coverColor outsideBackCover bloom-backMatter Device16x9Portrait side-right' data-page='required singleton' data-export='back-matter-back-cover' data-xmatter-page='outsideBackCover' id='411e063b-50a4-4f92-beb6-5c248cded6b1' data-page-number='' lang=''>
      <div class='marginBox'>
        <div class='bloom-translationGroup' data-default-languages='N1'>
          <div aria-describedby='qtip-0' data-languagetipcontent='English' data-audiorecordingendtimes='8.840 11.600 14.400' aria-label='false' role='textbox' spellcheck='true' tabindex='0' data-hasqtip='true' class='bloom-editable Outside-Back-Cover-style audio-sentence bloom-postAudioSplit bloom-content1 bloom-contentNational1 bloom-visibility-code-on' data-book='outsideBackCover' data-hint='If you need somewhere to put more information about the book, you can use this page, which is the outside of the back cover.' data-audiorecordingmode='TextBox' data-duration='14.49771' id='i98cce2ab-2618-43b2-b9b6-6437eb134501' contenteditable='true' lang='en'>
            <p><span id='i745d1faa-fb1a-4580-a19b-8cfed3f3042a' class='bloom-highlightSegment'>Written during the Pandemic of 2020, this book was created to help children who are ill or who have loved ones who are ill.</span></p>
            <p><span id='i3d292196-dab5-41c5-bb62-dcefb8ab2531' class='bloom-highlightSegment'>This is an original creation of</span></p>
            <p><span id='i9bc70333-4f50-47d3-9763-c5fb92922640' class='bloom-highlightSegment'><strong>Chetana Charitable Trust, Chennai</strong></span></p>
          </div>
        </div>
      </div>
    </div>
  </body>
</html>"
            );
            var book = CreateBook();

            // This has a total of no audio-sentence spans and eleven audio-sentence divs.
            // check initial conditions
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath("//span[contains(@class,'audio-sentence')]");
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'audio-sentence') and @id!='']",
                    11
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'audio-sentence') and @id='ee2f4638-8892-4a13-9abd-be43417236cc']",
                    3
                ); // bookTitle
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'audio-sentence') and @id='i07d27e74-f55e-4adf-b09e-16c3aae93789']",
                    2
                ); // smallCoverCredits
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'audio-sentence') and @id='i1010a3ba-2abe-4a1b-a945-74b5cfbe0130']",
                    2
                ); // funding
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'audio-sentence') and @id='i98cce2ab-2618-43b2-b9b6-6437eb134501']",
                    2
                ); // outsideBackCover
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'audio-sentence') and @id='i8a56eaf1-f6ff-46c1-b973-c1a82360f40b']",
                    2
                ); // ERROR!

            book.BringBookUpToDate(new NullProgress()); // SUT

            // check final condition
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath("//span[contains(@class,'audio-sentence')]");
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'audio-sentence') and @id!='']",
                    11
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'audio-sentence') and @id='ee2f4638-8892-4a13-9abd-be43417236cc']",
                    3
                ); // bookTitle (same)
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'audio-sentence') and @id='i07d27e74-f55e-4adf-b09e-16c3aae93789']",
                    2
                ); // smallCoverCredits (same)
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'audio-sentence') and @id='i1010a3ba-2abe-4a1b-a945-74b5cfbe0130']",
                    2
                ); // funding (same)
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'audio-sentence') and @id='i98cce2ab-2618-43b2-b9b6-6437eb134501']",
                    2
                ); // outsideBackCover (same)
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'audio-sentence') and @id='i8a56eaf1-f6ff-46c1-b973-c1a82360f40b']",
                    1
                ); // fixed!

            // Check that all the audio ids outside the data-div are unique.
            var audioNodes = book.RawDom
                .SafeSelectNodes(
                    "(//div[contains(@class,'bloom-page')]//div|//div[contains(@class,'bloom-page')]//span)[contains(@class,'audio-sentence') and @id]"
                )
                .Cast<XmlNode>()
                .ToList();
            HashSet<string> uniqueIds = new HashSet<string>();
            foreach (var node in audioNodes)
            {
                var id = node.GetStringAttribute("id");
                uniqueIds.Add(id);
            }
            Assert.That(audioNodes.Count, Is.EqualTo(uniqueIds.Count + 1)); // NB: bookTitle occurs twice in xMatter pages
        }

        [Test]
        public void BringBookUpToDate_FixesDuplicateAudioIds_ManyDuplicates()
        {
            _bookDom = new HtmlDom(
                @"<html>
  <head>
    <meta content='text/html; charset=utf-8' http-equiv='content-type' />
  </head>
  <body>
    <div class='bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual' data-page='' data-page-number='1' data-pagelineage='adcd48df-e9ab-4a07-afd4-6a24d0398382' id='8065f807-769f-4e50-9a45-f329ce05dd32' lang=''>
      <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto'>
        <div aria-label='false' class='bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' data-hasqtip='true' role='textbox' spellcheck='true' style='min-height: 60px;' tabindex='0' data-languagetipcontent='English' lang='en' contenteditable='true'>
          <p><span class='audio-sentence' id='i0a52e794-9191-49df-8bb0-49fd2d7403e6'>Test!</span>
             <span class='audio-sentence' id='i2a5c5a6c-8e4c-4557-8f8f-3e4689d10e21'>Something recorded?</span>
             <span class='audio-sentence' id='i2a5c5a6c-8e4c-4557-8f8f-3e4689d10e21'>Something else.</span></p>
        </div>
      </div>
    </div>
    <div class='bloom-page numberedPage customPage A4Portrait side-right bloom-monolingual' data-page='' id='ff4335b6-07d9-4b7a-b2fe-f836c9da4c96' data-pagelineage='5dcd48df-e9ab-4a07-afd4-6a24d0398386' lang='' data-page-number='2'>
      <div class='bloom-translationGroup bloom-trailingElement'>
        <div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable Title-On-Title-Page-style bloom-content1 bloom-contentNational2 bloom-visibility-code-on' contenteditable='true' lang='en'>
          <p><span id='i546e558f-7fc2-4504-a771-dfcce32be0fb' class='audio-sentence' recordingmd5='undefined'>This is test data.</span></p>
          <p><span class='audio-sentence' recordingmd5='undefined'></span>
             <span id='i8b5d14ac-0056-4469-8db4-11d4da8c4c9d' class='audio-sentence' recordingmd5='undefined'>more data</span></p>
        </div>
      </div>
      <div class='bloom-translationGroup bloom-trailingElement'>
        <div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable normal-style bloom-content1 bloom-contentNational2 bloom-visibility-code-on' contenteditable='true' lang='en'>
          <p><span id='i4d78a211-e1cc-483c-8b54-4256967e683f' class='audio-sentence' recordingmd5='undefined'>This is a test.</span>
             <span id='i785e181c-1b45-4404-82a6-fd9104dcd768' class='audio-sentence' recordingmd5='undefined'>This is still a test.</span>
             <span id='i2adf83a8-c689-4aa6-8603-b2ef8f3d0efe' class='audio-sentence' recordingmd5='undefined'>Testing is hard work!</span>
             <span id='ddcbbf14-445e-4bfa-ad0a-94a9223dff3d' class='audio-sentence' recordingmd5='undefined'>But it has to be done.</span></p>
        </div>
      </div>
    </div>
    <div class='bloom-page numberedPage customPage A4Portrait side-right bloom-monolingual' data-page='' id='c430b078-b953-495e-a19c-db34d0a4789b' data-pagelineage='5dcd48df-e9ab-4a07-afd4-6a24d0398386;ff4335b6-07d9-4b7a-b2fe-f836c9da4c96' lang='' data-page-number='2'>
      <div class='bloom-translationGroup bloom-trailingElement'>
        <div data-languagetipcontent='English' data-hasqtip='true' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable Title-On-Title-Page-style bloom-content1 bloom-contentNational2 bloom-visibility-code-on' contenteditable='true' lang='en'>
          <p><strong>This is text.</strong>
             <span id='i8b5d14ac-0056-4469-8db4-11d4da8c4c9d' class='audio-sentence' recordingmd5='undefined'>So is this!</span></p>
        </div>
      </div>
      <div class='bloom-translationGroup bloom-trailingElement'>
        <div data-languagetipcontent='English' data-hasqtip='true' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable normal-style bloom-content1 bloom-contentNational2 bloom-visibility-code-on' contenteditable='true' lang='en'>
          <p><span id='i4d78a211-e1cc-483c-8b54-4256967e683f' class='audio-sentence' recordingmd5='undefined'></span>This is another random sentence.</p>
          <p><span id='ddcbbf14-445e-4bfa-ad0a-94a9223dff3d' class='audio-sentence' recordingmd5='undefined'></span></p>
        </div>
      </div>
    </div>
    <div class='bloom-page numberedPage customPage Device16x9Landscape side-right bloom-monolingual' id='a5fbba26-4852-48ac-a777-3af30c3bdc3f' data-pagelineage='a31c38d8-c1cb-4eb9-951b-d2840f6a8bdb;1fb20cfe-a762-4df8-94ad-ac4bea5b1a96;d9b4e525-aab7-4c86-a62c-2e8eca084e61' lang='' data-page-number='3' data-page=''>
      <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto'>
        <div data-audiorecordingmode='Sentence' data-hasqtip='true' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 20px;' class='bloom-editable Heading1-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' data-languagetipcontent='English' lang='en' contenteditable='true'>
          <p><span id='i390ae479-61d7-4843-9998-2df7650ddeed' class='audio-sentence' recordingmd5='undefined'></span></p>
          <p><span id='i390ae479-61d7-4843-9998-2df7650ddeed' class='audio-sentence' recordingmd5='undefined'><strong>Secretary's Message</strong></span></p>
          <p><span id='c0d82e47-a45c-4546-9406-45ad0ce70eab' class='audio-sentence' recordingmd5='undefined'>collection of writing</span></p>
        </div>
      </div>
    </div>
    <div class='bloom-page numberedPage customPage bloom-combinedPage Device16x9Portrait side-left bloom-monolingual' data-page='' id='de7abe8a-3b05-43ae-a2f1-cd123306e4e4' data-pagelineage='adcd48df-e9ab-4a07-afd4-6a24d0398383;...' data-page-number='8' lang=''>
      <div data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement'>
        <div data-duration='1.549' data-languagetipcontent='English' id='i90ccd522-5d20-4ff7-b9b3-b32a83f2abeb' data-audiorecordingmode='TextBox' data-hasqtip='true' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 130px;' class='bloom-editable BigWords-style audio-sentence bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
          <p>Ii</p>
        </div>
      </div>
    </div>
    <div class='bloom-page numberedPage customPage bloom-combinedPage Device16x9Portrait side-right bloom-monolingual' data-page='' id='23688af4-8649-4ae3-8c05-6cdc5a3c83d8' data-pagelineage='adcd48df-e9ab-4a07-afd4-6a24d0398383;...' data-page-number='9' lang=''>
      <div data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement'>
        <div data-languagetipcontent='English' data-duration='1.872' id='i90ccd522-5d20-4ff7-b9b3-b32a83f2abeb' data-audiorecordingmode='TextBox' data-hasqtip='true' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 130px;' class='bloom-editable BigWords-style audio-sentence bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
          <p>Jj</p>
        </div>
      </div>
    </div>
    <div class='bloom-page numberedPage customPage bloom-combinedPage Device16x9Portrait side-left bloom-monolingual' data-page='' id='b6275eca-7d34-4089-bc0b-bae2a1936ce3' data-pagelineage='adcd48df-e9ab-4a07-afd4-6a24d0398383;...' data-page-number='10' lang=''>
      <div data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement'>
        <div data-languagetipcontent='English' data-duration='1.872' id='i90ccd522-5d20-4ff7-b9b3-b32a83f2abeb' data-audiorecordingmode='TextBox' data-hasqtip='true' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 130px;' class='bloom-editable BigWords-style audio-sentence bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
          <p>Kk</p>
        </div>
      </div>
    </div>
    <div class='bloom-page numberedPage customPage bloom-combinedPage Device16x9Portrait side-right bloom-monolingual' data-page='' id='9185a9e9-aa3b-4510-8aa4-145e40fdcf04' data-pagelineage='adcd48df-e9ab-4a07-afd4-6a24d0398383;...' data-page-number='11' lang=''>
      <div data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement'>
        <div data-languagetipcontent='English' data-duration='2.136' id='i90ccd522-5d20-4ff7-b9b3-b32a83f2abeb' data-audiorecordingmode='TextBox' data-hasqtip='true' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 130px;' class='bloom-editable BigWords-style audio-sentence bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
          <p>Ll</p>
        </div>
      </div>
    </div>
    <div class='bloom-page numberedPage customPage bloom-combinedPage Device16x9Portrait side-left bloom-monolingual' data-page='' id='bf6260b7-5e06-4172-ad01-5b7f9beac48a' data-pagelineage='adcd48df-e9ab-4a07-afd4-6a24d0398383;...' data-page-number='12' lang=''>
      <div data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement'>
        <div data-duration='2.605' data-languagetipcontent='English' id='i90ccd522-5d20-4ff7-b9b3-b32a83f2abeb' data-audiorecordingmode='TextBox' data-hasqtip='true' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 130px;' class='bloom-editable BigWords-style audio-sentence bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
          <p>Mm</p>
        </div>
      </div>
    </div>
    <div class='bloom-page numberedPage customPage bloom-combinedPage Device16x9Portrait side-right bloom-monolingual' data-page='' id='4bb50bde-63f9-4770-be83-1bdc9d01396e' data-pagelineage='adcd48df-e9ab-4a07-afd4-6a24d0398383;...' data-page-number='13' lang=''>
      <div data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement'>
        <div data-duration='1.885' data-languagetipcontent='English' id='i90ccd522-5d20-4ff7-b9b3-b32a83f2abeb' data-audiorecordingmode='TextBox' data-hasqtip='true' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 130px;' class='bloom-editable BigWords-style audio-sentence bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
          <p>Nn</p>
        </div>
      </div>
    </div>
    <div class='bloom-page numberedPage customPage bloom-combinedPage Device16x9Portrait side-left bloom-monolingual' data-page='' id='a625a19c-17d4-4fc5-b7ba-bd760c0a34c1' data-pagelineage='adcd48df-e9ab-4a07-afd4-6a24d0398383;...' data-page-number='14' lang=''>
      <div data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement'>
        <div data-duration='2.317' data-languagetipcontent='English' id='i90ccd522-5d20-4ff7-b9b3-b32a83f2abeb' data-audiorecordingmode='TextBox' data-hasqtip='true' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 130px;' class='bloom-editable BigWords-style audio-sentence bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
          <p>Oo</p>
        </div>
      </div>
    </div>
    <div class='bloom-page numberedPage customPage bloom-combinedPage Device16x9Portrait side-right bloom-monolingual' data-page='' id='22376a27-1e84-40d1-9f49-086a0957919a' data-pagelineage='adcd48df-e9ab-4a07-afd4-6a24d0398383;...' data-page-number='15' lang=''>
      <div data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement'>
        <div data-duration='1.909' data-languagetipcontent='English' id='i90ccd522-5d20-4ff7-b9b3-b32a83f2abeb' data-audiorecordingmode='TextBox' data-hasqtip='true' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 130px;' class='bloom-editable BigWords-style audio-sentence bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
          <p>Pp</p>
        </div>
      </div>
    </div>
    <div class='bloom-page numberedPage customPage bloom-combinedPage Device16x9Portrait side-left bloom-monolingual' data-page='' id='bb2bb8bc-f92b-452b-9ed3-fe7a3a5632c3' data-pagelineage='adcd48df-e9ab-4a07-afd4-6a24d0398383;...' data-page-number='16' lang=''>
      <div data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement'>
        <div data-duration='1.573' data-languagetipcontent='English' id='i90ccd522-5d20-4ff7-b9b3-b32a83f2abeb' data-audiorecordingmode='TextBox' data-hasqtip='true' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 130px;' class='bloom-editable BigWords-style audio-sentence bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
          <p>Qq</p>
        </div>
      </div>
    </div>
  </body>
</html>"
            );
            var book = CreateBook();
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='en']/p/span[contains(@class,'audio-sentence') and @id!='']",
                    15
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='en']/p/span[contains(@class,'audio-sentence') and not(@id)]",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='i2a5c5a6c-8e4c-4557-8f8f-3e4689d10e21']",
                    2
                ); // ERROR!
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='i8b5d14ac-0056-4469-8db4-11d4da8c4c9d']",
                    2
                ); // ERROR!
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='i4d78a211-e1cc-483c-8b54-4256967e683f']",
                    2
                ); // ERROR!
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='ddcbbf14-445e-4bfa-ad0a-94a9223dff3d']",
                    2
                ); // ERROR!
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='i390ae479-61d7-4843-9998-2df7650ddeed']",
                    2
                ); // ERROR!
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'audio-sentence') and @id!='']",
                    9
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'audio-sentence') and @id='i90ccd522-5d20-4ff7-b9b3-b32a83f2abeb']",
                    9
                ); // ERROR!

            book.BringBookUpToDate(new NullProgress()); // SUT

            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='en']/p/span[contains(@class,'audio-sentence') and @id!='']",
                    16
                ); // one missing id added
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath(
                    "//div[@lang='en']/p/span[contains(@class,'audio-sentence') and not(@id)]"
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='i2a5c5a6c-8e4c-4557-8f8f-3e4689d10e21']",
                    1
                ); // fixed!
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='i8b5d14ac-0056-4469-8db4-11d4da8c4c9d']",
                    1
                ); // fixed!
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='i4d78a211-e1cc-483c-8b54-4256967e683f']",
                    1
                ); // fixed!
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='ddcbbf14-445e-4bfa-ad0a-94a9223dff3d']",
                    1
                ); // fixed!
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='i390ae479-61d7-4843-9998-2df7650ddeed']",
                    1
                ); // fixed!
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'audio-sentence') and @id!='']",
                    9
                ); // unchanged
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'audio-sentence') and @id='i90ccd522-5d20-4ff7-b9b3-b32a83f2abeb']",
                    1
                ); // fixed!

            // Check that all the audio ids outside the data-div are unique.
            var audioNodes = book.RawDom
                .SafeSelectNodes(
                    "(//div[contains(@class,'bloom-page')]//div|//div[contains(@class,'bloom-page')]//span)[contains(@class,'audio-sentence') and @id]"
                )
                .Cast<XmlNode>()
                .ToList();
            HashSet<string> uniqueIds = new HashSet<string>();
            foreach (var node in audioNodes)
            {
                var id = node.GetStringAttribute("id");
                uniqueIds.Add(id);
            }
            Assert.That(audioNodes.Count, Is.EqualTo(uniqueIds.Count));
        }

        [Test]
        public void BringBookUpToDate_FixesDuplicateAudioIds_DataDivProblems()
        {
            _bookDom = new HtmlDom(
                @"<html>
  <head>
    <meta charset='UTF-8'></meta>
  </head>
  <body>
    <div id='bloomDataDiv'>
      <div data-book='bookTitle' lang='en'>
        <p><span id='i9af373c9-2959-43ee-b42b-93b958432bdb' class='audio-sentence'>A Book</span></p>
      </div>
      <div data-book='originalContributions' lang='en'>
        <p><span id='ef8243d2-f98e-4a96-993d-8152069516e6' class='audio-sentence'>Images by Stephen McConnel, © 2011 Steve McConnel.</span>
        <span id='da7f2387-28f5-4a43-a447-235850d6eaf6' class='audio-sentence'>CC BY-SA 1.0.</span></p>
      </div>
      <div data-book='smallCoverCredits' lang='en'>
        <p><span id='i12cd55e7-8292-4c12-92ed-7616da631d0b' class='audio-sentence'>Stephen McConnel</span></p>
        <p><span id='i12cd55e7-8292-4c12-92ed-7616da631d0b' class='audio-sentence'>Somebody Else</span></p>
      </div>
      <div data-book='funding' lang='en' data-audiorecordingmode='Sentence'>
        <p><span id='i256a477b-1dcb-4492-92fe-b0d82065a03c' class='audio-sentence'>thanks, everyone</span></p>
        <p><span id='i082e977b-adca-4310-8841-1a78b0f44cbd' class='audio-sentence'>nobody paid for this!</span></p>
      </div>
      <div data-book='versionAcknowledgments' lang='en' data-audiorecordingmode='Sentence'>
        <p><span id='i082e977b-adca-4310-8841-1a78b0f44cbd' class='audio-sentence'>nobody paid for this!</span></p>
      </div>
      <div data-book='originalAcknowledgments' lang='en' data-audiorecordingmode='Sentence'>
        <p><span id='i256a477b-1dcb-4492-92fe-b0d82065a03c' class='audio-sentence'>thanks, everyone</span></p>
      </div>
    </div>
    <div class=""bloom-page cover coverColor bloom-frontMatter frontCover outsideFrontCover A5Portrait side-right"" data-page=""required singleton"" data-export=""front-matter-cover"" data-xmatter-page=""frontCover"" id=""0176b5a1-a52a-4efc-b512-ff8c06e6ca79"" data-page-number="""">
      <div class='marginBox'>
        <div class='bloom-translationGroup bookTitle' data-default-languages='V,N1'>
          <div class='bloom-editable Title-On-Cover-style' lang='en' data-book='bookTitle' data-audiorecordingmode='Sentence'>
            <p><span id='i9af373c9-2959-43ee-b42b-93b958432bdb' class='audio-sentence' recordingmd5='876a79b75646878799746fa21c5c7cb0'>A Book</span></p>
          </div>
        </div>
        <div class='creditsRow' data-hint='You may use this space for author/illustrator, or anything else.'>
          <div class='bloom-translationGroup' data-default-languages='V'>
            <div data-audiorecordingmode='Sentence' class='bloom-editable smallCoverCredits' lang='en' data-book='smallCoverCredits'>
              <p><span id='i12cd55e7-8292-4c12-92ed-7616da631d0b' class='audio-sentence'>Stephen McConnel</span></p>
              <p><span id='i12cd55e7-8292-4c12-92ed-7616da631d0b' class='audio-sentence'>Somebody Else</span></p>
            </div>
          </div>
        </div>
      </div>
    </div>
    <div class=""bloom-page titlePage bloom-frontMatter A5Portrait side-right"" data-page=""required singleton"" data-export=""front-matter-title-page"" data-xmatter-page=""titlePage"" id=""6ab095fa-7da3-48f3-9ae4-7a7196b94ba3"" data-page-number="""" lang="""">
      <div class='marginBox'>
        <div class='bloom-translationGroup' data-default-languages='V,N1' id='titlePageTitleBlock'>
          <div data-audiorecordingmode='Sentence' class='bloom-editable Title-On-Title-Page-style' data-book='bookTitle' lang='en'>
            <p><span id='i9af373c9-2959-43ee-b42b-93b958432bdb' class='audio-sentence' recordingmd5='876a79b75646878799746fa21c5c7cb0'>A Book</span></p>
          </div>
        </div>
        <div class='bloom-translationGroup' data-default-languages='N1' id='originalContributions'>
          <div data-audiorecordingmode='Sentence' class='bloom-editable credits' data-book='originalContributions' lang='en' contenteditable='true'>
            <p><span id='ef8243d2-f98e-4a96-993d-8152069516e6' class='audio-sentence' >Images by Stephen McConnel, © 2011 Steve McConnel.</span>
            <span id='da7f2387-28f5-4a43-a447-235850d6eaf6' class='audio-sentence' recordingmd5='10fa17363ea15ca65374451914c74717'>CC BY-SA 1.0.</span></p>
          </div>
	</div>
        <div class='bloom-translationGroup' data-default-languages='N1' id='funding'>
          <div data-audiorecordingmode='Sentence' class='bloom-editable funding' data-book='funding' lang='en'>
            <p><span id='i256a477b-1dcb-4492-92fe-b0d82065a03c' class='audio-sentence' recordingmd5='undefined'>thanks, everyone</span></p>
            <p><span id='i082e977b-adca-4310-8841-1a78b0f44cbd' class='audio-sentence' recordingmd5='undefined'>nobody paid for this!</span></p>
          </div>
        </div>
      </div>
    </div>
    <div class=""bloom-page bloom-frontMatter credits A5Portrait side-left"" data-page=""required singleton"" data-export=""front-matter-credits"" data-xmatter-page=""credits"" id=""9bc79fb7-0c92-44a3-9d57-0d7ccbbaeb45"" data-page-number="""">
      <div class='marginBox'>
        <div class='bloom-translationGroup versionAcknowledgments' data-default-languages='N1'>
          <div data-audiorecordingmode='Sentence' class='bloom-editable versionAcknowledgments' data-book='versionAcknowledgments' lang='en'>
            <p><span id='i082e977b-adca-4310-8841-1a78b0f44cbd' class='audio-sentence'>nobody paid for this!</span></p>
          </div>
        </div>
        <div class='bloom-translationGroup originalAcknowledgments' data-default-languages='N1'>
          <div data-audiorecordingmode='Sentence' class='bloom-editable Credits-Page-style' data-book='originalAcknowledgments' lang='en'>
            <p><span id='i256a477b-1dcb-4492-92fe-b0d82065a03c' class='audio-sentence'>thanks, everyone</span></p>
          </div>
        </div>
      </div>
    </div>
    <div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""1ca77759-6b00-4100-9edd-400b7e77d235"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""1"" lang=""en"">
      <div class=""marginBox"">
        <div class=""bloom-translationGroup bloom-imageDescription bloom-trailingElement"">
          <div data-languagetipcontent=""English"" aria-label=""false"" role=""textbox"" spellcheck=""true"" tabindex=""0"" style=""min-height: 24px;"" class=""bloom-editable ImageDescriptionEdit-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"">
            <p><span id=""i9af373c9-2959-43ee-b42b-93b958432bdb"" class=""audio-sentence"">This Is A Book</span></p>
          </div>
        </div>
      </div>
    </div>
  </body>
</html>
"
            );
            // Set up book to cause automatic duplication of some English data-div strings into other languages.
            var book = CreateBook(
                new CollectionSettings(
                    new NewCollectionSettings
                    {
                        PathToSettingsFile = CollectionSettings.GetPathForNewSettings(
                            _testFolder.Path,
                            "test"
                        ),
                        Language1Tag = "en",
                        Language2Tag = "fr",
                        Language3Tag = "es"
                    }
                )
            );

            // Create a couple of fake audio files to test whether they get copied/renamed.
            BookStorageTests.MakeSampleAudioFiles(
                _tempFolder.Path,
                "i9af373c9-2959-43ee-b42b-93b958432bdb",
                ".mp3"
            );
            BookStorageTests.MakeSampleAudioFiles(
                _tempFolder.Path,
                "i256a477b-1dcb-4492-92fe-b0d82065a03c",
                ".mp3"
            );
            Assert.That(
                Directory.EnumerateFiles(Path.Combine(_tempFolder.Path, "audio")).Count,
                Is.EqualTo(2)
            ); // 2 files created

            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='en']/p/span[contains(@class,'audio-sentence') and @id!='']",
                    20
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath( // funding/en AND originalAcknowledgements/en
                    "//span[contains(@class,'audio-sentence') and @id='i256a477b-1dcb-4492-92fe-b0d82065a03c']",
                    4
                ); // ERROR!
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath( // funding/en AND versionAcknowledgements/en
                    "//span[contains(@class,'audio-sentence') and @id='i082e977b-adca-4310-8841-1a78b0f44cbd']",
                    4
                ); // ERROR!
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='ef8243d2-f98e-4a96-993d-8152069516e6']",
                    2
                ); // originalContributions okay
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='i12cd55e7-8292-4c12-92ed-7616da631d0b']",
                    4
                ); // smallCoverCredits internal duplicate ERROR!
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath( // three in data-book="book-title", one not
                    "//span[contains(@class,'audio-sentence') and @id='i9af373c9-2959-43ee-b42b-93b958432bdb']",
                    4
                ); // bookTitle ERROR!

            book.BringBookUpToDate(new NullProgress()); // SUT

            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='en']/p/span[contains(@class,'audio-sentence') and @id!='']",
                    20
                ); // unchanged
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='i256a477b-1dcb-4492-92fe-b0d82065a03c']",
                    2
                ); // funding/en fixed
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='i082e977b-adca-4310-8841-1a78b0f44cbd']",
                    2
                ); // funding/en fixed
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='ef8243d2-f98e-4a96-993d-8152069516e6']",
                    2
                ); // originalContributions still okay
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='i12cd55e7-8292-4c12-92ed-7616da631d0b']",
                    2
                ); // smallCoverCredits okay
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//span[contains(@class,'audio-sentence') and @id='i9af373c9-2959-43ee-b42b-93b958432bdb']",
                    3
                ); // bookTitle fixed

            // Check that files have been copied/renamed.
            Assert.That(
                Directory.EnumerateFiles(Path.Combine(_tempFolder.Path, "audio")).Count,
                Is.EqualTo(4)
            ); // 2 more files created

            // Check that all the audio ids outside the data-div are unique (except bookTitle is used twice).
            var audioNodes = book.RawDom
                .SafeSelectNodes(
                    "(//div[contains(@class,'bloom-page')]//div|//div[contains(@class,'bloom-page')]//span)[contains(@class,'audio-sentence') and @id]"
                )
                .Cast<XmlNode>()
                .ToList();
            var uniqueIds = new HashSet<string>();
            foreach (var node in audioNodes)
            {
                var id = node.GetStringAttribute("id");
                uniqueIds.Add(id);
            }
            Assert.That(audioNodes.Count, Is.EqualTo(uniqueIds.Count + 1)); // bookTitle shared by two xMatter pages

            // Check that all the audio ids inside the data-div are unique.
            audioNodes = book.RawDom
                .SafeSelectNodes(
                    "(//div[@id='bloomDataDiv']//div|//div[@id='bloomDataDiv']//span)[contains(@class,'audio-sentence') and @id]"
                )
                .Cast<XmlNode>()
                .ToList();
            uniqueIds.Clear();
            foreach (var node in audioNodes)
            {
                var id = node.GetStringAttribute("id");
                uniqueIds.Add(id);
            }
            Assert.That(audioNodes.Count, Is.EqualTo(uniqueIds.Count));
        }

        [Test]
        public void BringBookUpToDate_FromHarvester_RemovesFontFacesFromDefaultLangStyles()
        {
            var book = CreateBook();

            RobustFile.WriteAllText(
                Path.Combine(book.FolderPath, "defaultLangStyles.css"),
                @"@font-face { font-family: ABeeZee; src: url(./host/fonts/ABeeZee-Regular.woff2); }
@font-face { font-family: ABeeZee; font-style: italic; src: url(./host/fonts/ABeeZee-Italic.woff2); }
@font-face { font-family: Andika; src: url(./host/fonts/Andika-Regular.woff2); }
@font-face { font-family: Andika; font-weight: bold; src: url(./host/fonts/Andika-Bold.woff2); }
@font-face { font-family: Andika; font-style: italic; src: url(./host/fonts/Andika-Italic.woff2); }
@font-face { font-family: Andika; font-weight: bold; font-style: italic; src: url(./host/fonts/Andika-BoldItalic.woff2); }
@font-face { font-family: ""Andika New Basic""; font-weight: normal; font-style: normal; src: url(""./host/fonts/Andika New Basic""); }
@font-face { font-family: ""Andika New Basic""; font-weight: bold; font-style: normal; src: url(""./host/fonts/Andika New Basic Bold""); }
@font-face { font-family: ""Andika New Basic""; font-weight: normal; font-style: italic; src: url(""./host/fonts/Andika New Basic Italic""); }
@font-face { font-family: ""Andika New Basic""; font-weight: bold; font-style: italic; src: url(""./host/fonts/Andika New Basic Bold Italic"") ; }
/* *** DO NOT EDIT! ***   These styles are controlled by the Settings dialog box in Bloom. */
/* They may be over-ridden by rules in customCollectionStyles.css or customBookStyles.css */

.numberedPage::after
{
 font-family: 'ABeeZee';
 direction: ltr;
}

[lang='mcr']
{
 font-family: 'ABeeZee';
 direction: ltr;
}
"
            );

            Assert.That(
                RobustFile.Exists(Path.Combine(book.FolderPath, "defaultLangStyles.css")),
                Is.True
            );

            Program.RunningHarvesterMode = true;
            book.WriteFontFaces = false;

            book.BringBookUpToDate(new NullProgress()); // SUT

            var newContents = RobustFile.ReadAllText(
                Path.Combine(book.FolderPath, "defaultLangStyles.css")
            );
            Assert.That(
                newContents,
                Is.EqualTo(
                    @"/* *** DO NOT EDIT! ***   These styles are controlled by the Settings dialog box in Bloom. */
/* They may be over-ridden by rules in customCollectionStyles.css or customBookStyles.css */

.numberedPage::after
{
 font-family: 'ABeeZee';
 direction: ltr;
}

[lang='mcr']
{
 font-family: 'ABeeZee';
 direction: ltr;
}
"
                )
            );

            Program.RunningHarvesterMode = false;
        }

        [Test]
        public void FixBookIdAndLineageIfNeeded_WithPageTemplateSourceBasicBook_SetsMissingLineageToBasicBook()
        {
            _bookDom = new HtmlDom(
                @" <html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<meta name='pageTemplateSource' content='Basic Book' />
					<meta name='bloomBookId' content='MyId' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page'>
					</div>
				</body></html>"
            );

            _metadata.BookLineage = ""; // not sure if these could be left from another test
            _metadata.Id = "";
            var book = CreateBook();

            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//meta[@name='bloomBookLineage' and @content='"
                        + Bloom.Book.Book.kIdOfBasicBook
                        + "']",
                    1
                );
            //Assert.That(_metadata.bloom.bookLineage, Is.EqualTo(Bloom.Book.Book.kIdOfBasicBook));
        }

        [Test]
        public void FixBookIdAndLineageIfNeeded_WithPageTemplateSourceBasicBook_OnBookThatHasJsonLineage_DoesNotSetLineage()
        {
            _bookDom = new HtmlDom(
                @"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<meta name='pageTemplateSource' content='Basic Book' />
					<meta name='bloomBookId' content='MyId' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page'>
					</div>
				</body></html>"
            );

            _metadata.BookLineage = "something current";
            _metadata.Id = "";
            var book = CreateBook();

            // 0 because it should NOT make the change.
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//meta[@name='bloomBookLineage' and @content='"
                        + Bloom.Book.Book.kIdOfBasicBook
                        + "']",
                    0
                );
            Assert.That(_metadata.BookLineage, Is.EqualTo("something current"));
        }

        [Test]
        public void Save_UpdatesMetadataTitle()
        {
            _bookDom = new HtmlDom(
                @"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
				   <title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page'>
						<div class='bloom-page' id='guid3'>
							<textarea lang='en' data-book='bookTitle'>original</textarea>
						</div>
					</div>
				</body></html>"
            );

            var book = CreateBook();

            var titleElt = _bookDom.SelectSingleNode("//textarea");
            titleElt.InnerText = "changed & <mangled>";
            book.Save();
            Assert.That(_metadata.Title, Is.EqualTo("changed & <mangled>"));
        }

        [Test]
        public void Save_UpdatesBookInfoMetadataTags()
        {
            _bookDom = new HtmlDom(
                @"<html><body>
					<div class='bloom-page' id='guid3'>
						<div lang='en' data-derived='topic'>original</div>
					</div>
				</body></html>"
            );

            var book = CreateBook();
            book.OurHtmlDom.SetBookSetting("topic", "en", "Animal stories");
            book.Save();
            Assert.That(book.BookInfo.TopicsList, Is.EqualTo("Animal stories"));

            book.OurHtmlDom.SetBookSetting("topic", "en", "Science");
            book.Save();
            Assert.That(book.BookInfo.TopicsList, Is.EqualTo("Science"));
        }

        [Test]
        public void Save_UpdatesMetadataCreditsRemovingBreaks()
        {
            _bookDom = new HtmlDom(
                @"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
				   <title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page'>
						<div class='bloom-page' id='guid3'>
							<textarea lang='en' data-book='originalAcknowledgments'>original</textarea>
						</div>
					</div>
				</body></html>"
            );

            var book = CreateBook();

            var acksElt = _bookDom.SelectSingleNode("//textarea");
            acksElt.InnerXml = "changed" + Environment.NewLine + "<br />more changes";
            book.Save();
            Assert.That(
                _metadata.Credits,
                Is.EqualTo("changed" + Environment.NewLine + "more changes")
            );
        }

        [Test]
        public void Save_UpdatesMetadataCreditsRemovingP()
        {
            _bookDom = new HtmlDom(
                @"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
				   <title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page'>
						<div class='bloom-page' id='guid3'>
							<textarea lang='en' data-book='originalAcknowledgments'><p>original</p></textarea>
						</div>
					</div>
				</body></html>"
            );

            var book = CreateBook();

            var acksElt = _bookDom.SelectSingleNode("//textarea");
#if __MonoCS__	// may not be needed for Mono 4.x
            acksElt.OwnerDocument.PreserveWhitespace = true; // Does not preserve newlines on Linux without this
#endif
            acksElt.InnerXml = "<p>changed</p>" + Environment.NewLine + "<p>more changes</p>";
            book.Save();
            Assert.That(
                _metadata.Credits,
                Is.EqualTo("changed" + Environment.NewLine + "more changes")
            );
        }

        [Test]
        public void Save_UpdatesMetadataIsbnAndPageCount()
        {
            _bookDom = new HtmlDom(
                @"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
				   <title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page' id='guid3'>
						<textarea lang='en' data-book='ISBN'>original</textarea>
					</div>
				</body></html>"
            );

            var book = CreateBook();

            var isbnElt = _bookDom.SelectSingleNode("//textarea");
            isbnElt.InnerText = "978-0-306-40615-7";
            book.Save();
            Assert.That(book.BookInfo.Isbn, Is.EqualTo("978-0-306-40615-7"));

            var dom = book.GetEditableHtmlDomForPage(book.GetPages().First());
            isbnElt = dom.SelectSingleNode("//textarea");
            isbnElt.InnerText = " ";
            book.SavePage(dom);
            book.Save();
            Assert.That(_metadata.Isbn, Is.EqualTo(""));
        }

        public void Save_UpdatesAllTitles()
        {
            _bookDom = new HtmlDom(
                @"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page'>
						<div class='bloom-page' id='guid2'>
							<textarea lang='en' data-book='bookTitle'>my nice title</textarea>
							<textarea lang='de' data-book='bookTitle'>Mein schönen Titel</textarea>
							<textarea lang='es' data-book='bookTitle'>мy buen título</textarea>
						</div>
					</div>
				</body></html>".Replace("nice title", "\"nice\" title\\topic")
            );

            var book = CreateBook();

            book.Save();

            // Enhance: the order is not critical.
            Assert.That(
                _metadata.AllTitles,
                Is.EqualTo(
                    "{\"de\":\"Mein schönen Titel\",\"en\":\"my \\\"nice\\\" title\\\\topic\",\"es\":\"мy buen título\"}"
                )
            );
        }

        [Test]
        [TestCase(false)]
        [TestCase(true)]
        public void AllPublishableLanguages_FindsBloomEditableElements(
            bool includeLangsOccurringOnlyInXmatter
        )
        {
            _bookDom = new HtmlDom(
                @"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page bloom-frontMatter'>
					   <div class='bloom-translationGroup bloom-trailingElement'>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='tr'>
								Some Thai in front matter. Should not count at all, except for testcase true.
							</div>
						</div>
					</div>
					<div class='bloom-page' id='guid3'>
					   <div class='bloom-translationGroup bloom-trailingElement'>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='de'>
								Bloom ist ein Programm zum Erstellen von Sammlungen der Bucher. Es ist eine Hilfe zur Alphabetisierung.
							</div>

							<div class='bloom-editable' contenteditable='true' lang='en'>
								Bloom is a program for creating collections of books. It is an aid to literacy.
							</div>
							<div class='bloom-editable' contenteditable='true' lang='es'>
							</div>
							<div class='bloom-editable' contenteditable='true' lang='tr'></div>
						</div>
					</div>
					<div class='bloom-page' id='guid3'>
					   <div class='bloom-translationGroup bloom-trailingElement'>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='de'>
								Some German.
							</div>
							<div class='bloom-editable' contenteditable='true' lang='en'>
								Some English.
							</div>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='es'>
								Something or other.
							</div>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='xyz'>
								Something or other.
							</div>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='*'>
								This is not in any known language
							</div>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='z'>
								We use z for some special purpose, seems to occur in every book, don't want it.
							</div>
							<div class='bloom-editable' contenteditable='true' lang='tr'>
							</div>
						</div>
					</div>
					<div class='bloom-page bloom-backMatter'>
					   <div class='bloom-translationGroup bloom-trailingElement'>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='tr'>
								Some Thai in back matter. Should not count at all, except for testcase true.
							</div>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='en'>
								Some English in back matter.
							</div>
							<div class='bloom-editable Inside-Back-Cover-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='lbl' contenteditable='true' data-book='insideBackCover'>
								<label class='bubble'>If you need somewhere to put more information about the book, you can use this page, which is the inside of the back cover.</label>
							</div>
						</div>
					</div>
				</body></html>"
            );

            var book = CreateBook();
            var allLanguages = book.AllPublishableLanguages(includeLangsOccurringOnlyInXmatter);
            // In the case where 'includeLangsOccurringOnlyInXmatter' is true, thai will be included in the list,
            // since there is no thai text on any non-xmatter pages. The boolean value will be true for all languages
            // that have text on each non-xmatter page (English/German) and false for all the others.
            Assert.That(allLanguages["en"], Is.True);
            Assert.That(allLanguages["de"], Is.True);
            Assert.That(allLanguages["es"], Is.False); // in first group this is empty
            Assert.That(allLanguages["xyz"], Is.False); // not in first group at all
            if (includeLangsOccurringOnlyInXmatter)
            {
                Assert.That(allLanguages["tr"], Is.False); // only exists in xmatter (therefore 'incomplete')
                Assert.That(allLanguages.Count, Is.EqualTo(5)); // no * or z
            }
            else
            {
                bool dummy;
                Assert.That(allLanguages.TryGetValue("tr", out dummy), Is.False); // only exists in xmatter
                Assert.That(allLanguages.Count, Is.EqualTo(4)); // no * or z or thai
            }
        }

        // We've relaxed constraining comical books to one language.  See https://issues.bloomlibrary.org/youtrack/issue/BL-10275.
        [Test]
        public void AllPublishableLanguages_NotAffectedByComical()
        {
            _bookDom = new HtmlDom(
                @"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page' id='guid3'>
						<div class='bloom-imageContainer'>
							<svg id='comicalItem' class='comical-generated' />
						</div>
						<div class='bloom-translationGroup bloom-trailingElement'>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='de'>
								Some German.
							</div>
							<div class='bloom-editable' contenteditable='true' lang='en'>
								Some English.
							</div>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='xyz'>
								Something or other.
							</div>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='*'>
								This is not in any known language
							</div>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='z'>
								We use z for some special purpose, seems to occur in every book, don't want it.
							</div>
							<div class='bloom-editable' contenteditable='true' lang='tr'>
								Some Thai.
							</div>
						</div>
					</div>
				</body></html>"
            );

            var book = CreateBook();
            var allLanguages = book.AllPublishableLanguages(true);
            Assert.That(allLanguages["en"], Is.True);
            Assert.That(allLanguages["de"], Is.True);
            Assert.That(allLanguages["xyz"], Is.True);
            Assert.That(allLanguages["tr"], Is.True);
            Assert.That(allLanguages, Has.Count.EqualTo(4));

            var comicalItem = book.OurHtmlDom.RawDom.SelectSingleNode("//svg[@id='comicalItem']"); // GetElementById("comicalItem");
            comicalItem.ParentNode.RemoveChild(comicalItem);

            allLanguages = book.AllPublishableLanguages(true);
            // Now we should get the same list without comical
            Assert.That(allLanguages["en"], Is.True);
            Assert.That(allLanguages["de"], Is.True);
            Assert.That(allLanguages["xyz"], Is.True);
            Assert.That(allLanguages["tr"], Is.True);
            Assert.That(allLanguages, Has.Count.EqualTo(4));
        }

        [Test]
        public void AllPublishableLanguages_DoesNotFindGeneratedContent()
        {
            _bookDom = new HtmlDom(
                @"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
				   <title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page' id='guid1'>
					   <div class='bloom-translationGroup bloom-trailingElement bloom-ignoreChildrenForBookLanguageList'>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='de'>
								Bloom ist ein Programm zum Erstellen von Sammlungen der Bucher. Es ist eine Hilfe zur Alphabetisierung.
							</div>

							<div class='bloom-editable' contenteditable='true' lang='en'>
								Bloom is a program for creating collections of books. It is an aid to literacy.
							</div>
						</div>
					</div>
					<div class='bloom-page' id='guid2'>
					   <div class='bloom-translationGroup bloom-trailingElement'>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='xyz'>
								AAA text
							</div>

							<div class='bloom-editable' contenteditable='true' lang='bbb'>
								BBB text
							</div>
						</div>
					</div>
				</body></html>"
            );

            var book = CreateBook();
            var allLanguages = book.AllPublishableLanguages(
                includeLangsOccurringOnlyInXmatter: true
            );
            Assert.That(allLanguages["xyz"], Is.True);
            Assert.That(allLanguages["bbb"], Is.True);
            Assert.That(allLanguages.Count, Is.EqualTo(2));
        }

        [Test]
        public void AllPublishableLanguages_DoesNotIncludeTemplateHintText()
        {
            _bookDom = new HtmlDom(
                @"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page' id='guid1'>
					   <div class='bloom-translationGroup bloom-trailingElement'>
							<label class='bubble' lang='es'>Template Spanish hint text</label>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='de'>
								Bloom ist ein Programm zum Erstellen von Sammlungen der Bucher. Es ist eine Hilfe zur Alphabetisierung.
							</div>

							<div class='bloom-editable' contenteditable='true' lang='en'>
								Bloom is a program for creating collections of books. It is an aid to literacy.
							</div>
						</div>
					</div>
				</body></html>"
            );

            var book = CreateBook();
            var allLanguages = book.AllPublishableLanguages(
                includeLangsOccurringOnlyInXmatter: true
            );
            Assert.That(allLanguages.ContainsKey("xyz"), Is.False); // book L1 does not have to be included if it does not occur (strange, but maybe picture or sign language book)
            Assert.That(allLanguages["de"], Is.True);
            Assert.That(allLanguages["en"], Is.True);
            Assert.That(allLanguages.ContainsKey("es"), Is.False); // the main point: spanish occurs only as a hint
            Assert.That(allLanguages.Count, Is.EqualTo(2));
        }

        [Test]
        public void AllPublishableLanguages_TemplateHintTextDoesNotMakeLanguageComplete()
        {
            _bookDom = new HtmlDom(
                @"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page' id='guid1'>
					   <div class='bloom-translationGroup bloom-trailingElement'>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='de'>
								Bloom ist ein Programm zum Erstellen von Sammlungen der Bucher. Es ist eine Hilfe zur Alphabetisierung.
							</div>

							<div class='bloom-editable' contenteditable='true' lang='en'>
								Bloom is a program for creating collections of books. It is an aid to literacy.
							</div>
						</div>
					   <div class='bloom-translationGroup bloom-trailingElement'>
							<label class='bubble' lang='en'>Template English hint text</label>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='de'>
								Bloom ist ein Programm zum Erstellen von Sammlungen der Bucher. Es ist eine Hilfe zur Alphabetisierung.
							</div>

							<div class='bloom-editable' contenteditable='true' lang='en'>
							</div>
						</div>
					</div>
				</body></html>"
            );

            var book = CreateBook();
            var allLanguages = book.AllPublishableLanguages(
                includeLangsOccurringOnlyInXmatter: true
            );
            Assert.That(allLanguages.ContainsKey("xyz"), Is.False); // no xyz in content (or anywhere); no longer forced in just because it is L1
            Assert.That(allLanguages["de"], Is.True);
            Assert.That(allLanguages["en"], Is.False); // in first tg as content; in second TG only as bubble. So present, but not complete.
            Assert.That(allLanguages.Count, Is.EqualTo(2));
        }

        [Test]
        public void AllPublishableLanguages_OnlyXmatterLangsWhichArePartOfTheCollection()
        {
            _bookDom = new HtmlDom(
                @"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page bloom-backMatter' id='guid1'>
						<div class='bloom-translationGroup bloom-trailingElement'>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='en'>
								English in xmatter
							</div>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='fr'>
								French in xmatter
							</div>
						</div>
					</div>
				</body>
				</html>"
            );

            var collectionSettings = CreateDefaultCollectionsSettings();
            collectionSettings.Language1Tag = "tpi";
            collectionSettings.Language2Tag = "en";
            collectionSettings.Language3Tag = "pt";
            var book = CreateBook(collectionSettings);
            var allLanguages = book.AllPublishableLanguages(
                includeLangsOccurringOnlyInXmatter: true
            );
            Assert.That(allLanguages.ContainsKey("en"), Is.True);
            Assert.That(allLanguages.ContainsKey("fr"), Is.False);
            Assert.That(allLanguages.Count, Is.EqualTo(1));
        }

        [Test]
        public void UpdateLicenseMetdata_UpdatesJson()
        {
            var book = CreateBook();

            // Creative Commons License
            var licenseData = new Metadata();
            licenseData.License = CreativeCommonsLicense.FromLicenseUrl(
                "http://creativecommons.org/licenses/by-sa/3.0/"
            );
            licenseData.License.RightsStatement =
                "Please acknowledge nicely to joe.blow@example.com";

            book.SetMetadata(licenseData);

            Assert.That(_metadata.License, Is.EqualTo("cc-by-sa"));
            Assert.That(
                _metadata.LicenseNotes,
                Is.EqualTo(
                    "Please acknowledge nicely to joe.blow@ex(download book to read full email address)"
                )
            );

            // Custom License
            licenseData.License = new CustomLicense { RightsStatement = "Use it if you dare" };

            book.SetMetadata(licenseData);

            Assert.That(_metadata.License, Is.EqualTo("custom"));
            Assert.That(_metadata.LicenseNotes, Is.EqualTo("Use it if you dare"));

            // Null License (ask the user)
            licenseData.License = new NullLicense { RightsStatement = "Ask me" };

            book.SetMetadata(licenseData);

            Assert.That(_metadata.License, Is.EqualTo("ask"));
            Assert.That(_metadata.LicenseNotes, Is.EqualTo("Ask me"));
        }

        [Test]
        public void FixBookIdAndLineageIfNeeded_FixesBasicBookId()
        {
            _bookDom = new HtmlDom(
                @"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<meta name='bloomBookId' content='"
                    + Bloom.Book.Book.kIdOfBasicBook
                    + @"' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page'>
					</div>
				</body></html>"
            );

            _metadata.Id = "";
            var book = CreateBook();

            // 0 indicates it should NOT match, that is, that it doesn't have the mistaken ID any more.
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//meta[@name='bloomBookId' and @content='"
                        + Bloom.Book.Book.kIdOfBasicBook
                        + "']",
                    0
                );
            // but it should have SOME ID. Hopefully a new one, but that is hard to verify.
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//meta[@name='bloomBookId']", 1);
        }

        [Test]
        public void Constructor_HadNoTitleButDOMHasItInADataItem_TitleElementIsSet()
        {
            SetDom(
                @"<div class='bloom-page' id='guid2'>
						<p>
							<textarea lang='xyz' data-book='bookTitle'>original</textarea>
						</p>
					</div>"
            );
            var book = CreateBook();
            var title = (XmlElement)book.RawDom.SelectSingleNodeHonoringDefaultNS("//title");
            Assert.AreEqual("original", title.InnerText);
        }

        [Test]
        public void Constructor_LanguagesOfBookIsSet()
        {
            var collectionSettings = CreateDefaultCollectionsSettings();
            collectionSettings.Language1Tag = "en";
            var book = CreateBook(collectionSettings);
            var langs =
                book.RawDom.SelectSingleNode(
                    "//div[@id='bloomDataDiv']/div[@data-book='languagesOfBook']"
                ) as XmlElement;
            Assert.AreEqual("English", langs.InnerText);
        }

        [Test]
        public void SavePage_HadTitleChangeEnglishTitle_ChangesTitleElement()
        {
            _bookDom = new HtmlDom(
                @"
				<html><head></head><body>
					<div id='bloomDataDiv'>
						  <div data-book='bookTitle' lang='en'>original</div>
					</div>
					<div class='bloom-page' id='guid1'>
						 <div data-book='bookTitle' lang='en'>original</div>
					</div>
				  </body></html>"
            );

            var book = CreateBook();
            Assert.AreEqual("original", book.Title);

            //simulate editing the page
            var pageDom = new HtmlDom(
                @"
				<html><head></head><body>
					  <div class='bloom-page' id='guid1'>
							<div data-book='bookTitle' lang='en'>newTitle</div>
					   </div>
				  </body></html>"
            );

            book.SavePage(pageDom);
            Assert.AreEqual("newTitle", book.Title);
        }

        [Test]
        public void SavePage_HasTitleTemplate_ChangesTitleElement()
        {
            _bookDom = new HtmlDom(
                @"
				<html><head></head><body>
					<div id='bloomDataDiv'>
						  <div data-book='bookTitle' lang='en'>blaah</div>
						<div data-book='bookTitleTemplate' lang='en'>a {book.flavor} book</div>
					</div>
					<div class='bloom-page' id='guid1'>
						 <div data-book='book.flavor' lang='en'>sweet</div>
					</div>
				  </body></html>"
            );

            var book = CreateBook();
            Assert.AreEqual("a sweet book", book.Title);

            //simulate editing the page
            var pageDom = new HtmlDom(
                @"
				<html><head></head><body>
					  <div class='bloom-page' id='guid1'>
						 <div data-book='book.flavor' lang='en'>sour</div>
					   </div>
				  </body></html>"
            );

            book.SavePage(pageDom);
            Assert.AreEqual("a sour book", book.Title);
        }

        /*
         * TranslationGroupManager.UpdateContentLanguageClasses() sees that we have three active languages and adds
         * bloom-trilingual as a class at the page level.  However, it was not getting added to the stored version
         * of the page.  Thus, we are now checking that SavePage() adds it.
         */
        [Test]
        public void SavePage_MultiLingualClassUpdated()
        {
            _bookDom = new HtmlDom(
                @"
				<html><head></head><body>
					<div id='bloomDataDiv'>
						<div data-book='contentLanguage1' lang='*'>
							xyz
						</div>
						<div data-book='contentLanguage2' lang='*'>
							en
						</div>
						<div data-book='contentLanguage3' lang='*'>
							fr
						</div>
					</div>
					<div class='bloom-page' id='guid1'>
						<div class='bloom-editable bloom-content1' contenteditable='true'></div>
						<div class='bloom-editable bloom-content2' contenteditable='true'></div>
						<div class='bloom-editable bloom-content3' contenteditable='true'></div>
					</div>
				  </body></html>"
            );

            var book = CreateBook();

            // Initially, bloom-trilingual isn't there
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]", 1);
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-page') and contains(@class,'bloom-trilingual')]",
                    0
                );

            var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[0]);

            // bloom-trilingual was added to the temp version of the page
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]", 1);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-page') and contains(@class,'bloom-trilingual')]",
                    1
                );

            book.SavePage(dom);

            // bloom-trilingual was also added to the stored version of the page
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]", 1);
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-page') and contains(@class,'bloom-trilingual')]",
                    1
                );
        }

        [Test]
        public void RepairBrokenSmallCoverCredits_Works()
        {
            _bookDom = new HtmlDom(
                @"
				<html><head></head><body>
					<div id='bloomDataDiv'>
						<div data-book='contentLanguage1' lang='*'>
							xyz
						</div>
						<div data-book='contentLanguage2' lang='*'>
							en
						</div>
						<div data-book='contentLanguage3' lang='*'>
							fr
						</div>
						<div data-book='smallCoverCredits' lang='*'>
							<div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
								<p>Dr. Stephen McConnel, Ph.D.</p>
							</div>
							<div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable bloom-contentNational2' contenteditable='true' lang='mix' />
							<div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable bloom-contentNational1' contenteditable='true' lang='es'>
								<p />
							</div>
							<div class='bloom-editable' contenteditable='true' lang='z' />
							<div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='fr'>
								<p>M. Stephen McConnel</p>
							</div>
						</div>
						<div data-book='smallCoverCredits' lang='fr'>
							<p>Stephen McConnel</p>
						</div>
					</div>
					<div class='bloom-page' id='guid1'>
						<div class='bloom-editable bloom-content1' contenteditable='true'></div>
						<div class='bloom-editable bloom-content2' contenteditable='true'></div>
						<div class='bloom-editable bloom-content3' contenteditable='true'></div>
					</div>
				  </body></html>"
            );
            var book = CreateBook();
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits']",
                    2
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='*']",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='en']"
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='mix']"
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='es']"
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='fr']",
                    1
                );
            Bloom.Book.Book.RepairBrokenSmallCoverCredits(book.OurHtmlDom);
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits']",
                    2
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='*']"
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='en']",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='mix']"
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='es']"
                );
            // Now test code that probably never will be exercised in the wild.
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='fr']",
                    1
                );
            var div = book.RawDom.SelectSingleNode(
                "//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='fr']"
            );
            Assert.AreEqual("Stephen McConnel", div.InnerText.Trim());
        }

        [Test]
        public void RepairPageLabelLocalization_Works()
        {
            _bookDom = new HtmlDom(
                @"
			<html><head></head>
				<body>
					<!-- bare pageLabel, xmatter, no l18n attribute -->
					<div class='bloom-page frontCover bloom-frontMatter' id='guid1'>
						<div lang='en' class='pageLabel'>
							Front Cover
						</div>
						<div class='bloom-editable bloom-content1' contenteditable='true'></div>
					</div>
					<!-- old l18n attribute in pageLabel, needs to be replaced -->
					<div class='bloom-page' id='guid2'>
						<div class='pageLabel' data-i18n='EditTab.ThumbnailCaptions.Custom' lang='en'>
							Custom
						</div>
						<div class='bloom-editable bloom-content1' contenteditable='true'></div>
					</div>
					<div class='bloom-page' id='guid3'>
						<!-- proper pageLabel, preserve it -->
						<div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Basic Text &amp; Picture' lang='en'>
							Possibly already translated text
						</div>
						<div class='bloom-editable bloom-content1' contenteditable='true'></div>
					</div>
					<div class='bloom-page outsideBackCover bloom-backMatter' id='guid4'>
						<!-- no l18n attribute on back cover xmatter pageLabel -->
						<div lang='en' class='pageLabel'>
							Outside Back Cover
						</div>
						<div class='bloom-editable bloom-content1' contenteditable='true'></div>
					</div>
				</body>
			</html>"
            );
            var book = CreateBook();
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid1']/div[@class='pageLabel' and @lang='en']",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath("//div[@id='guid1']/div[@data-i18n]");
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid1']/div[@class='pageLabel' and contains(text(),'Front Cover')]",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid2']/div[@class='pageLabel' and @lang='en']",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid2']/div[@data-i18n='EditTab.ThumbnailCaptions.Custom']",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid2']/div[@class='pageLabel' and contains(text(),'Custom')]",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid3']/div[@class='pageLabel' and @lang='en']",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid3']/div[@data-i18n='TemplateBooks.PageLabel.Basic Text & Picture']",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid3']/div[@class='pageLabel' and contains(text(),'Possibly already translated text')]",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid4']/div[@class='pageLabel' and @lang='en']",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath("//div[@id='guid4']/div[@data-i18n]");
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid4']/div[@class='pageLabel' and contains(text(),'Outside Back Cover')]",
                    1
                );
            Bloom.Book.Book.RepairPageLabelLocalization(book.OurHtmlDom);
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid1']/div[@class='pageLabel' and @lang='en']",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid1']/div[@data-i18n='TemplateBooks.PageLabel.Front Cover']",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid1']/div[@class='pageLabel' and contains(text(),'Front Cover')]",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid2']/div[@class='pageLabel' and @lang='en']",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid2']/div[@data-i18n='TemplateBooks.PageLabel.Custom']",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid2']/div[@class='pageLabel' and contains(text(),'Custom')]",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid3']/div[@class='pageLabel' and @lang='en']",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid3']/div[@data-i18n='TemplateBooks.PageLabel.Basic Text & Picture']",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid3']/div[@class='pageLabel' and contains(text(),'Possibly already translated text')]",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid4']/div[@class='pageLabel' and @lang='en']",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid4']/div[@data-i18n='TemplateBooks.PageLabel.Outside Back Cover']",
                    1
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='guid4']/div[@class='pageLabel' and contains(text(),'Outside Back Cover')]",
                    1
                );
        }

        [Test]
        public void RemoveBlankPages_BlankPageRemoved()
        {
            // This page should be deleted, despite containing a visible div (but with only whitespace content),
            // a div with text (but hidden, since it doesn't have bloom-visibility-code-on),
            // and two kinds of image (but both pointing at placeholders).
            _bookDom = new HtmlDom(
                @"
				<html><head></head><body>
					<div class='bloom-page' id='guid1'>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>
						</div>
						<div class='bloom-editable bloom-content1' contenteditable='true'>This is hidden.</div>
						<div class='bloom-imageContainer'>
							<img src='placeHolder.png'></img>
						</div>
						<div class='bloom-imageContainer bloom-backgroundImage' style="
                    + "\"background-image:url('placeHolder.png')\""
                    + @" ></div>
					</div>
				  </body></html>"
            );
            var book = CreateBook();
            book.RemoveBlankPages();
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath("//div[contains(@class,'bloom-page')]");
        }

        [Test]
        public void RemoveBlankPages_ActivityPagesKept()
        {
            _bookDom = new HtmlDom(
                @"
				<html><head></head><body>
					<div class='bloom-page bloom-interactive-page' id='guid1'>
						<div class='bloom-editable bloom-content1' contenteditable='true'></div>
					</div>
					<div class='bloom-page' id='guid2'>
						<div class='bloom-editable bloom-content1' contenteditable='true'></div>
					</div>
					<div class='bloom-page' id='guid3' data-activity='true'>
						<div class='bloom-editable bloom-content1' contenteditable='true'></div>
					</div>
				</body></html>"
            );
            var book = CreateBook();
            book.RemoveBlankPages();
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]", 2);
        }

        [Test]
        public void RemoveBlankPages_ImagePagesKept()
        {
            _bookDom = new HtmlDom(
                @"
				<html><head></head><body>
					<div class='bloom-page' id='guid1'>
						<div class='bloom-editable bloom-content1' contenteditable='true'></div>
						<img src='somePicture.png'></img>
					</div>
					<div class='bloom-page' id='guid2'>
						<div class='bloom-editable bloom-content1' contenteditable='true'></div>
						<div class='bloom-imageContainer bloom-backgroundImage' style="
                    + "\"background-image:url('someImage.png')\""
                    + @" ></div>
					</div>
				</body></html>"
            );
            var book = CreateBook();
            book.RemoveBlankPages();
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]", 2);
        }

        [Test]
        public void RemoveBlankPages_TextPagesKept()
        {
            _bookDom = new HtmlDom(
                @"
				<html><head></head><body>
					<div class='bloom-page' id='guid1'>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>some real text!</div>
					</div>
					<div class='bloom-page' id='guid2'>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>This is visible!</div>
					</div>
				</body></html>"
            );
            var book = CreateBook();
            book.RemoveBlankPages();
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]", 2);
        }

        [Test]
        public void RemoveBlankPages_RenumbersPages()
        {
            _bookDom = new HtmlDom(
                @"
				<html><head></head><body>
					<div class='bloom-page numberedPage' id='guid1' data-page-number='1'>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>some real text!</div>
					</div>
					<div class='bloom-page numberedPage' id='guid2' data-page-number='2'>
					</div>
					<div class='bloom-page numberedPage' id='guid2' data-page-number='3'>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>This is visible!</div>
					</div>
				</body></html>"
            );
            var book = CreateBook();
            book.RemoveBlankPages();
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]", 2);
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-page') and @data-page-number='2']",
                    1
                );
        }

        [TestCase("span")]
        [TestCase("div")]
        public void SetAnimationDurationsFromAudioDurations_SetsExpectedDuration(string elementName)
        {
            // page 2 is left with no image to test that it doesn't choke on that.
            // page 4 has an image with no animation; a data-duration should not be set.
            _bookDom = new HtmlDom(
                $@"
				<html><head></head><body>
					<div class='bloom-page numberedPage' id='guid1' data-page-number='1'>
						<div class='bloom-imageContainer some other classes' data-initialrect='0.0,0.0,0.5,0.5' data-finalrect='0.5,0.5,0.5,0.5'></div>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'><{elementName} class='audio-sentence' data-duration='1.200'>some real text!</{elementName}> <{elementName} class='audio-sentence' data-duration='0.400'>another sentence!</{elementName}></div>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'><{elementName} class='audio-sentence' data-duration='1.500'>some more text!</{elementName}></div>
						<div class='bloom-editable bloom-visibility-code-on' contenteditable='true'><{elementName} class='audio-sentence' data-duration='1.800'>text not in content1, duration does not count</{elementName}></div>
					</div>
					<div class='bloom-page numberedPage' id='guid2' data-page-number='2'>
					</div>
					<div class='bloom-page numberedPage' id='guid3' data-page-number='3'>
						<div class='bloom-imageContainer some other classes' data-initialrect='0.0,0.0,0.5,0.5' data-finalrect='0.5,0.5,0.5,0.5'></div>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>This is visible!</div>
					</div>
					<div class='bloom-page numberedPage' id='guid4' data-page-number='4'>
						<div class='bloom-imageContainer some other classes'></div>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>This is visible!</div>
					</div>
				</body></html>"
            );
            var book = CreateBook();
            book.SetAnimationDurationsFromAudioDurations();

            Assert.AreEqual(
                3.1,
                double.Parse(
                    book.RawDom
                        .SelectSingleNode(
                            "//div[@id='guid1']/div[contains(@class,'bloom-imageContainer')]"
                        )
                        .Attributes["data-duration"].Value,
                    CultureInfo.InvariantCulture
                ),
                0.001,
                "Duration 1"
            );
            Assert.AreEqual(
                4,
                double.Parse(
                    book.RawDom
                        .SelectSingleNode(
                            "//div[@id='guid3']/div[contains(@class,'bloom-imageContainer')]"
                        )
                        .Attributes["data-duration"].Value,
                    CultureInfo.InvariantCulture
                ),
                0,
                "Duration 3"
            );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath(
                    @"//div[@id='guid4']/div[contains(@class,'bloom-imageContainer') and @data-duration]"
                );
        }

        [Test]
        public void SetAnimationDurationsFromAudioDurations_AudioSentenceDiv_SetsExpectedDuration()
        {
            // page 2 is left with no image to test that it doesn't choke on that.
            // page 4 has an image with no animation; a data-duration should not be set.
            _bookDom = new HtmlDom(
                $@"
				<html><head></head><body>
					<div class='bloom-page numberedPage' id='guid1' data-page-number='1'>
						<div class='bloom-imageContainer some other classes' data-initialrect='0.0,0.0,0.5,0.5' data-finalrect='0.5,0.5,0.5,0.5'></div>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on audio-sentence' contenteditable='true' id='1' data-duration='1.600'>some real text! another sentence!</div>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on audio-sentence' contenteditable='true' id='2' data-duration='1.500'>some more text!</div>
						<div class='bloom-editable bloom-visibility-code-on audio-sentence' contenteditable='true' data-duration='1.800'>text not in content1, duration does not count</div>
					</div>
					<div class='bloom-page numberedPage' id='guid2' data-page-number='2'>
					</div>
					<div class='bloom-page numberedPage' id='guid3' data-page-number='3'>
						<div class='bloom-imageContainer some other classes' data-initialrect='0.0,0.0,0.5,0.5' data-finalrect='0.5,0.5,0.5,0.5'></div>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>This is visible!</div>
					</div>
					<div class='bloom-page numberedPage' id='guid4' data-page-number='4'>
						<div class='bloom-imageContainer some other classes'></div>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>This is visible!</div>
					</div>
					<div class='bloom-page numberedPage' id='guid5' data-page-number='1'>
						<div class='bloom-imageContainer some other classes' data-initialrect='0.0,0.0,0.5,0.5' data-finalrect='0.5,0.5,0.5,0.5'></div>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'><span class='audio-sentence' data-duration='1.200'>some real text!</span> <span class='audio-sentence' data-duration='0.400'>another sentence!</span></div>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on audio-sentence' contenteditable='true' id='2' data-duration='1.500'>some more text!</div>
						<div class='bloom-editable bloom-visibility-code-on audio-sentence' contenteditable='true' data-duration='1.800'>text not in content1, duration does not count</div>
					</div>
				</body></html>"
            );
            var book = CreateBook();
            book.SetAnimationDurationsFromAudioDurations();
            // Note: these tests are rather too picky about the formatting of the output floats. We'd be quite happy if the result
            // was 3.10000 and 4.0. If this proves problematic we can make the test smarter.
            Assert.AreEqual(
                3.1,
                double.Parse(
                    book.RawDom
                        .SelectSingleNode(
                            "//div[@id='guid1']/div[contains(@class,'bloom-imageContainer')]"
                        )
                        .Attributes["data-duration"].Value,
                    CultureInfo.InvariantCulture
                ),
                0.001,
                "Duration 1"
            );
            Assert.AreEqual(
                4,
                double.Parse(
                    book.RawDom
                        .SelectSingleNode(
                            "//div[@id='guid3']/div[contains(@class,'bloom-imageContainer')]"
                        )
                        .Attributes["data-duration"].Value,
                    CultureInfo.InvariantCulture
                ),
                0,
                "Duration 3"
            );
            Assert.AreEqual(
                3.1,
                double.Parse(
                    book.RawDom
                        .SelectSingleNode(
                            "//div[@id='guid5']/div[contains(@class,'bloom-imageContainer')]"
                        )
                        .Attributes["data-duration"].Value,
                    CultureInfo.InvariantCulture
                ),
                0.001,
                "Duration 1"
            );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath(
                    @"//div[@id='guid4']/div[contains(@class,'bloom-imageContainer') and @data-duration]"
                );
        }

        private void SetupUpdateMetadataFeaturesTest()
        {
            string audioPrefix = "audio";
            string videoFilename = "video1.mp4";

            SetDom(
                $@"
<div class='bloom-page'>
	<div id='somewrapper'>
		<div class='bloom-imageContainer'>
			<div class='bloom-imageDescription bloom-translationGroup'>
				<div class='bloom-editable' lang='en'>
					A Picture
				</div>
				<div class='bloom-editable' lang='es'>
					A Picture
				</div>
			</div>
		</div>
		<div class='bloom-translationGroup'>
			<div id='{audioPrefix}1' class='audio-sentence bloom-editable' lang='en'>
				Page One
			</div>
			<div id='{audioPrefix}2' class='audio-sentence bloom-editable' lang='es'>
				Pagino Dos
			</div>
		</div>
		<div class='bloom-videoContainer'>
			<video><source src='video/{videoFilename}'>
			</source></video>
		</div>
	</div>
</div>"
            );

            for (int i = 1; i <= 2; ++i)
                BookStorageTests.MakeSampleAudioFiles(
                    _tempFolder.Path,
                    $"{audioPrefix}{i}",
                    ".mp3"
                );

            Directory.CreateDirectory(Path.Combine(_tempFolder.Path, "video"));
            BookStorageTests.MakeSampleVideoFiles(_tempFolder.Path, videoFilename);
        }

        [TestCase(true, true)]
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void UpdateMetadataFeatures_TestEnabledFlags_SwitchesWork(
            bool isTalkingBookEnabled,
            bool isSignLanguageEnabled
        )
        {
            SetupUpdateMetadataFeaturesTest();
            var book = CreateBook();

            book.UpdateMetadataFeatures(isTalkingBookEnabled, isSignLanguageEnabled, null);

            Assert.That(
                book.BookInfo.MetaData.Feature_TalkingBook,
                Is.EqualTo(isTalkingBookEnabled),
                "Feature_TalkingBook"
            );
            Assert.That(
                book.BookInfo.MetaData.Feature_SignLanguage,
                Is.EqualTo(isSignLanguageEnabled),
                "Feature_SignLanguage"
            );
        }

        [TestCase(
            null,
            new string[] { "en", "es" },
            TestName = "UpdateMetadataFeatures_AllLangsAllowed_AllLangsReturned"
        )]
        [TestCase(
            new string[] { "en", "es" },
            new string[] { "en", "es" },
            TestName = "UpdateMetadataFeatures_TwoLangsAllowed_TwoLangsReturned"
        )]
        [TestCase(
            new string[] { "en" },
            new string[] { "en" },
            TestName = "UpdateMetadataFeatures_OneLangAllowed_OnlyOneLangReturned"
        )]
        [TestCase(
            new string[] { "fr" },
            new string[0],
            TestName = "UpdateMetadataFeatures_UntranslatedLangAllowed_NoneReturned"
        )]
        [TestCase(
            new string[0],
            new string[0],
            TestName = "UpdateMetadataFeatures_NoneAllowed_NoneReturned"
        )]
        public void UpdateMetadataFeatures_CheckAllowedLanguages(
            string[] allowedLangs,
            string[] expectedResults
        )
        {
            const string signLangaugeCode = "ase";
            SetupUpdateMetadataFeaturesTest();
            var book = CreateBook();
            book.CollectionSettings.SignLanguageTag = signLangaugeCode;

            book.UpdateMetadataFeatures(true, true, allowedLangs);

            CollectionAssert.AreEquivalent(
                expectedResults,
                book.BookInfo.MetaData.Feature_TalkingBook_LangCodes,
                "TalkingBook"
            );

            // SignLanaguage doesn't care about allowedLangs setting, only its enabled flag.
            CollectionAssert.AreEquivalent(
                new string[1] { signLangaugeCode },
                book.BookInfo.MetaData.Feature_SignLanguage_LangCodes,
                "SignLanguage"
            );
        }

        [Test]
        public void UpdateMetadataFeatures_QuizMissing_QuizFeatureFalse()
        {
            SetDom(
                @"
<div class='bloom-page numberedPage'>
	<div class='marginBox'>
		<div class='bloom-translationGroup'>
			<!-- Blah blah, more content goes here -->
		</div>
	</div>
</div>"
            );

            var book = CreateBook();
            book.CollectionSettings.BrandingProjectKey = "MyCustomBrand"; // Needed so Enterprise Features is considered enabled which is needed for quizzes

            book.UpdateMetadataFeatures(false, false, null);

            Assert.AreEqual(false, book.BookInfo.MetaData.Feature_Quiz, "Quiz");
            CollectionAssert.AreEquivalent(
                new string[0],
                book.BookInfo.MetaData.Features,
                "Features"
            );
        }

        [Test]
        public void UpdateMetadataFeatures_QuizAdded_QuizFeatureTrue()
        {
            SetDom(
                @"
<div class='bloom-page simple-comprehension-quiz bloom-interactive-page'>
	<div class='marginBox'>
		<div class='quiz'>
			<!-- Blah blah, a bunch of quiz content would go in here -->
		</div>
	</div>
</div>"
            );

            var book = CreateBook();
            book.CollectionSettings.BrandingProjectKey = "MyCustomBrand"; // Needed so Enterprise Features is considered enabled which is needed for quizzes

            book.UpdateMetadataFeatures(false, false, null);

            Assert.AreEqual(true, book.BookInfo.MetaData.Feature_Quiz, "Quiz");
            CollectionAssert.AreEquivalent(
                new string[] { "activity", "quiz" },
                book.BookInfo.MetaData.Features,
                "Features"
            );
        }

        [Test]
        public void UpdateMetadataFeatures_WidgetActivityMissing_ActivityAndWidgetFeaturesFalse()
        {
            var html =
                @"<html>
					<body>
<div class='bloom-page simple-comprehension-quiz bloom-interactive-page'>
	<div class='marginBox'>
		
	</div>
</div>
</body></html>";

            var book = CreateBookWithPhysicalFile(html);
            book.BookInfo.MetaData.Feature_Widget = true; // spurious, see if it gets cleaned up

            book.UpdateMetadataFeatures(false, false, null);

            Assert.AreEqual(false, book.BookInfo.MetaData.Feature_Activity, "Activity");
            Assert.AreEqual(false, book.BookInfo.MetaData.Feature_Widget, "Widget");
            CollectionAssert.AreEquivalent(
                new string[0],
                book.BookInfo.MetaData.Features,
                "Features"
            );
        }

        [Test]
        public void UpdateMetadataFeatures_WidgetActivityAdded_ActivityAndWidgetFeaturesTrue()
        {
            var html =
                @"<html>
					<body>
<div class='bloom-page simple-comprehension-quiz bloom-interactive-page'>
	<div class='marginBox'>
		<div class='bloom-widgetContainer'>
			<iframe src='something'></iframe>
		</div>
	</div>
</div>
</body></html>";

            var book = CreateBookWithPhysicalFile(html);

            book.UpdateMetadataFeatures(false, false, null);

            Assert.AreEqual(true, book.BookInfo.MetaData.Feature_Activity, "Activity");
            Assert.AreEqual(true, book.BookInfo.MetaData.Feature_Widget, "Widget");
            CollectionAssert.AreEquivalent(
                new string[] { "activity", "widget" },
                book.BookInfo.MetaData.Features,
                "Features"
            );
        }

        [Test]
        public void UpdateMetadataFeatures_ComicMissing_ComicFeatureFalse()
        {
            // Setup
            SetDom(
                @"
<html>
	<body>
		<div class='bloom-page'>
			<div class='bloom-imageContainer'>
			</div>
		</div>
	</body>
</html>
"
            );
            var book = CreateBook();

            // System under test
            bool propertyResult = book.HasOverlayPages;
            book.UpdateMetadataFeatures(false, false, null);

            // Verification
            Assert.AreEqual(false, propertyResult);
            Assert.AreEqual(false, book.BookInfo.MetaData.Feature_Comic, "Comic");
            CollectionAssert.AreEquivalent(
                new string[0],
                book.BookInfo.MetaData.Features,
                "Features"
            );
        }

        [Test]
        public void UpdateMetadataFeatures_ComicAdded_ComicFeatureTrue()
        {
            // Setup
            SetDom(
                @"
<html>
	<body>
		<div class='bloom-page'>
			<div class='bloom-imageContainer'>
				<svg class='comical-generated'>
					<!-- Stuff goes here -->
				</svg>
			</div>
		</div>
	</body>
</html>
"
            );
            var book = CreateBook();

            // System under test
            bool propertyResult = book.HasOverlayPages;
            book.UpdateMetadataFeatures(false, false, null);

            // Verification
            Assert.AreEqual(true, propertyResult);
            Assert.AreEqual(true, book.BookInfo.MetaData.Feature_Comic, "Comic");
            CollectionAssert.AreEquivalent(
                new string[] { "comic" },
                book.BookInfo.MetaData.Features,
                "Features"
            );
        }

        [Test]
        public void UpdateMetadataFeatures_MotionMissing_MotionFeatureFalse()
        {
            _bookDom = new HtmlDom(
                @"<html>
	<body>
		 <div class='bloom-page numberedPage'>
			<div class='marginBox'>
				<div class='bloom-imageContainer'>
					<img src='aor_CMB001.png'></img>
				</div>
				<div class='bloom-translationGroup'>
					<!-- Blah blah, more content goes here -->
				</div>
			</div>
		</div>
	</body>
</html>"
            );

            var book = CreateBook();

            book.UpdateMetadataFeatures(false, false, null);

            Assert.AreEqual(false, book.BookInfo.MetaData.Feature_Motion, "Feature_Motion");
            CollectionAssert.AreEquivalent(
                new string[0],
                book.BookInfo.MetaData.Features,
                "Features"
            );
        }

        [Test]
        public void UpdateMetadataFeatures_MotionAdded_MotionFeatureTrue()
        {
            // The content of the DOM no longer matters. I've deliberately deleted the body attributes
            // that used to determine it.
            _bookDom = new HtmlDom(
                @"<html>
	<body>
		 <div class='bloom-page numberedPage'>
			<div class='marginBox'>
				<div class='bloom-imageContainer' data-initialrect='0 0 1 1' data-finalrect='0.3 0.3 0.5 0.5'>
					<img src='aor_CMB001.png'></img>
				</div>
				<div class='bloom-translationGroup'>
					<!-- Blah blah, more content goes here -->
				</div>
			</div>
		</div>
	</body>
</html>"
            );

            var book = CreateBook();
            book.BookInfo.PublishSettings.BloomPub.PublishAsMotionBookIfApplicable = true;

            book.UpdateMetadataFeatures(false, false, null);

            Assert.AreEqual(true, book.BookInfo.MetaData.Feature_Motion, "Feature_Motion");
            CollectionAssert.AreEquivalent(
                new string[] { "motion" },
                book.BookInfo.MetaData.Features,
                "Features"
            );
        }

        private Mock<IPage> CreateTemplatePage(string divContent)
        {
            var mockTemplateBook = new Moq.Mock<Bloom.Book.Book>();
            mockTemplateBook
                .Setup(x => x.OurHtmlDom.GetTemplateStyleSheets())
                .Returns(new string[] { });

            var templatePage = new Moq.Mock<IPage>();

            templatePage.Setup(x => x.Book).Returns(mockTemplateBook.Object);
            var d = new XmlDocument();
            d.LoadXml("<wrapper>" + divContent + "</wrapper>");
            var pageContentElement = (XmlElement)d.SelectSingleNode("//div");
            templatePage.Setup(x => x.GetDivNodeForThisPage()).Returns(pageContentElement);

            return templatePage;
        }

        [Test]
        public void RemoveNonPublishablePages_Works()
        {
            _bookDom = new HtmlDom(
                @"
				<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='guid1' data-page-number='1'>
						<div />
					</div>
					<div class='bloom-page numberedPage bloom-noAudio' id='guid2' data-page-number='2'>
					</div>
					<div class='bloom-page numberedPage bloom-noreader' id='guid3' data-page-number='3'>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>This is visible!</div>
					</div>
					<div class='bloom-page numberedPage screen-only' id='guid4' data-page-number='4'>
						<div class='bloom-imageContainer some other classes'></div>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>This is visible!</div>
					</div>
				</body></html>"
            );
            var book = CreateBook();

            // SUT
            book.RemoveNonPublishablePages();

            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(@"//div[contains(@class, 'bloom-page')]", 3);
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasNoMatchForXpath(@"//div[contains(@class,'bloom-noreader')]");
        }

        [Test]
        public void HasAudio_OnlyNonAudioSpans_ReturnsFalse()
        {
            // Test setup
            string id = "guid1";
            _bookDom = new HtmlDom(
                $@"
				<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='guid1' data-page-number='1'>
						<p><span id='{id}' class='video-sentence'>Page 1 Paragraph 1 Sentence 1</span></p>
					</div>
				</body></html>"
            );
            var book = CreateBook();

            BookStorageTests.MakeSampleAudioFiles(_tempFolder.Path, id, ".mp4");

            // System under test //
            bool result = book.HasAudio();

            // Verification
            Assert.AreEqual(false, result);
        }

        [Test]
        public void HasAudio_ContainsAudioSpans_ReturnsTrue()
        {
            // Test setup
            string id = "guid1";
            _bookDom = new HtmlDom(
                $@"
				<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='guid1' data-page-number='1'>
						<p><span id='{id}' class='audio-sentence'>Page 1 Paragraph 1 Sentence 1</span></p>
					</div>
				</body></html>"
            );
            var book = CreateBook();

            BookStorageTests.MakeSampleAudioFiles(_tempFolder.Path, id, ".wav", ".mp3");

            // System under test //
            bool result = book.HasAudio();

            // Verification //
            Assert.AreEqual(true, result);
        }

        [Test]
        public void HasAudio_ContainsAudioDivs_ReturnsTrue()
        {
            // Test setup
            string id = "guid1";
            _bookDom = new HtmlDom(
                $@"
				<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='page1' data-page-number='1'>
						<div id='guid1' class='audio-sentence'>
							<p>Page 1 Paragraph 1 Sentence 1</p>
							<p>Page 1 Paragraph 2 Sentence 1</p>
						</div>
					</div>
				</body></html>"
            );
            var book = CreateBook();

            BookStorageTests.MakeSampleAudioFiles(_tempFolder.Path, id, ".wav", ".mp3");

            // System under test //
            bool result = book.HasAudio();

            // Verification //
            Assert.AreEqual(true, result);
        }

        [Test]
        public void HasVideos_ContainsValidVideoDiv_ReturnsTrue()
        {
            // Test setup
            var videoPath = Path.Combine(_tempFolder.Path, "video"); //Path to the video files.
            Directory.CreateDirectory(videoPath);
            string filename = "guid1.mp4";
            _bookDom = new HtmlDom(
                $@"
				<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='page1' data-page-number='1'>
						<div class='bloom-videoContainer bloom-leadingElement bloom-selected'>
							<video>
								<source src='video/"
                    + filename
                    + @"' type='video/mp4'></source>
							</video>
						</div>
					</div>
				</body></html>"
            );
            var book = CreateBook();

            BookStorageTests.MakeSampleVideoFiles(_tempFolder.Path, filename);

            // System under test //
            bool result = book.HasVideos();

            // Verification //
            Assert.IsTrue(result);
        }

        [Test]
        public void HasVideos_ContainsValidVideoDivWithTimings_ReturnsTrue()
        {
            // Test setup
            var videoPath = Path.Combine(_tempFolder.Path, "video"); //Path to the video files.
            Directory.CreateDirectory(videoPath);
            string filename = "guid1.mp4";
            _bookDom = new HtmlDom(
                $@"
				<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='page1' data-page-number='1'>
						<div class='bloom-videoContainer bloom-leadingElement bloom-selected'>
							<video>
								<source src='video/"
                    + filename
                    + @"#t=0.0,4.6' type='video/mp4'></source>
							</video>
						</div>
					</div>
				</body></html>"
            );
            var book = CreateBook();

            BookStorageTests.MakeSampleVideoFiles(_tempFolder.Path, filename);

            // System under test //
            bool result = book.HasVideos();

            // Verification //
            Assert.IsTrue(result);
        }

        [Test]
        public void HasVideos_VideoDivReferencesNonexistentFile()
        {
            // Test setup
            var videoPath = Path.Combine(_tempFolder.Path, "video"); //Path to the video files.
            Directory.CreateDirectory(videoPath);
            string filename = "guid1.mp4";
            _bookDom = new HtmlDom(
                $@"
				<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='page1' data-page-number='1'>
						<div class='bloom-videoContainer bloom-leadingElement bloom-selected'>
							<video>
								<source src='video/"
                    + filename
                    + @"' type='video/mp4'></source>
							</video>
						</div>
					</div>
				</body></html>"
            );
            var book = CreateBook();

            // System under test //
            bool result = book.HasVideos();

            // Verification //
            Assert.IsFalse(result);
        }

        [TestCase("span")]
        [TestCase("div")]
        public void HasFullAudioCoverage_ContainsMissingAudioElements_ReturnsFalse(
            string elementName
        )
        {
            // Test setup
            string lang = CreateDefaultCollectionsSettings().Language1Tag;
            _bookDom = new HtmlDom(
                $@"
				<html><head></head><body>
					<div class='bloom-page numberedPage' id='guid1'>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='{lang}'>
							<p><{elementName} id='id1' class='audio-sentence'>Sentence 1.</{elementName}>
							   <{elementName} id='id2' class='audio-sentence'>Sentence 2.</{elementName}></p>
						</div>
					</div>
				  </body></html>"
            );
            var book = CreateBook();

            BookStorageTests.MakeSampleAudioFiles(_tempFolder.Path, "id1", ".wav", ".mp3");

            // System under test //
            bool result = book.HasFullAudioCoverage();

            // Verification //
            Assert.AreEqual(false, result, $"ElementName: {elementName}");
        }

        [TestCase("span")]
        [TestCase("div")]
        public void HasFullAudioCoverage_ContainsAllAudioElements_ReturnsTrue(string elementName)
        {
            // Test setup
            string lang = CreateDefaultCollectionsSettings().Language1Tag;
            _bookDom = new HtmlDom(
                $@"
				<html><head></head><body>
					<div class='bloom-page numberedPage' id='guid1'>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='{lang}'>
							<p><{elementName} id='id1' class='audio-sentence'>Sentence 1.</{elementName}>
							   <{elementName} id='id2' class='audio-sentence'>Sentence 2.</{elementName}></p>
						</div>
					</div>
				  </body></html>"
            );
            var book = CreateBook();

            BookStorageTests.MakeSampleAudioFiles(_tempFolder.Path, "id1", ".wav", ".mp3");
            BookStorageTests.MakeSampleAudioFiles(_tempFolder.Path, "id2", ".wav", ".mp3");

            // System under test //
            bool result = book.HasFullAudioCoverage();

            // Verification //
            Assert.AreEqual(true, result, $"ElementName: {elementName}");
        }

        private const string _pathToTestVideos = "src/BloomTests/videos";

        [Test]
        public void PrepareBookVideoForPublishing_AddControls()
        {
            SetDom(
                @"<div id='bloomDataDiv'></div>
				<div class='bloom-page'>
					<div class='marginBox'>
						<div class='variousSplitContainerStuff'>
							<div class='bloom-videoContainer'>
								<video>
									<source src='video/Crow.mp4#t=1.2,5.7'></source>
								</video>
							</div>
						</div>
						<div class='variousSplitContainerStuff'>
							<div class='bloom-videoContainer bloom-noVideoSelected'></div>
						</div>
						<div class='variousSplitContainerStuff'>
							<div class='bloom-videoContainer bloom-selected'>
								<video>
									<source src='video/Five%20count.mp4#t=0.0,5.2'></source>
								</video>
							</div>
						</div>
					</div>
				</div>"
            );
            var book = CreateBook();
            var bookDom = book.OurHtmlDom;

            // Setup 'real' video files, otherwise we aren't actually trimming any video files
            var videoFolder = Path.Combine(book.FolderPath, "video");
            Directory.CreateDirectory(videoFolder);
            RobustFile.Copy(
                FileLocationUtilities.GetFileDistributedWithApplication(
                    _pathToTestVideos,
                    "Crow.mp4"
                ),
                Path.Combine(videoFolder, "Crow.mp4")
            );
            RobustFile.Copy(
                FileLocationUtilities.GetFileDistributedWithApplication(
                    _pathToTestVideos,
                    "Five count.mp4"
                ),
                Path.Combine(videoFolder, "Five count.mp4")
            );

            var videoContainerElements = bookDom.SafeSelectNodes(
                ".//div[contains(@class, 'bloom-videoContainer')]"
            );
            // Process each discovered videoContainer
            foreach (XmlElement videoContainerElement in videoContainerElements)
            {
                SignLanguageApi.PrepareVideoForPublishing(
                    videoContainerElement,
                    book.FolderPath,
                    videoControls: true
                );
            }

            const string videoWithControlsXpath = ".//video[@controls='']";
            const string hashTimingsXpath = ".//source[contains(@src, '#t=')]";

            // We didn't change the # of videos or sources and the video elements now include 'controls' attribute.
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(videoWithControlsXpath, 2);
            // For the Crow video, which was trimmed, we should also strip off timings in 'src' attributes.
            // The Five count video should not actually be trimmed, due to the timings being set to the whole
            // length of the video. And due to it not being trimmed, it still has its timings in the 'src' attribute.
            // This can happen if the user set timings on a video at some point and then went back and
            // moved the sliders back to their maximum settings.
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(hashTimingsXpath, 1);
        }

        [Test]
        public void SelectAudioSentenceElements_SentenceWithNoId_SkipsIt()
        {
            string html =
                @"<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='page1' data-page-number='1'>
						<div class='bloom-editable'>
							<p><span class='audio-sentence'>Page 1 Paragraph 1 Sentence 1</span></p>
							<p><span id='id2' class='audio-sentence'>Page 1 Paragraph 2 Sentence 1</span></p>
							<p><span id='' class='audio-sentence'>Page 1 Paragraph 3 Sentence 1</span></p>
						</div>
					</div>
				</body></html>";

            var dom = new HtmlDom(html);
            var audioSpans = HtmlDom.SelectAudioSentenceElements(dom.Body);
            Assert.That(audioSpans, Has.Count.EqualTo(1));
            Assert.That(audioSpans[0].InnerText, Is.EqualTo("Page 1 Paragraph 2 Sentence 1"));
        }

        [Test]
        public void HasAudioSentenceElementsWithoutId_HandlesEmptyId()
        {
            string html =
                @"<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='page1' data-page-number='1'>
						<div class='bloom-editable'>
							<p><span id='id1' class='audio-sentence'>Page 1 Paragraph 1 Sentence 1</span></p>
							<p><span id='id2' class='audio-sentence'>Page 1 Paragraph 2 Sentence 1</span></p>
							<p><span id='' class='audio-sentence'>Page 1 Paragraph 3 Sentence 1</span></p>
						</div>
					</div>
				</body></html>";

            var dom = new HtmlDom(html);
            var hasMissingId = HtmlDom.HasAudioSentenceElementsWithoutId(dom.Body);
            Assert.That(hasMissingId, Is.True);
        }

        [Test]
        public void HasAudioSentenceElementsWithoutId_HandlesDivEmptyId()
        {
            string html =
                @"<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='page1' data-page-number='1'>
						<div class='bloom-editable audio-sentence' id=''>
							<p><span id='id1' class='audio-sentence'>Page 1 Paragraph 1 Sentence 1</span></p>
							<p><span id='id2' class='audio-sentence'>Page 1 Paragraph 2 Sentence 1</span></p>
							<p><span id='id3' class='audio-sentence'>Page 1 Paragraph 3 Sentence 1</span></p>
						</div>
					</div>
				</body></html>";

            var dom = new HtmlDom(html);
            var hasMissingId = HtmlDom.HasAudioSentenceElementsWithoutId(dom.Body);
            Assert.That(hasMissingId, Is.True);
        }

        [Test]
        public void HasAudioSentenceElementsWithoutId_HandlesMissingId()
        {
            string html =
                @"<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='page1' data-page-number='1'>
						<div class='bloom-editable'>
							<p><span id='id1' class='audio-sentence'>Page 1 Paragraph 1 Sentence 1</span></p>
							<p><span id='id2' class='audio-sentence'>Page 1 Paragraph 2 Sentence 1</span></p>
							<p><span class='audio-sentence'>Page 1 Paragraph 3 Sentence 1</span></p>
						</div>
					</div>
				</body></html>";

            var dom = new HtmlDom(html);
            var hasMissingId = HtmlDom.HasAudioSentenceElementsWithoutId(dom.Body);
            Assert.That(hasMissingId, Is.True);
        }

        [Test]
        public void HasAudioSentenceElementsWithoutId_HandlesDivMissingId()
        {
            string html =
                @"<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='page1' data-page-number='1'>
						<div class='bloom-editable audio-sentence'>
							<p><span id='id1' class='audio-sentence'>Page 1 Paragraph 1 Sentence 1</span></p>
							<p><span id='id2' class='audio-sentence'>Page 1 Paragraph 2 Sentence 1</span></p>
							<p><span id='id3' class='audio-sentence'>Page 1 Paragraph 3 Sentence 1</span></p>
						</div>
					</div>
				</body></html>";

            var dom = new HtmlDom(html);
            var hasMissingId = HtmlDom.HasAudioSentenceElementsWithoutId(dom.Body);
            Assert.That(hasMissingId, Is.True);
        }

        [Test]
        public void HasAudioSentenceElementsWithoutId_HandlesIdsAllThere()
        {
            string html =
                @"<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='page1' data-page-number='1'>
						<div class='bloom-editable'>
							<p><span id='id1' class='audio-sentence'>Page 1 Paragraph 1 Sentence 1</span></p>
							<p><span id='id2' class='audio-sentence'>Page 1 Paragraph 2 Sentence 1</span></p>
							<p><span id='id3' class='audio-sentence'>Page 1 Paragraph 3 Sentence 1</span></p>
						</div>
					</div>
				</body></html>";

            var dom = new HtmlDom(html);
            var hasMissingId = HtmlDom.HasAudioSentenceElementsWithoutId(dom.Body);
            Assert.That(hasMissingId, Is.False);
        }

        [Test]
        public void HasAudioSentenceElementsWithoutId_HandlesDivIdsAllThere()
        {
            string html =
                @"<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='page1' data-page-number='1'>
						<div class='bloom-editable audio-sentence' id='id0'>
							<p><span id='id1' class='audio-sentence'>Page 1 Paragraph 1 Sentence 1</span></p>
							<p><span id='id2' class='audio-sentence'>Page 1 Paragraph 2 Sentence 1</span></p>
							<p><span id='id3' class='audio-sentence'>Page 1 Paragraph 3 Sentence 1</span></p>
						</div>
					</div>
				</body></html>";

            var dom = new HtmlDom(html);
            var hasMissingId = HtmlDom.HasAudioSentenceElementsWithoutId(dom.Body);
            Assert.That(hasMissingId, Is.False);
        }

        [Test]
        public void RepairCoverImageDescriptions_ExtractsMultipleDescriptions()
        {
            string html =
                @"<html><head></head>
<body>
	<div id='bloomDataDiv'>
		<div data-book='coverImageDescription' lang='*'>
			<div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable normal-style bloom-content1 bloom-visibility-code-on' lang='en' contenteditable='true'>
				<p>musical score in artistic waves</p>
			</div>
			<div style='' class='bloom-editable normal-style' lang='z' contenteditable='true'></div>
			<div data-languagetipcontent='español' style='' class='bloom-editable normal-style bloom-contentNational2' lang='es' contenteditable='true'>
				<p>partitura musical en ondas artísticas</p>
			</div>
			<div data-languagetipcontent='français' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='' class='bloom-editable normal-style bloom-contentNational1' lang='fr' contenteditable='true'>
				<p>partition musicale en vagues artistiques</p>
			</div>
		</div>
	</div>
	<div class='bloom-page' id='guid1'>
		<div class='bloom-editable bloom-content1' contenteditable='true'></div>
		<div class='bloom-editable bloom-content2' contenteditable='true'></div>
		<div class='bloom-editable bloom-content3' contenteditable='true'></div>
	</div>
</body></html>";
            var dom = new HtmlDom(html);

            // The old xmatter template put the data-book attribute on the translationGroup level instead of the editable level.
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription' and @lang='*']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription']/div",
                    4
                );

            // SUT
            Bloom.Book.Book.RepairCoverImageDescriptions(dom);

            CheckForGoodSetOfCoverImageDescriptionDataDivs(dom);
        }

        [Test]
        public void RepairCoverImageDescriptions_LeavesWellEnoughAlone()
        {
            string html =
                @"<html><head></head>
<body>
	<div id='bloomDataDiv'>
		<div data-book='coverImageDescription' lang='en'>
			<p>musical score in artistic waves</p>
		</div>
		<div data-book='coverImageDescription' lang='es'>
			<p>partitura musical en ondas artísticas</p>
		</div>
		<div data-book='coverImageDescription' lang='fr'>
			<p>partition musicale en vagues artistiques</p>
		</div>
	</div>
	<div class='bloom-page' id='guid1'>
		<div class='bloom-editable bloom-content1' contenteditable='true'></div>
		<div class='bloom-editable bloom-content2' contenteditable='true'></div>
		<div class='bloom-editable bloom-content3' contenteditable='true'></div>
	</div>
</body></html>";
            var dom = new HtmlDom(html);

            CheckForGoodSetOfCoverImageDescriptionDataDivs(dom);

            // SUT
            Bloom.Book.Book.RepairCoverImageDescriptions(dom);

            // Verify that nothing has changed materially.  (same set of assertions)
            CheckForGoodSetOfCoverImageDescriptionDataDivs(dom);
        }

        private void CheckForGoodSetOfCoverImageDescriptionDataDivs(HtmlDom dom)
        {
            // This checking method is shared by the two previous tests.
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription']",
                    3
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription']/p",
                    3
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription' and @lang='en']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription' and @lang='es']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription' and @lang='fr']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription' and @lang='z']"
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription']/div"
                );
        }

        [Test]
        public void RepairCoverImageDescriptions_RemovesEmptyDescription()
        {
            string html =
                @"<html><head></head>
<body>
	<div id='bloomDataDiv'>
		<div data-book='coverImageDescription' lang='*'>
			<div style='' class='bloom-editable normal-style' lang='z' contenteditable='true'></div>
		</div>
	</div>
	<div class='bloom-page' id='guid1'>
		<div class='bloom-editable bloom-content1' contenteditable='true'></div>
		<div class='bloom-editable bloom-content2' contenteditable='true'></div>
		<div class='bloom-editable bloom-content3' contenteditable='true'></div>
	</div>
</body></html>";
            var dom = new HtmlDom(html);

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription']/div[@lang='z']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription']",
                    1
                );

            // SUT
            Bloom.Book.Book.RepairCoverImageDescriptions(dom);

            // Verify that nothing is now stored for coverImageDescription.
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription']"
                );
        }

        [Test]
        public void RepairCoverImageDescriptions_DoesNotDoubleData()
        {
            string html =
                @"<html><head></head>
<body>
	<div id='bloomDataDiv'>
		<div data-book='coverImageDescription' lang='*'>
			<div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable normal-style bloom-content1 bloom-visibility-code-on' lang='en' contenteditable='true'>
				<p>mother cat carrying a kitten</p>
			</div>
			<div style='' class='bloom-editable normal-style' lang='z' contenteditable='true'></div>
			<div data-languagetipcontent='español' style='' class='bloom-editable normal-style bloom-contentNational2' lang='es' contenteditable='true'>
				<p>madre gato llevando un gatito</p>
			</div>
		</div>
		<div data-book='coverImageDescription' lang='en'>
			<div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable normal-style bloom-content1 bloom-visibility-code-on' lang='en' contenteditable='true'>
				<p>mother cat carrying a kitten</p>
			</div>
			<div style='' class='bloom-editable normal-style' lang='z' contenteditable='true'></div>
			<div data-languagetipcontent='español' style='' class='bloom-editable normal-style bloom-contentNational2' lang='es' contenteditable='true'>
				<p>madre gato llevando un gatito</p>
			</div>
		</div>
	</div>
	<div class='bloom-page' id='guid1'>
		<div class='bloom-editable bloom-content1' contenteditable='true'></div>
		<div class='bloom-editable bloom-content2' contenteditable='true'></div>
		<div class='bloom-editable bloom-content3' contenteditable='true'></div>
	</div>
</body></html>";
            var dom = new HtmlDom(html);

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription']",
                    2
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription' and @lang='*']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription' and @lang='en']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription']/div",
                    6
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription']/p"
                );

            // SUT
            Bloom.Book.Book.RepairCoverImageDescriptions(dom);

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription']",
                    2
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription' and @lang='en']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription' and @lang='es']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription']/p",
                    2
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription' and @lang='z']"
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription']/div"
                );
        }

        [Test]
        public void IsPageBloomEnterpriseOnly_HasEnterpriseOnlyClass_True()
        {
            var xml =
                "<div class='bloom-page simple-comprehension-quiz enterprise-only bloom-interactive-page side-right A5Portrait bloom-monolingual'></div>";

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xml);
            XmlElement page = doc.DocumentElement;
            Assert.True(Bloom.Book.Book.IsPageBloomEnterpriseOnly(page));
        }

        // Originally, video was enterprise-only, so the logic was reversed.
        // Now we want to be sure that video does not trigger a page as enterprise-only.
        [Test]
        public void IsPageBloomEnterpriseOnly_HasVideo_False()
        {
            string xml =
                @"
	<div class=""bloom-page numberedPage customPage side-left A5Portrait bloom-monolingual"" data-page="""" id=""4854bc4a-0046-426e-9e19-596773582d23"" data-pagelineage=""8bedcdf8-3ad6-4967-b027-6c186436572f"" data-page-number=""2"" lang="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Just Video"" lang=""en"">
            Just Video
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""marginBox"">
            <div class=""split-pane-component-inner"">
                <div class=""box-header-off bloom-translationGroup"">
                    Processing

                    <div data-languagetipcontent=""English"" class=""bloom-editable bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" aria-label=""false"" role=""textbox"" spellcheck=""true"" tabindex=""0"" contenteditable=""true"" lang=""en"">
                        <p></p>
                    </div>
                </div>

                <div class=""bloom-videoContainer bloom-noVideoSelected bloom-leadingElement bloom-selected"">
                    <video>
                    <source src=""video/8e7297fd-8ecf-41a9-b82a-e7020a1314ca.mp4#t=0.0,1.6""></source></video>
                </div>
            </div>
        </div>
    </div>";

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xml);
            XmlElement page = doc.DocumentElement;
            Assert.False(Bloom.Book.Book.IsPageBloomEnterpriseOnly(page));
        }

        [Test]
        public void IsPageBloomEnterpriseOnly_NoEnterpriseOnlyItems_False()
        {
            string xml =
                @"
	<div class=""bloom-page numberedPage customPage side-left A5Portrait bloom-monolingual"" data-page="""" id=""4854bc4a-0046-426e-9e19-596773582d23"" data-pagelineage=""8bedcdf8-3ad6-4967-b027-6c186436572f"" data-page-number=""2"" lang="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Just Video"" lang=""en"">
            Just Video
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""marginBox"">
            <div class=""split-pane-component-inner"">
                <div class=""box-header-off bloom-translationGroup"">
                    Processing

                    <div data-languagetipcontent=""English"" class=""bloom-editable bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" aria-label=""false"" role=""textbox"" spellcheck=""true"" tabindex=""0"" contenteditable=""true"" lang=""en"">
                        <p></p>
                    </div>
                </div>
            </div>
        </div>
    </div>";

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xml);
            XmlElement page = doc.DocumentElement;
            Assert.False(Bloom.Book.Book.IsPageBloomEnterpriseOnly(page));
        }

        private const string kSentenceModeRecordingHtml =
            @"<!DOCTYPE html [ <!ENTITY nbsp '&#160;'> ]>
<html>
  <head><meta charset='UTF-8'></meta></head>
  <body lang='en'>
    <div class='bloom-page numberedPage' id='e63d0a55-7228-41b9-b0e2-38386613826b' data-page-number='1' lang='en'>
        <div class='marginBox'>
            <div style='min-height: 42px;' class='split-pane horizontal-percent'>
                <div class='split-pane-component position-top' style='bottom: 50%'>
                    <div class='split-pane-component-inner'>
                        <div class='bloom-imageContainer bloom-leadingElement'>
                            <img data-license='cc-by-sa' data-copyright='Copyright SIL International 2009' src='aor_Cat3.png' alt='cat lying down looking at you'></img>
                            <div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement'>
                                <div data-audiorecordingmode='Sentence' role='textbox' class='bloom-editable ImageDescriptionEdit-style' lang='en'>
                                    <p><span id='fe0b3747-50d2-49ed-97b7-87f7a956c694' class='audio-sentence'>cat lying down looking at you</span></p>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
                <div class='split-pane-component position-bottom' style='height: 50%'>
                    <div class='split-pane-component-inner'>
                        <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto'>
                            <div data-audiorecordingmode='Sentence' role='textbox' class='bloom-editable normal-style' lang='en'>
                                <p><span id='a2a1a35b-f673-43da-b208-0a9d0adcc62d' class='audio-sentence' recordingmd5='undefined'>This is a <em>cat</em>.</span>&nbsp;<span id='eb91602e-bba1-4089-a5e4-f29b70e08af6' class='audio-sentence'>Believe it!</span></p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
  </body>
</html>";

        [Test]
        public void RemoveObsoleteAudioMarkup_WorksForSentenceModeRecording()
        {
            _bookDom = new HtmlDom(kSentenceModeRecordingHtml);
            var book = CreateBook();
            VerifyInitialSentenceModeMarkup();

            // SUT
            book.RemoveObsoleteAudioMarkup();

            AssertThatXmlIn
                .Dom(_bookDom.RawDom)
                .HasNoMatchForXpath("//div[@data-audiorecordingmode]");
            AssertThatXmlIn
                .Dom(_bookDom.RawDom)
                .HasNoMatchForXpath("//span[@class='audio-sentence']");
            AssertThatXmlIn.Dom(_bookDom.RawDom).HasNoMatchForXpath("//span[@recordingmd5]");
            // verify that text content hasn't changed with removal of the spans
            var para =
                _bookDom.RawDom.SelectSingleNode(
                    "//div[contains(@class,'ImageDescriptionEdit-style')]/p"
                ) as XmlElement;
            Assert.That(para, Is.Not.Null);
            Assert.That(para.InnerXml, Is.EqualTo("cat lying down looking at you"));
            Assert.That(para.InnerText, Is.EqualTo("cat lying down looking at you"));
            para =
                _bookDom.RawDom.SelectSingleNode("//div[contains(@class,'normal-style')]/p")
                as XmlElement;
            Assert.That(para, Is.Not.Null);
            Assert.That(para.InnerXml, Is.EqualTo("This is a <em>cat</em>.&nbsp;Believe it!"));
            Assert.That(para.InnerText, Is.EqualTo("This is a cat.\u00A0Believe it!"));
        }

        [Test]
        public void RemoveObsoleteAudioMarkup_DoesNotRemoveValidSentenceModeMarkup()
        {
            _bookDom = new HtmlDom(kSentenceModeRecordingHtml);
            var book = CreateBook();
            VerifyInitialSentenceModeMarkup();

            // SUT
            book.RemoveObsoleteAudioMarkup((folder, file) => true);

            // Note that these asserts are exactly the same as before running the method with the (trivial) function for testing
            // audio file existence.
            VerifyInitialSentenceModeMarkup();
        }

        private void VerifyInitialSentenceModeMarkup()
        {
            AssertThatXmlIn
                .Dom(_bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@data-audiorecordingmode]", 2);
            AssertThatXmlIn
                .Dom(_bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//span[@class='audio-sentence']", 3);
            AssertThatXmlIn
                .Dom(_bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//span[@recordingmd5]", 1);
            var para =
                _bookDom.RawDom.SelectSingleNode(
                    "//div[contains(@class,'ImageDescriptionEdit-style')]/p"
                ) as XmlElement;
            Assert.That(para, Is.Not.Null);
            Assert.That(
                para.InnerXml,
                Is.EqualTo(
                    "<span id=\"fe0b3747-50d2-49ed-97b7-87f7a956c694\" class=\"audio-sentence\">cat lying down looking at you</span>"
                )
            );
            Assert.That(para.InnerText, Is.EqualTo("cat lying down looking at you"));
            para =
                _bookDom.RawDom.SelectSingleNode("//div[contains(@class,'normal-style')]/p")
                as XmlElement;
            Assert.That(para, Is.Not.Null);
            Assert.That(
                para.InnerXml,
                Is.EqualTo(
                    "<span id=\"a2a1a35b-f673-43da-b208-0a9d0adcc62d\" class=\"audio-sentence\" recordingmd5=\"undefined\">This is a <em>cat</em>.</span>&nbsp;<span id=\"eb91602e-bba1-4089-a5e4-f29b70e08af6\" class=\"audio-sentence\">Believe it!</span>"
                )
            );
            Assert.That(para.InnerText, Is.EqualTo("This is a cat.\u00A0Believe it!"));
        }

        private const string kTextBoxModeRecordingHtml =
            @"<!DOCTYPE html [ <!ENTITY nbsp '&#160;'> ]>
<html>
  <head><meta charset='UTF-8'></meta></head>
  <body lang='en'>
    <div class='bloom-page numberedPage' id='77bd6b91-91e4-45c2-bff1-73a9ca0b5500' data-page-number='1'>
        <div class='marginBox'>
            <div style='min-height: 42px;' class='split-pane horizontal-percent'>
                <div class='split-pane-component position-top' style='bottom: 50%'>
                    <div class='split-pane-component-inner'>
                        <div title='aor_Cat3.png 44.36 KB 1500 x 1248 355 DPI (should be 300-600) Bit Depth: 1' class='bloom-imageContainer bloom-leadingElement'>
                            <img data-license='cc-by-sa' data-copyright='Copyright SIL International 2009' src='aor_Cat3.png' alt='cat lying down looking at the reader'></img>
                            <div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement'>
                                <div data-audiorecordingendtimes='3.36' data-duration='3.436167' id='i961d5cf7-f77c-4256-8f19-28afff4dc716' data-audiorecordingmode='TextBox'
                                     role='textbox' class='bloom-editable ImageDescriptionEdit-style audio-sentence' contenteditable='true' lang='en'>
                                    <p><span id='i542eb6e4' class='bloom-highlightSegment'>cat lying down looking at the reader</span></p>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
                <div class='split-pane-component position-bottom' style='height: 50%'>
                    <div class='split-pane-component-inner'>
                        <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto'>
                            <div data-audiorecordingendtimes='1.640 3.800 6.840' data-duration='6.936575' id='i4900c329-2a1c-44b4-9efc-37b1ee7a42d1' data-audiorecordingmode='TextBox'
                                 role='textbox' class='bloom-editable normal-style audio-sentence bloom-postAudioSplit' contenteditable='true' lang='en'>
                                <p><span id='i9c96ab8d' class='bloom-highlightSegment' recordingmd5='undefined'>This is a <em>cat</em>.</span>&nbsp; <span id='i7725f2c4' class='bloom-highlightSegment' recordingmd5='undefined'>This is <strong>only</strong> a cat.</span></p>
                                <p><span id='i8d4ddf89' class='bloom-highlightSegment' recordingmd5='undefined'>There's nothing ""only"" about a cat!</span></p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
  </body>
</html>";

        [Test]
        public void RemoveObsoleteAudioMarkup_WorksForTextBoxModeRecording()
        {
            _bookDom = new HtmlDom(kTextBoxModeRecordingHtml);
            var book = CreateBook();
            VerifyInitialTextBoxModeMarkup();

            // SUT
            book.RemoveObsoleteAudioMarkup();

            AssertThatXmlIn
                .Dom(_bookDom.RawDom)
                .HasNoMatchForXpath("//div[contains(@class,'audio-sentence')]");
            AssertThatXmlIn
                .Dom(_bookDom.RawDom)
                .HasNoMatchForXpath("//span[@class='bloom-highlightSegment']");
            AssertThatXmlIn.Dom(_bookDom.RawDom).HasNoMatchForXpath("//span[@recordingmd5]");
            AssertThatXmlIn
                .Dom(_bookDom.RawDom)
                .HasNoMatchForXpath("//div[@data-audiorecordingmode]");
            AssertThatXmlIn
                .Dom(_bookDom.RawDom)
                .HasNoMatchForXpath("//div[@data-audiorecordingendtimes]");
            AssertThatXmlIn.Dom(_bookDom.RawDom).HasNoMatchForXpath("//div[@data-duration]");
            // verify that text content hasn't changed with removal of the unneeded spans
            var para =
                _bookDom.RawDom.SelectSingleNode(
                    "//div[contains(@class,'ImageDescriptionEdit-style')]/p"
                ) as XmlElement;
            Assert.That(para, Is.Not.Null);
            Assert.That(para.InnerXml, Is.EqualTo("cat lying down looking at the reader"));
            Assert.That(para.InnerText, Is.EqualTo("cat lying down looking at the reader"));
            var paras = _bookDom.RawDom.SafeSelectNodes("//div[contains(@class,'normal-style')]/p");
            Assert.That(paras, Is.Not.Null);
            Assert.That(paras.Count, Is.EqualTo(2));
            Assert.That(
                paras[0].InnerXml,
                Is.EqualTo("This is a <em>cat</em>.&nbsp;This is <strong>only</strong> a cat.")
            );
            Assert.That(paras[0].InnerText, Is.EqualTo("This is a cat.\u00A0This is only a cat."));
            Assert.That(paras[1].InnerXml, Is.EqualTo("There's nothing \"only\" about a cat!"));
            Assert.That(paras[1].InnerText, Is.EqualTo("There's nothing \"only\" about a cat!"));
        }

        [Test]
        public void RemoveObsoleteAudioMarkup_DoesNotRemoveValidTextBoxModeMarkup()
        {
            _bookDom = new HtmlDom(kTextBoxModeRecordingHtml);
            var book = CreateBook();
            VerifyInitialTextBoxModeMarkup();

            // SUT
            book.RemoveObsoleteAudioMarkup((folder, basename) => true);

            // Note that these asserts are exactly the same as before running the method with the (trivial) function for testing
            // audio file existence.
            VerifyInitialTextBoxModeMarkup();
        }

        private void VerifyInitialTextBoxModeMarkup()
        {
            AssertThatXmlIn
                .Dom(_bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@data-audiorecordingmode]", 2);
            AssertThatXmlIn
                .Dom(_bookDom.RawDom)
                .HasNoMatchForXpath("//span[@class='audio-sentence']");
            AssertThatXmlIn
                .Dom(_bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'audio-sentence')]", 2);
            AssertThatXmlIn
                .Dom(_bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//span[@class='bloom-highlightSegment']", 4);
            AssertThatXmlIn
                .Dom(_bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//span[@recordingmd5]", 3);
            var para =
                _bookDom.RawDom.SelectSingleNode(
                    "//div[contains(@class,'ImageDescriptionEdit-style')]/p"
                ) as XmlElement;
            Assert.That(para, Is.Not.Null);
            Assert.That(
                para.InnerXml,
                Is.EqualTo(
                    "<span id=\"i542eb6e4\" class=\"bloom-highlightSegment\">cat lying down looking at the reader</span>"
                )
            );
            Assert.That(para.InnerText, Is.EqualTo("cat lying down looking at the reader"));
            var paras = _bookDom.RawDom.SafeSelectNodes("//div[contains(@class,'normal-style')]/p");
            Assert.That(paras, Is.Not.Null);
            Assert.That(paras.Count, Is.EqualTo(2));
            Assert.That(
                paras[0].InnerXml,
                Is.EqualTo(
                    "<span id=\"i9c96ab8d\" class=\"bloom-highlightSegment\" recordingmd5=\"undefined\">This is a <em>cat</em>.</span>&nbsp;<span id=\"i7725f2c4\" class=\"bloom-highlightSegment\" recordingmd5=\"undefined\">This is <strong>only</strong> a cat.</span>"
                )
            );
            Assert.That(paras[0].InnerText, Is.EqualTo("This is a cat.\u00A0This is only a cat."));
            Assert.That(
                paras[1].InnerXml,
                Is.EqualTo(
                    "<span id=\"i8d4ddf89\" class=\"bloom-highlightSegment\" recordingmd5=\"undefined\">There's nothing \"only\" about a cat!</span>"
                )
            );
            Assert.That(paras[1].InnerText, Is.EqualTo("There's nothing \"only\" about a cat!"));
        }

        [Test]
        public void SetTopic_TopicContainsSpecialChars_IsEncodedProperly()
        {
            var book = CreateBook();

            book.SetTopic("A & B"); // "&" is a special char in XML

            // Verify "&" becomes encoded in the XML... and that the previous line didn't throw an exception.
            Assert.That(book.RawDom.InnerXml.Contains("A &amp; B"));
        }

        [Test]
        public void SetAndLockBookName_Controls_TitleBestForUserDisplay_MovesBook()
        {
            var book = CreateBookWithPhysicalFile(ThreePageHtml);
            book.SetAndLockBookName("animals");
            var bookName = book.NameBestForUserDisplay;
            Assert.That(bookName, Is.EqualTo("animals"));
            Assert.That(Path.GetFileName(book.FolderPath), Is.EqualTo("animals"));
            book.SetAndLockBookName(null);
            bookName = book.NameBestForUserDisplay;
            Assert.That(bookName, Is.EqualTo("dog"));
            Assert.That(Path.GetFileName(book.FolderPath), Is.EqualTo("dog"));
        }

        [Test]
        public void RemoveXmlMarkup_PlainText_NoChange()
        {
            string input = "Enter\nShift-Enter\nLast Line";
            var result = Bloom.Book.Book.RemoveHtmlMarkup(
                input,
                Bloom.Book.Book.LineBreakSpanConversionMode.ToSpace
            );

            Assert.That(result, Is.EqualTo("Enter\nShift-Enter\nLast Line"));
        }

        [TestCase(Bloom.Book.Book.LineBreakSpanConversionMode.ToSpace)]
        [TestCase(Bloom.Book.Book.LineBreakSpanConversionMode.ToNewline)]
        [TestCase(Bloom.Book.Book.LineBreakSpanConversionMode.ToSimpleNewline)]
        public void RemoveXmlMarkup_LinebreakButNoByteOrderMark_MarkupRemoved(
            Bloom.Book.Book.LineBreakSpanConversionMode conversionMode
        )
        {
            string input =
                "<p>Enter</p> <p>Shift-Enter<span class=\"bloom-linebreak\"></span>Last Line </p>";
            var result = Bloom.Book.Book.RemoveHtmlMarkup(input, conversionMode);

            string replacement = " ";
            switch (conversionMode)
            {
                case Bloom.Book.Book.LineBreakSpanConversionMode.ToNewline:
                    replacement = Environment.NewLine;
                    break;
                case Bloom.Book.Book.LineBreakSpanConversionMode.ToSimpleNewline:
                    replacement = "\n";
                    break;
                default:
                    break;
            }
            Assert.That(result, Is.EqualTo($"Enter Shift-Enter{replacement}Last Line "));
        }

        [Test]
        public void RemoveXmlMarkup_LinebreakAndByteOrderMark_MarkupAndByteOrderMarkRemoved()
        {
            string input =
                "<p>Enter</p> <p>Shift-Enter<span class=\"bloom-linebreak\"></span>\uFEFFLast Line </p>";
            var result = Bloom.Book.Book.RemoveHtmlMarkup(
                input,
                Bloom.Book.Book.LineBreakSpanConversionMode.ToSpace
            );

            Assert.That(result, Is.EqualTo("Enter Shift-Enter Last Line "));
        }

		[Test]
		public void ElementIsInXMatter_AncestorHasBloomFrontMatter_ReturnsTrue()
		{
			XmlDocument doc = new XmlDocument();
			XmlElement body = doc.CreateElement("body");
			XmlElement xmatterDiv = doc.CreateElement("div");
			xmatterDiv.SetAttribute("class", "bloom-frontMatter");
			body.AppendChild(xmatterDiv);
			XmlElement parentDiv = doc.CreateElement("div");
			xmatterDiv.AppendChild(parentDiv);
			XmlElement div = doc.CreateElement("div");
			parentDiv.AppendChild(div);

			Assert.That(Bloom.Book.Book.ElementIsInXMatter(div), Is.True);

			XmlElement innerDiv = doc.CreateElement("div");
			div.AppendChild(innerDiv);

			Assert.That(Bloom.Book.Book.ElementIsInXMatter(innerDiv), Is.True);
		}

		[Test]
		public void ElementIsInXMatter_NoAncestorHasBloomFrontMatter_ReturnsFalse()
		{
			XmlDocument doc = new XmlDocument();
			XmlElement body = doc.CreateElement("body");
			XmlElement nonXmatterDiv = doc.CreateElement("div");
			body.AppendChild(nonXmatterDiv);
			XmlElement parentDiv = doc.CreateElement("div");
			nonXmatterDiv.AppendChild(parentDiv);
			XmlElement div = doc.CreateElement("div");
			parentDiv.AppendChild(div);

			Assert.That(Bloom.Book.Book.ElementIsInXMatter(div), Is.False);

			XmlElement innerDiv = doc.CreateElement("div");
			div.AppendChild(innerDiv);

			Assert.That(Bloom.Book.Book.ElementIsInXMatter(innerDiv), Is.False);
		}

		[Test]
		public void ElementIsInXMatter_ElementIsBody_ReturnsFalse()
		{
			XmlDocument doc = new XmlDocument();
			XmlElement body = doc.CreateElement("body");

			Assert.That(Bloom.Book.Book.ElementIsInXMatter(body), Is.False);
		}
    }
}
