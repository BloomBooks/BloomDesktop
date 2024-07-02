using System;
using System.Text;
using Bloom.Book;
using Bloom.SafeXml;
using NUnit.Framework;

namespace BloomTests.Book
{
    [TestFixture]
    public class PageMigrationTests : BookTestsBase
    {
        [Test]
        public void MigrateTextOnlyShellPage_CopiesTextAndAttrs()
        {
            SetDom(
                @"<div class='bloom-page' data-pagelineage='d31c38d8-c1cb-4eb9-951b-d2840f6a8bdb' id='thePage' data-page-number='3' nonsense='45'>
			   <div class='marginBox'>
					<div aria-describedby='qtip-1' data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement normal-style'>
						<div aria-describedby='qtip-0' data-hasqtip='true' class='bloom-editable normal-style bloom-content1' contenteditable='true' lang='en'>
							There was an old man called Bilanga who was very tall and also not yet married.
						</div>

						<div data-hasqtip='true' class='bloom-editable normal-style' contenteditable='true' lang='pis'>
							Wanfala olman nem blong hem Bilanga barava tol an hem no marit tu.
						</div>
						<div data-hasqtip='true' class='bloom-editable normal-style' contenteditable='true' lang='xyz'>
							Translation into xyz, the primary language.
						</div>
						<div class='bloom-editable' contenteditable='true' lang='z'></div>
					</div>
				</div>
			</div>
			"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            var page = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
            book.BringPageUpToDate(page);

            var newPage = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];

            CheckPageIsCustomizable(newPage);
            CheckPageLineage(
                page,
                newPage,
                "d31c38d8-c1cb-4eb9-951b-d2840f6a8bdb",
                Bloom.Book.Book.JustTextGuid
            );
            CheckEditableText(
                newPage,
                "en",
                "There was an old man called Bilanga who was very tall and also not yet married."
            );
            CheckEditableText(
                newPage,
                "pis",
                "Wanfala olman nem blong hem Bilanga barava tol an hem no marit tu."
            );
            CheckEditableText(newPage, "xyz", "Translation into xyz, the primary language.");
            CheckEditableText(newPage, "z", "");
            Assert.That(
                newPage.SafeSelectNodes("//div[@lang='z' and contains(@class,'bloom-editable')]"),
                Has.Length.EqualTo(1),
                "Failed to remove old child element"
            );
            Assert.That(newPage.GetAttribute("data-page-number"), Is.EqualTo("3"));
            Assert.That(newPage.GetAttribute("nonsense"), Is.EqualTo("45"));
        }

        [Test]
        public void MigrateBasicPageWith2PartLineage_CopiesTextAndImage()
        {
            SetDom(
                @"<div class='bloom-page' data-pagelineage='5dcd48df-e9ab-4a07-afd4-6a24d0398382;426e78a9-34d3-47f1-8355-ae737470bb6e' id='thePage'>
			   <div class='marginBox'>
					<div class='bloom-imageContainer bloom-leadingElement'><img data-license='cc-by-nc-sa' data-copyright='Copyright © 2012, LASI' style='width: 608px; height: 471px; margin-left: 199px; margin-top: 0px;' src='erjwx3bl.q3c.png' alt='This picture, erjwx3bl.q3c.png, is missing or was loading too slowly.' height='471' width='608'></img></div>
					<div aria-describedby='qtip-1' data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement normal-style'>
						<div aria-describedby='qtip-0' data-hasqtip='true' class='bloom-editable normal-style bloom-content1' contenteditable='true' lang='en'>
							There was an old man called Bilanga who was very tall and also not yet married.
						</div>

						<div data-hasqtip='true' class='bloom-editable normal-style' contenteditable='true' lang='pis'>
							Wanfala olman nem blong hem Bilanga barava tol an hem no marit tu.
						</div>
						<div data-hasqtip='true' class='bloom-editable normal-style' contenteditable='true' lang='xyz'>
							Translation into xyz, the primary language.
						</div>
						<div class='bloom-editable' contenteditable='true' lang='z'></div>
					</div>
				</div>
			</div>
			"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            var page = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
            book.BringPageUpToDate(page);

            var newPage = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];

            CheckPageIsCustomizable(newPage);
            CheckPageLineage(
                page,
                newPage,
                "5dcd48df-e9ab-4a07-afd4-6a24d0398382",
                Bloom.Book.Book.BasicTextAndImageGuid
            );
            CheckEditableText(
                newPage,
                "en",
                "There was an old man called Bilanga who was very tall and also not yet married."
            );
            CheckEditableText(
                newPage,
                "pis",
                "Wanfala olman nem blong hem Bilanga barava tol an hem no marit tu."
            );
            CheckEditableText(newPage, "xyz", "Translation into xyz, the primary language.");
            CheckEditableText(newPage, "z", "");
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//img[@data-license='cc-by-nc-sa' and @data-copyright='Copyright © 2012, LASI' and @src='erjwx3bl.q3c.png']",
                    1
                );
            AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//img", 1);
            Assert.That(
                newPage.SafeSelectNodes("//div[@lang='z' and contains(@class,'bloom-editable')]"),
                Has.Length.EqualTo(1),
                "Failed to remove old child element"
            );
        }

        [Test]
        public void MigratePictureInMiddle_CopiesBothTextsAndImage()
        {
            SetDom(
                @"<div class='bloom-page' data-pagelineage='5dcd48df-e9ab-4a07-afd4-6a24d0398383' id='thePage'>
				<div class='marginBox'>
					<div class='bloom-translationGroup bloom-leadingElement normal-style'>
						<div aria-describedby='qtip-0' data-hasqtip='true' class='bloom-editable normal-style' contenteditable='true' lang='en'>
							English in first block
						</div>

						<div data-hasqtip='true' class='bloom-editable normal-style' contenteditable='true' lang='pis'>
							Tok Pisin in first block
						</div>
					</div>
					<div class='bloom-imageContainer bloom-leadingElement'><img data-license='cc-by-nc-sa' data-copyright='Copyright © 2012, LASI' style='width: 608px; height: 471px; margin-left: 199px; margin-top: 0px;' src='erjwx3bl.q3c.png' alt='This picture, erjwx3bl.q3c.png, is missing or was loading too slowly.' height='471' width='608'></img></div>
					<div class='bloom-translationGroup bloom-trailingElement normal-style'>
						<div class='bloom-editable normal-style' contenteditable='true' lang='en'>
							There was an old man called Bilanga who was very tall and also not yet married.
						</div>

						<div class='bloom-editable normal-style' contenteditable='true' lang='pis'>
							Wanfala olman nem blong hem Bilanga barava tol an hem no marit tu.
						</div>
						<div class='bloom-editable normal-style' contenteditable='true' lang='xyz'>
							Translation into xyz, the primary language.
						</div>
						<div class='bloom-editable' contenteditable='true' lang='z'></div>
					</div>
				</div>
			</div>
			"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            var page = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
            book.BringPageUpToDate(page);

            var newPage = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];

            CheckPageIsCustomizable(newPage);
            CheckPageLineage(
                page,
                newPage,
                "5dcd48df-e9ab-4a07-afd4-6a24d0398383",
                "adcd48df-e9ab-4a07-afd4-6a24d0398383"
            );
            CheckEditableText(newPage, "en", "English in first block");
            CheckEditableText(newPage, "pis", "Tok Pisin in first block");
            CheckEditableText(
                newPage,
                "en",
                "There was an old man called Bilanga who was very tall and also not yet married.",
                1
            );
            CheckEditableText(
                newPage,
                "pis",
                "Wanfala olman nem blong hem Bilanga barava tol an hem no marit tu.",
                1
            );
            CheckEditableText(newPage, "xyz", "Translation into xyz, the primary language.", 1);
            CheckEditableText(newPage, "z", "", 1);
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//img[@data-license='cc-by-nc-sa' and @data-copyright='Copyright © 2012, LASI' and @src='erjwx3bl.q3c.png']",
                    1
                );
            AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//img", 1);
            Assert.That(
                newPage.SafeSelectNodes("//div[@lang='z' and contains(@class,'bloom-editable')]"),
                Has.Length.EqualTo(1),
                "Failed to remove old child element"
            );
        }

        [Test]
        public void MigrateJustPicture_CopiesImage()
        {
            SetDom(
                @"<div class='bloom-page' data-pagelineage='5dcd48df-e9ab-4a07-afd4-6a24d0398385' id='thePage'>
			   <div class='marginBox'>
					<div class='bloom-imageContainer bloom-leadingElement'><img data-license='cc-by-nc-sa' data-copyright='Copyright © 2012, LASI' style='width: 608px; height: 471px; margin-left: 199px; margin-top: 0px;' src='erjwx3bl.q3c.png' alt='This picture, erjwx3bl.q3c.png, is missing or was loading too slowly.' height='471' width='608'></img></div>
				</div>
			</div>
			"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            var page = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
            book.BringPageUpToDate(page);

            var newPage = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];

            CheckPageIsCustomizable(newPage);
            CheckPageLineage(
                page,
                newPage,
                "5dcd48df-e9ab-4a07-afd4-6a24d0398385",
                Bloom.Book.Book.JustPictureGuid
            );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//img[@data-license='cc-by-nc-sa' and @data-copyright='Copyright © 2012, LASI' and @src='erjwx3bl.q3c.png']",
                    1
                );
            AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//img", 1);
        }

        [Test]
        public void MigrateUnknownPage_DoesNothing()
        {
            SetDom(
                @"<div class='bloom-page' data-pagelineage='5dcd48df-e9ab-4b07-afd4-6a24d0398382' id='thePage'>
			   <div class='marginBox'>
					<div class='bloom-imageContainer bloom-leadingElement'><img data-license='cc-by-nc-sa' data-copyright='Copyright © 2012, LASI' style='width: 608px; height: 471px; margin-left: 199px; margin-top: 0px;' src='erjwx3bl.q3c.png' alt='This picture, erjwx3bl.q3c.png, is missing or was loading too slowly.' height='471' width='608'></img></div>
					<div aria-describedby='qtip-1' data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement normal-style'>
						<div aria-describedby='qtip-0' data-hasqtip='true' class='bloom-editable normal-style bloom-content1' contenteditable='true' lang='en'>
							There was an old man called Bilanga who was very tall and also not yet married.
						</div>
					</div>
				</div>
			</div>
			"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            var page = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
            var oldContent = page.OuterXml;
            book.BringPageUpToDate(page);

            var newPage = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
            Assert.That(newPage.OuterXml, Is.EqualTo(oldContent), "should not have modified page");
            Assert.That(newPage, Is.EqualTo(page), "should not have copied, just kept");
        }

        [Test]
        public void MigratePageWithoutLineage_DoesNothing()
        {
            SetDom(
                @"<div class='bloom-page' id='thePage'>
			   <div class='marginBox'>
					<div class='bloom-imageContainer bloom-leadingElement'><img data-license='cc-by-nc-sa' data-copyright='Copyright © 2012, LASI' style='width: 608px; height: 471px; margin-left: 199px; margin-top: 0px;' src='erjwx3bl.q3c.png' alt='This picture, erjwx3bl.q3c.png, is missing or was loading too slowly.' height='471' width='608'></img></div>
					<div aria-describedby='qtip-1' data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement normal-style'>
						<div aria-describedby='qtip-0' data-hasqtip='true' class='bloom-editable normal-style bloom-content1' contenteditable='true' lang='en'>
							There was an old man called Bilanga who was very tall and also not yet married.
						</div>
					</div>
				</div>
			</div>
			"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            var page = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
            var oldContent = page.OuterXml;
            book.BringPageUpToDate(page);

            var newPage = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
            Assert.That(newPage.OuterXml, Is.EqualTo(oldContent), "should not have modified page");
            Assert.That(newPage, Is.EqualTo(page), "should not have copied, just kept");
        }

        [Test]
        public void AddBigWordsStyleIfUsedAndNoUserStylesElement()
        {
            var dom = CreateAndMigrateBigWordsPage(null); // will produce the default empty userModifiedStyles element (and coverColor element)
            AssertThatXmlIn
                .Dom(dom)
                .HasAtLeastOneMatchForXpath(
                    "html/head/style[@type='text/css' and @title='userModifiedStyles' and text()='.BigWords-style { font-size: 45pt !important; text-align: center !important; }']"
                );
        }

        [Test]
        public void DontChangeBigWordsStyleIfUsedAndPresent()
        {
            var dom = CreateAndMigrateBigWordsPage(headElt =>
            {
                var userStyles = HtmlDom.GetUserModifiedStyleElement(headElt); // there will be an empty element
                userStyles.InnerText =
                    ".BigWords-style { font-size: 50pt !important; text-align: center !important; }";
            });

            AssertThatXmlIn
                .Dom(dom)
                .HasAtLeastOneMatchForXpath(
                    "html/head/style[@type='text/css' and @title='userModifiedStyles' and text()='.BigWords-style { font-size: 50pt !important; text-align: center !important; }']"
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasNoMatchForXpath(
                    "html/head/style[@type='text/css' and @title='userModifiedStyles' and text()='.BigWords-style { font-size: 45pt !important; text-align: center !important; }']"
                );
        }

        [Test]
        public void AddBigWordsStyleIfNeededAndMissingFromStylesheet()
        {
            var dom = CreateAndMigrateBigWordsPage(headElt =>
            {
                var userStyles = HtmlDom.GetUserModifiedStyleElement(headElt); // there will be an empty element
                userStyles.InnerText = ".OtherWords-style { font-size: 50pt}";
            });

            AssertThatXmlIn
                .Dom(dom)
                .HasAtLeastOneMatchForXpath(
                    "html/head/style[@type='text/css' and @title='userModifiedStyles' and text()='.OtherWords-style { font-size: 50pt} .BigWords-style { font-size: 45pt !important; text-align: center !important; }']"
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasNoMatchForXpath(
                    "html/head/style[@type='text/css' and @title='userModifiedStyles' and text()='.BigWords-style { font-size: 45pt !important; text-align: center !important; }']"
                );
        }

        //regression: BL-2782
        [Test]
        public void BringPageUpToDateWithMigration_WasA4Landscape_StaysA4Landscape()
        {
            SetDom(
                @"<div class='bloom-page A4Landscape' data-pagelineage='5dcd48df-e9ab-4a07-afd4-6a24d0398385' id='thePage'>
			   <div class='marginBox'>
					<div class='bloom-imageContainer bloom-leadingElement'><img src='erjwx3bl.q3c.png'></img></div>
				</div>
			</div>
			"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            var page = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
            book.BringPageUpToDate(page);

            var updatedPage = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
            Assert.IsTrue(
                updatedPage.OuterXml.Contains("A4Landscape"),
                "the old page was in A4Landscape, so the migrated page should be, too."
            );
            Assert.IsFalse(
                updatedPage.OuterXml.Contains("A5Portrait"),
                "the old page was in A4Landscape, so the migrated page should not have some other size/orientation."
            );
        }

        [Test]
        public void BringPageUpToDateWithMigration_PageHasClassWeDidNotThinkAbout_ClassIsRetained()
        {
            SetDom(
                @"<div class='bloom-page A4Landscape foobar' data-pagelineage='5dcd48df-e9ab-4a07-afd4-6a24d0398385' id='thePage'>
			   <div class='marginBox'>
					<div class='bloom-imageContainer bloom-leadingElement'><img src='erjwx3bl.q3c.png'></img></div>
				</div>
			</div>
			"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            var page = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
            book.BringPageUpToDate(page);

            var updatedPage = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
            Assert.IsTrue(
                updatedPage.OuterXml.Contains("foobar"),
                "foobar, a class in the old page, should be added to the newly constructed page"
            );
        }

        [Test]
        public void BringPageUpToDateWithMigration_ClassInNewTemplatePage_ClassIsRetained()
        {
            SetDom(
                @"<div class='foobar' data-pagelineage='5dcd48df-e9ab-4a07-afd4-6a24d0398385' id='thePage'>
			   <div class='marginBox'>
					<div class='bloom-imageContainer bloom-leadingElement'><img src='erjwx3bl.q3c.png'></img></div>
				</div>
			</div>
			"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            var page = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
            book.BringPageUpToDate(page);

            var updatedPage = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
            Assert.IsTrue(
                updatedPage.OuterXml.Contains("bloom-page"),
                "we expect that the new template page will have this class, which we've omitted from the old page"
            );
        }

        [Test]
        public void BringPageUpToDateWithMigration_OldPageHadBasicBookClassName_ClassIsRemoved()
        {
            SetDom(
                @"<div class='foobar imageOnTop' data-pagelineage='5dcd48df-e9ab-4a07-afd4-6a24d0398385' id='thePage'>
			   <div class='marginBox'>
					<div class='bloom-imageContainer bloom-leadingElement'><img src='erjwx3bl.q3c.png'></img></div>
				</div>
			</div>
			"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            var page = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
            book.BringPageUpToDate(page);

            var updatedPage = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
            Assert.IsFalse(
                updatedPage.OuterXml.Contains("imageOnTop"),
                "imageOnTop refers to the old fixed-stylesheet way of showing pages"
            );
        }

        /// <summary>
        /// this is a regression test, from BL-2887
        /// </summary>
        [Test]
        public void BringPageUpToDateWithMigration_SourceHasNoDataPage_DoesNotAcquireDataPageFromTemplate()
        {
            //the  data-pagelineage='FD115DFF-0415-4444-8E76-3D2A18DBBD27' here tells us we came from
            //an old template page and triggers migration to its current equivalent
            SetDom(
                @"<div class='imageOnTop'  data-pagelineage='FD115DFF-0415-4444-8E76-3D2A18DBBD27' id='thePage'>
			   <div class='marginBox'>
					<div class='bloom-imageContainer bloom-leadingElement'><img src='erjwx3bl.q3c.png'></img></div>
				</div>
			</div>
			"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            var page = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
            book.BringPageUpToDate(page);
            //The source template page has data-page='extra', but the migrated page *must not* have this.
            AssertThatXmlIn
                .Dom(dom)
                .HasNoMatchForXpath("//div[@id='thePage' and @data-page='extra']");
            AssertThatXmlIn.Dom(dom).HasNoMatchForXpath("//div[@id='thePage' and @data-page]");
        }

        /// <summary>
        /// this is a regression test, from BL-2887
        /// </summary>
        [Test]
        public void BringPageUpToDateWithMigration_SourceHasEmptyDataPage_DoesNotAcquireDataPageFromTemplate()
        {
            //the  data-pagelineage='FD115DFF-0415-4444-8E76-3D2A18DBBD27' here tells us we came from
            //an old template page and triggers migration to its current equivalent
            SetDom(
                @"<div class='imageOnTop'  data-page='' data-pagelineage='FD115DFF-0415-4444-8E76-3D2A18DBBD27' id='thePage'>
			   <div class='marginBox'>
					<div class='bloom-imageContainer bloom-leadingElement'><img src='erjwx3bl.q3c.png'></img></div>
				</div>
			</div>
			"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            var page = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
            book.BringPageUpToDate(page);
            //The source template page has data-page='extra', but the migrated page *must not* have this.
            AssertThatXmlIn
                .Dom(dom)
                .HasNoMatchForXpath("//div[@id='thePage' and @data-page='extra']");
            AssertThatXmlIn.Dom(dom).HasNoMatchForXpath("//div[@id='thePage' and @data-page]");
        }

        // Common code for tests of adding needed styles. The main difference between the tests is the state of the stylesheet
        // (if any) inserted by the modifyHead action.
        private SafeXmlDocument CreateAndMigrateBigWordsPage(Action<SafeXmlElement> modifyHead)
        {
            SetDom(
                @"<div class='bloom-page' data-pagelineage='FD115DFF-0415-4444-8E76-3D2A18DBBD27' id='thePage'>
			   <div class='marginBox'>
					<div class='bloom-imageContainer bloom-leadingElement'><img data-license='cc-by-nc-sa' data-copyright='Copyright © 2012, LASI' style='width: 608px; height: 471px; margin-left: 199px; margin-top: 0px;' src='erjwx3bl.q3c.png' alt='This picture, erjwx3bl.q3c.png, is missing or was loading too slowly.' height='471' width='608'></img></div>
					<div aria-describedby='qtip-1' data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement normal-style'>
						<div aria-describedby='qtip-0' data-hasqtip='true' class='bloom-editable BigWords-style bloom-content1' contenteditable='true' lang='en'>
							There was an old man called Bilanga who was very tall and also not yet married.
						</div>
					</div>
				</div>
			</div>
			"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            if (modifyHead != null)
            {
                modifyHead((SafeXmlElement)dom.DocumentElement.ChildNodes[0]);
            }
            var page = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
            book.BringPageUpToDate(page);

            var newPage = (SafeXmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];

            CheckPageIsCustomizable(newPage);
            return dom;
        }

        [Test]
        public void UpdatePageToTemplate_UpdatesPage()
        {
            // Makes a book with two pages. The second (with one image and one translationGroup) is the one we will update to the template.
            SetDom(
                @"<div class='bloom-page DeviceLandscape' data-pagelineage='FD115DFF-0415-4444-8E76-3D2A18DBBD27' id='prevPage'>
			   <div class='marginBox'>
					<div class='bloom-imageContainer bloom-leadingElement'><img data-license='cc-by-nc' data-copyright='Copyright © 2012, LASI' style='width: 608px; height: 471px; margin-left: 199px; margin-top: 0px;' src='erjwx3bl.q3c.png' alt='This picture, erjwx3bl.q3c.png, is missing or was loading too slowly.' height='471' width='608'></img></div>
					<div aria-describedby='qtip-1' data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement normal-style'>
						<div aria-describedby='qtip-0' data-hasqtip='true' class='bloom-editable BigWords-style bloom-content1' contenteditable='true' lang='en'>
							Different text in first para.
						</div>
					</div>
				</div>
			</div>
<div class='DeviceLandscape bloom-page side-left' data-pagelineage='FD115DFF-0415-4444-8E76-3D2A18DBBD27' id='thePage'>
			   <div class='marginBox'>
					<div class='bloom-imageContainer bloom-leadingElement'><img data-license='cc-by-nc-sa' data-copyright='Copyright © 2012, LASI' style='width: 608px; height: 471px; margin-left: 199px; margin-top: 0px;' src='erjwx3bl.q3c.png' alt='This picture, erjwx3bl.q3c.png, is missing or was loading too slowly.' height='471' width='608'></img></div>
					<div aria-describedby='qtip-1' data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement normal-style'>
						<div aria-describedby='qtip-0' data-hasqtip='true' class='bloom-editable BigWords-style bloom-content1' contenteditable='true' lang='en'>
							There was an old man called Bilanga who was very tall and also not yet married.
						</div>
						<div aria-describedby='qtip-0' data-hasqtip='true' class='bloom-editable bloom-content1' contenteditable='true' lang='fr'>
							Some french
						</div>
					</div>
				</div>
			</div>
			"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            // This is the template to which we will update the second page.  It has two translation groups, though only the first is needed to hold the
            // content from the original page.
            var newPageDom = MakeDom(
                (
                    @"<div class='A5Portrait bloom-page numberedPage customPage bloom-combinedPage' data-page='extra' id='newTemplate'>
	  <div lang='en' class='pageLabel'>Picture in Middle</div>
	  <div lang='en' class='pageDescription'></div>
	  <div class='marginBox'>
		<div class='split-pane horizontal-percent'>
		  <div style='bottom: 76%' class='split-pane-component position-top'>
			<div class='split-pane-component-inner'>
			  <div class='bloom-translationGroup bloom-trailingElement normal-style'>
				<div lang='z' contenteditable='true' class='bloom-content1 bloom-editable FancyNew-style'>
				</div>
			  </div>
			</div>
		  </div>
		  <div style='bottom: 76%' class='split-pane-divider horizontal-divider'></div>
		  <!--NB: this split percent has to be the same as that used for upper!!!!!!-->
		  <div style='height: 76%' class='split-pane-component position-bottom'>
			<div class='split-pane-component-inner'>
			  <div class='split-pane horizontal-percent'>
				<div style='bottom: 30%' class='split-pane-component position-top'>
				  <div class='split-pane-component-inner'>
					<div class='bloom-imageContainer bloom-leadingElement'><img src='placeHolder.png' alt='Could not load the picture'/>
					</div>
				  </div>
				</div>
				<div style='bottom: 30%' class='split-pane-divider horizontal-divider'></div>
				<!--NB: this split percent has to be the same as that used for upper!!!!!!-->
				<div style='height: 30%' class='split-pane-component position-bottom'>
				  <div class='split-pane-component-inner'>
					<div class='bloom-translationGroup bloom-trailingElement normal-style'>
					  <div lang='z' contenteditable='true' class='bloom-content1 bloom-editable'>
					  </div>
					</div>
				  </div>
				</div>
				<div class='split-pane-resize-shim'></div>
			  </div>
			</div>
		  </div>
		  <div class='split-pane-resize-shim'></div>
		</div>
	  </div>
	</div>"
                )
            );
            var template = (SafeXmlElement)newPageDom.SafeSelectNodes("//div[@id='newTemplate']")[0];
            var templatePage = new Page(
                book,
                template,
                "dummy",
                "id",
                x =>
                {
                    return template;
                }
            );
            book.UpdatePageToTemplate(book.OurHtmlDom, templatePage, "thePage");

            var newPage = (SafeXmlElement)dom.SafeSelectNodes(".//div[@id='thePage']")[0];
            Assert.That(
                newPage.GetAttribute("class"),
                Is.EqualTo(
                    "DeviceLandscape bloom-page numberedPage customPage bloom-combinedPage side-left bloom-monolingual"
                )
            );
            Assert.That(newPage.GetAttribute("data-pagelineage"), Is.EqualTo("newTemplate"));
            // We kept the image
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    ".//img[@data-license='cc-by-nc-sa' and @data-copyright='Copyright © 2012, LASI' and @src='erjwx3bl.q3c.png']",
                    1
                ); // the one in the first page has slightly different attrs
            CheckEditableText(
                newPage,
                "en",
                "There was an old man called Bilanga who was very tall and also not yet married."
            );
            CheckEditableText(newPage, "fr", "Some french");
            // We should have kept the second one in the new page even though we didn't put anything in it (and there is one in the first page, too).
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    ".//div[contains(@class, 'bloom-translationGroup')]",
                    3
                );
            // The English and French should both have ended up with this, also the inserted empty div for xyz.
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    ".//div[contains(@class, 'bloom-editable') and contains(@class, ' FancyNew-style')]",
                    3
                );
        }

        [TestCase("A5Landscape", "A5Portrait", "A5Landscape")]
        [TestCase("DeviceLandscape", "A5Portrait bloom-page", "DeviceLandscape bloom-page")]
        [TestCase(
            "Device9x16Portrait bloom-page",
            "somethingElse A5Landscape bloom-page another-style",
            "somethingElse Device9x16Portrait bloom-page another-style"
        )]
        [TestCase("A5Landscape bloom-page somethingElse", "bloom-page", "bloom-page A5Landscape")]
        public void TransferOrientation(
            string oldClasses,
            string newClasses,
            string expectedClasses
        )
        {
            Assert.That(
                HtmlDom.TransferOrientation(oldClasses, newClasses),
                Is.EqualTo(expectedClasses)
            );
        }

        [Test]
        public void UpdatePageToTemplate_CopiesTwoTranslationGroupsAndImages()
        {
            var book = SetupBookAndUpdatePage(
                MakeBloomImageGroup("blah.png", 423, 567)
                    + MakeBloomTranslationGroup(
                        "ocean-style",
                        new DivContent("The old man and the sea", "en"),
                        new DivContent("El viejo y el mar", "es")
                    )
                    + MakeBloomTranslationGroup(
                        "run-style",
                        new DivContent("Run very fast", "en"),
                        new DivContent("Corre muy rápido", "es")
                    )
                    + MakeBloomImageGroup("something.png", 95, 107),
                MakeBloomImageGroup("placeHolder.png", 23, 67)
                    + MakeBloomImageGroup("placeHolder.png", 195, 207)
                    + MakeBloomTranslationGroup("big-style", new DivContent("", "z"))
                    + MakeBloomTranslationGroup("small-style", new DivContent("", "z"))
            );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    ".//div[contains(@class, 'bloom-translationGroup')]",
                    2
                );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    ".//div[contains(@class, 'bloom-imageContainer')]",
                    2
                );
            AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath(".//img", 2);
            VerifyImage(book, 0, "blah.png", 423, 567); // Review: I'm a bit surprised it keeps the old sizes
            VerifyImage(book, 1, "something.png", 95, 107);
            VerifyTranslationGroup(
                book,
                0,
                "big-style",
                new DivContent("The old man and the sea", "en"),
                new DivContent("El viejo y el mar", "es")
            );
            VerifyTranslationGroup(
                book,
                1,
                "small-style",
                new DivContent("Run very fast", "en"),
                new DivContent("Corre muy rápido", "es")
            );
        }

        [Test]
        public void UpdatePageToTemplate_CopiesTwoTranslationGroups_IgnoringHiddenOnes()
        {
            var book = SetupBookAndUpdatePage(
                MakeBloomTranslationGroup(
                    "ocean-style",
                    new DivContent("The old man and the sea", "en"),
                    new DivContent("El viejo y el mar", "es")
                )
                    + MakeBloomTranslationGroup(
                        "run-style",
                        new DivContent("Run very fast", "en"),
                        new DivContent("Corre muy rápido", "es")
                    ),
                MakeBloomTranslationGroup("big-style", new DivContent("", "z"))
                    // This is the critcal aspect of this test...an invisible div that must NOT receive the stuff that is visible.
                    + MakeHiddenTranslationGroup()
                    + MakeBloomTranslationGroup("small-style", new DivContent("", "z"))
            );
            AssertThatXmlIn
                .Dom(book.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    ".//div[contains(@class, 'bloom-translationGroup')]",
                    3
                );
            VerifyTranslationGroup(
                book,
                0,
                "big-style",
                new DivContent("The old man and the sea", "en"),
                new DivContent("El viejo y el mar", "es")
            );
            // The '2' is critical here, this content needs to have been moved to the THIRD bloom-translationGroup
            // (skipping the hidden one).
            VerifyTranslationGroup(
                book,
                2,
                "small-style",
                new DivContent("Run very fast", "en"),
                new DivContent("Corre muy rápido", "es")
            );
        }

        private void VerifyTranslationGroup(
            Bloom.Book.Book book,
            int index,
            string style,
            params DivContent[] content
        )
        {
            var group = book.RawDom.SafeSelectNodes(
                ".//div[contains(@class, 'bloom-translationGroup')]"
            )[index];
            var editDivs = group.SafeSelectNodes("./div[contains(@class, 'bloom-editable')]");
            Assert.That(editDivs.Length, Is.GreaterThanOrEqualTo(content.Length)); // may keep 'z'?
            foreach (var item in content)
            {
                CheckEditDiv(style, editDivs, item);
            }
        }

        private static void CheckEditDiv(string style, SafeXmlNode[] editDivs, DivContent item)
        {
            foreach (SafeXmlElement div in editDivs)
            {
                if (div.GetAttribute("lang") != item.Lang)
                    continue;
                Assert.That(div.InnerText.Trim(), Is.EqualTo(item.Content.Trim()));
                Assert.That(div.GetAttribute("class"), Does.Contain(style));
                return;
            }
            Assert.Fail("no matching div found for " + item.Lang);
        }

        private void VerifyImage(
            Bloom.Book.Book book,
            int index,
            string imageName,
            int width,
            int height
        )
        {
            var div = book.RawDom.SafeSelectNodes(".//div[contains(@class, 'bloom-imageContainer')]")[
                index
            ];
            var images = div.SafeSelectNodes("img");
            Assert.That(
                images,
                Has.Length.EqualTo(1),
                "should only be one image in an image container"
            );
            var img = (SafeXmlElement)images[0];
            Assert.That(img.GetAttribute("src"), Is.EqualTo(imageName));
            Assert.That(img.GetAttribute("alt"), Does.Contain(imageName));
            Assert.That(img.GetAttribute("width"), Is.EqualTo(width.ToString()));
            Assert.That(img.GetAttribute("height"), Is.EqualTo(height.ToString()));
        }

        class DivContent
        {
            public string Content;
            public string Lang;

            public DivContent(string content, string lang)
            {
                Content = content;
                Lang = lang;
            }
        }

        string MakeBloomImageGroup(string name, int width, int height)
        {
            return @"<div class='bloom-imageContainer bloom-leadingElement'><img data-license='cc-by-nc-sa' data-copyright='Copyright © 2012, LASI' style='width: "
                + width
                + @"px; height: "
                + height
                + @"px; margin-left: 199px; margin-top: 0px;' src='"
                + name
                + @"' alt='This picture, "
                + name
                + @", is missing or was loading too slowly.' height='"
                + height
                + @"' width='"
                + width
                + @"'></img></div>";
        }

        string MakeHiddenTranslationGroup()
        {
            return @"<div class='box-header-off bloom-translationGroup'></div>";
        }

        string MakeBloomTranslationGroup(string style, params DivContent[] contentDivs)
        {
            var sb = new StringBuilder(
                @"<div aria-describedby='qtip-1' data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement "
                    + style
                    + @"'>"
            );
            foreach (var item in contentDivs)
                sb.AppendLine(MakeBloomEditableDiv(item, style));
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        string MakeBloomEditableDiv(DivContent content, string style)
        {
            return @"						<div  class='bloom-editable "
                + style
                + @" bloom-content1' contenteditable='true' lang='"
                + content.Lang
                + @"'>
							"
                + content.Content
                + @"
						</div>
";
        }

        Bloom.Book.Book SetupBookAndUpdatePage(string sourceContent, string templateContent)
        {
            SetDom(
                @"<div class='bloom-page' data-pagelineage='FD115DFF-0415-4444-8E76-3D2A18DBBD27' id='thePage'>
			   <div class='marginBox'>"
                    + sourceContent
                    + @"</div>
			</div>
			"
            );
            var book = CreateBook();
            var dom = book.RawDom;
            // This is the template to which we will update the page.
            var newPageDom = MakeDom(
                (
                    @"<div class='A5Portrait bloom-page numberedPage customPage bloom-combinedPage' data-page='extra' id='newTemplate'>
	  <div lang='en' class='pageLabel'>Picture in Middle</div>
	  <div lang='en' class='pageDescription'></div>
	  <div class='marginBox'>
		"
                    + templateContent
                    + @"
	  </div>
	</div>"
                )
            );
            var template = (SafeXmlElement)newPageDom.SafeSelectNodes("//div[@id='newTemplate']")[0];
            var templatePage = new Page(
                book,
                template,
                "dummy",
                "id",
                x =>
                {
                    return template;
                }
            );
            book.UpdatePageToTemplate(book.OurHtmlDom, templatePage, "thePage");
            return book;
        }

        // Enhance: if there are ever cases where there are multiple image containers to migrate, test this.
        // Enhance: if there are ever cases where it is possible not to have exactly corresponding parent elements (e.g., migrating a page with
        // one translation group to one with two), test this.
        // The current intended behavior is to copy the corresponding ones, leave additional destination elements unchanged, and discard
        // additional source ones. Some way to warn the user in the latter case might be wanted. Or, we may want a way to specify which
        // source maps to which destination.

        // some attempt at verifying that it updated the page structure
        private void CheckPageIsCustomizable(SafeXmlElement newPage)
        {
            Assert.That(newPage.GetAttribute("class"), Does.Contain("customPage"));
        }

        private void CheckPageLineage(
            SafeXmlElement oldPage,
            SafeXmlElement newPage,
            string oldGuid,
            string newGuid
        )
        {
            var oldLineage = oldPage.GetAttribute("data-pagelineage");
            var newLineage = newPage.GetAttribute("data-pagelineage");
            Assert.That(newLineage, Is.EqualTo(oldLineage.Replace(oldGuid, newGuid)));
        }

        private void CheckEditableText(
            SafeXmlElement page,
            string lang,
            string text,
            int groupIndex = 0
        )
        {
            var transGroup = (SafeXmlElement)
                page.SafeSelectNodes(".//div[contains(@class,'bloom-translationGroup')]")[
                    groupIndex
                ];
            var editDiv = (SafeXmlElement)
                transGroup.SafeSelectNodes(
                    "div[@lang='" + lang + "' and contains(@class,'bloom-editable')]"
                )[0];
            var actualText = editDiv.InnerXml;
            Assert.That(actualText.Trim(), Is.EqualTo(text));
        }
    }
}
