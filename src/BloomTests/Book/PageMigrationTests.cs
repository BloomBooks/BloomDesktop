using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using NUnit.Framework;
using Palaso.Xml;

namespace BloomTests.Book
{
	[TestFixture]
	public class PageMigrationTests : BookTestsBase
	{
		[Test]
		public void MigrateTextOnlyShellPage_CopiesText()
		{
			SetDom(@"<div class='bloom-page' data-pagelineage='d31c38d8-c1cb-4eb9-951b-d2840f6a8bdb' id='thePage'>
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
			");
			var book = CreateBook();
			var dom = book.RawDom;
			var page = (XmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
			book.BringPageUpToDate(page);

			var newPage = (XmlElement) dom.SafeSelectNodes("//div[@id='thePage']")[0];

			CheckPageIsCustomizable(newPage);
			CheckPageLineage(page, newPage, "d31c38d8-c1cb-4eb9-951b-d2840f6a8bdb", "a31c38d8-c1cb-4eb9-951b-d2840f6a8bdb");
			CheckEditableText(newPage, "en", "There was an old man called Bilanga who was very tall and also not yet married.");
			CheckEditableText(newPage, "pis", "Wanfala olman nem blong hem Bilanga barava tol an hem no marit tu.");
			CheckEditableText(newPage, "xyz", "Translation into xyz, the primary language.");
			CheckEditableText(newPage, "z", "");
			Assert.That(newPage.SafeSelectNodes("//div[@lang='z' and contains(@class,'bloom-editable')]"), Has.Count.EqualTo(1), "Failed to remove old child element");
		}

		[Test]
		public void MigrateBasicPageWith2PartLineage_CopiesTextAndImage()
		{
			SetDom(@"<div class='bloom-page' data-pagelineage='5dcd48df-e9ab-4a07-afd4-6a24d0398382;426e78a9-34d3-47f1-8355-ae737470bb6e' id='thePage'>
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
			");
			var book = CreateBook();
			var dom = book.RawDom;
			var page = (XmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
			book.BringPageUpToDate(page);

			var newPage = (XmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];

			CheckPageIsCustomizable(newPage);
			CheckPageLineage(page, newPage, "5dcd48df-e9ab-4a07-afd4-6a24d0398382", "adcd48df-e9ab-4a07-afd4-6a24d0398382");
			CheckEditableText(newPage, "en", "There was an old man called Bilanga who was very tall and also not yet married.");
			CheckEditableText(newPage, "pis", "Wanfala olman nem blong hem Bilanga barava tol an hem no marit tu.");
			CheckEditableText(newPage, "xyz", "Translation into xyz, the primary language.");
			CheckEditableText(newPage, "z", "");
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//img[@data-license='cc-by-nc-sa' and @data-copyright='Copyright © 2012, LASI' and @src='erjwx3bl.q3c.png']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//img", 1);
			Assert.That(newPage.SafeSelectNodes("//div[@lang='z' and contains(@class,'bloom-editable')]"), Has.Count.EqualTo(1), "Failed to remove old child element");
		}

		[Test]
		public void MigratePictureInMiddle_CopiesBothTextsAndImage()
		{
			SetDom(@"<div class='bloom-page' data-pagelineage='5dcd48df-e9ab-4a07-afd4-6a24d0398383' id='thePage'>
			   <div class='marginBox'>
					<div aria-describedby='qtip-1' data-hasqtip='true' class='bloom-translationGroup bloom-leadingElement normal-style'>
						<div aria-describedby='qtip-0' data-hasqtip='true' class='bloom-editable normal-style bloom-content1' contenteditable='true' lang='en'>
							English in first block
						</div>

						<div data-hasqtip='true' class='bloom-editable normal-style' contenteditable='true' lang='pis'>
							Tok Pisin in first block
						</div>
					</div>
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
			");
			var book = CreateBook();
			var dom = book.RawDom;
			var page = (XmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
			book.BringPageUpToDate(page);

			var newPage = (XmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];

			CheckPageIsCustomizable(newPage);
			CheckPageLineage(page, newPage, "5dcd48df-e9ab-4a07-afd4-6a24d0398383", "adcd48df-e9ab-4a07-afd4-6a24d0398383");
			CheckEditableText(newPage, "en", "English in first block");
			CheckEditableText(newPage, "pis", "Tok Pisin in first block");
			CheckEditableText(newPage, "en", "There was an old man called Bilanga who was very tall and also not yet married.", 1);
			CheckEditableText(newPage, "pis", "Wanfala olman nem blong hem Bilanga barava tol an hem no marit tu.",1);
			CheckEditableText(newPage, "xyz", "Translation into xyz, the primary language.",1);
			CheckEditableText(newPage, "z", "",1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//img[@data-license='cc-by-nc-sa' and @data-copyright='Copyright © 2012, LASI' and @src='erjwx3bl.q3c.png']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//img", 1);
			Assert.That(newPage.SafeSelectNodes("//div[@lang='z' and contains(@class,'bloom-editable')]"), Has.Count.EqualTo(1), "Failed to remove old child element");
		}

		[Test]
		public void MigrateJustPicture_CopiesImage()
		{
			SetDom(@"<div class='bloom-page' data-pagelineage='5dcd48df-e9ab-4a07-afd4-6a24d0398385' id='thePage'>
			   <div class='marginBox'>
					<div class='bloom-imageContainer bloom-leadingElement'><img data-license='cc-by-nc-sa' data-copyright='Copyright © 2012, LASI' style='width: 608px; height: 471px; margin-left: 199px; margin-top: 0px;' src='erjwx3bl.q3c.png' alt='This picture, erjwx3bl.q3c.png, is missing or was loading too slowly.' height='471' width='608'></img></div>
				</div>
			</div>
			");
			var book = CreateBook();
			var dom = book.RawDom;
			var page = (XmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
			book.BringPageUpToDate(page);

			var newPage = (XmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];

			CheckPageIsCustomizable(newPage);
			CheckPageLineage(page, newPage, "5dcd48df-e9ab-4a07-afd4-6a24d0398385", "adcd48df-e9ab-4a07-afd4-6a24d0398385");
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//img[@data-license='cc-by-nc-sa' and @data-copyright='Copyright © 2012, LASI' and @src='erjwx3bl.q3c.png']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//img", 1);
		}


		[Test]
		public void MigrateUnknownPage_DoesNothing()
		{
			SetDom(@"<div class='bloom-page' data-pagelineage='5dcd48df-e9ab-4b07-afd4-6a24d0398382' id='thePage'>
			   <div class='marginBox'>
					<div class='bloom-imageContainer bloom-leadingElement'><img data-license='cc-by-nc-sa' data-copyright='Copyright © 2012, LASI' style='width: 608px; height: 471px; margin-left: 199px; margin-top: 0px;' src='erjwx3bl.q3c.png' alt='This picture, erjwx3bl.q3c.png, is missing or was loading too slowly.' height='471' width='608'></img></div>
					<div aria-describedby='qtip-1' data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement normal-style'>
						<div aria-describedby='qtip-0' data-hasqtip='true' class='bloom-editable normal-style bloom-content1' contenteditable='true' lang='en'>
							There was an old man called Bilanga who was very tall and also not yet married.
						</div>
					</div>
				</div>
			</div>
			");
			var book = CreateBook();
			var dom = book.RawDom;
			var page = (XmlElement) dom.SafeSelectNodes("//div[@id='thePage']")[0];
			var oldContent = page.OuterXml;
			book.BringPageUpToDate(page);

			var newPage = (XmlElement) dom.SafeSelectNodes("//div[@id='thePage']")[0];
			Assert.That(newPage.OuterXml, Is.EqualTo(oldContent), "should not have modified page");
			Assert.That(newPage, Is.EqualTo(page), "should not have copied, just kept");
		}

		[Test]
		public void MigratePageWithoutLineage_DoesNothing()
		{
			SetDom(@"<div class='bloom-page' id='thePage'>
			   <div class='marginBox'>
					<div class='bloom-imageContainer bloom-leadingElement'><img data-license='cc-by-nc-sa' data-copyright='Copyright © 2012, LASI' style='width: 608px; height: 471px; margin-left: 199px; margin-top: 0px;' src='erjwx3bl.q3c.png' alt='This picture, erjwx3bl.q3c.png, is missing or was loading too slowly.' height='471' width='608'></img></div>
					<div aria-describedby='qtip-1' data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement normal-style'>
						<div aria-describedby='qtip-0' data-hasqtip='true' class='bloom-editable normal-style bloom-content1' contenteditable='true' lang='en'>
							There was an old man called Bilanga who was very tall and also not yet married.
						</div>
					</div>
				</div>
			</div>
			");
			var book = CreateBook();
			var dom = book.RawDom;
			var page = (XmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
			var oldContent = page.OuterXml;
			book.BringPageUpToDate(page);

			var newPage = (XmlElement)dom.SafeSelectNodes("//div[@id='thePage']")[0];
			Assert.That(newPage.OuterXml, Is.EqualTo(oldContent), "should not have modified page");
			Assert.That(newPage, Is.EqualTo(page), "should not have copied, just kept");
		}

		[Test]
		public void AddBigWordsStyleIfUsedAndNoUserStylesElement()
		{
			var dom = CreateAndMigrateBigWordsPage(headElt => { });
			AssertThatXmlIn.Dom(dom).HasAtLeastOneMatchForXpath("html/head/style[@type='text/css' and @title='userModifiedStyles' and text()='.BigWords-style { font-size: 45pt !important; text-align: center !important; }']");
		}

		[Test]
		public void DontChangeBigWordsStyleIfUsedAndPresent()
		{
			var dom = CreateAndMigrateBigWordsPage(headElt =>
			{
				var userStyles = headElt.OwnerDocument.CreateElement("style");
				userStyles.SetAttribute("type", "text/css");
				userStyles.SetAttribute("title", "userModifiedStyles");
				userStyles.InnerText = ".BigWords-style { font-size: 50pt !important; text-align: center !important; }";
				headElt.AppendChild(userStyles);
			});

			AssertThatXmlIn.Dom(dom).HasAtLeastOneMatchForXpath("html/head/style[@type='text/css' and @title='userModifiedStyles' and text()='.BigWords-style { font-size: 50pt !important; text-align: center !important; }']");
			AssertThatXmlIn.Dom(dom).HasNoMatchForXpath("html/head/style[@type='text/css' and @title='userModifiedStyles' and text()='.BigWords-style { font-size: 45pt !important; text-align: center !important; }']");
		}

		[Test]
		public void AddBigWordsStyleIfNeededAndMissingFromStylesheet()
		{
			var dom = CreateAndMigrateBigWordsPage(headElt =>
			{
				var userStyles = headElt.OwnerDocument.CreateElement("style");
				userStyles.SetAttribute("type", "text/css");
				userStyles.SetAttribute("title", "userModifiedStyles");
				userStyles.InnerText = ".OtherWords-style { font-size: 50pt}";
				headElt.AppendChild(userStyles);

			});

			AssertThatXmlIn.Dom(dom).HasAtLeastOneMatchForXpath("html/head/style[@type='text/css' and @title='userModifiedStyles' and text()='.OtherWords-style { font-size: 50pt} .BigWords-style { font-size: 45pt !important; text-align: center !important; }']");
			AssertThatXmlIn.Dom(dom).HasNoMatchForXpath("html/head/style[@type='text/css' and @title='userModifiedStyles' and text()='.BigWords-style { font-size: 45pt !important; text-align: center !important; }']");
		}

		// Common code for tests of adding needed styles. The main difference between the tests is the state of the stylesheet
		// (if any) inserted by the modifyHead action.
		private XmlDocument CreateAndMigrateBigWordsPage(Action<XmlElement> modifyHead)
		{
			SetDom(@"<div class='bloom-page' data-pagelineage='FD115DFF-0415-4444-8E76-3D2A18DBBD27' id='thePage'>
			   <div class='marginBox'>
					<div class='bloom-imageContainer bloom-leadingElement'><img data-license='cc-by-nc-sa' data-copyright='Copyright © 2012, LASI' style='width: 608px; height: 471px; margin-left: 199px; margin-top: 0px;' src='erjwx3bl.q3c.png' alt='This picture, erjwx3bl.q3c.png, is missing or was loading too slowly.' height='471' width='608'></img></div>
					<div aria-describedby='qtip-1' data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement normal-style'>
						<div aria-describedby='qtip-0' data-hasqtip='true' class='bloom-editable BigWords-style bloom-content1' contenteditable='true' lang='en'>
							There was an old man called Bilanga who was very tall and also not yet married.
						</div>
					</div>
				</div>
			</div>
			");
			var book = CreateBook();
			var dom = book.RawDom;
			modifyHead((XmlElement)dom.DocumentElement.ChildNodes[0]);
			var page = (XmlElement) dom.SafeSelectNodes("//div[@id='thePage']")[0];
			book.BringPageUpToDate(page);

			var newPage = (XmlElement) dom.SafeSelectNodes("//div[@id='thePage']")[0];

			CheckPageIsCustomizable(newPage);
			return dom;
		}

		// Enhance: if there are ever cases where there are multiple image containers to migrate, test this.
		// Enhance: if there are ever cases where it is possible not to have exactly corresponding parent elements (e.g., migrating a page with
		// one translation group to one with two), test this.
		// The current intended behavior is to copy the corresponding ones, leave additional destination elements unchanged, and discard
		// additional source ones. Some way to warn the user in the latter case might be wanted. Or, we may want a way to specify which
		// source maps to which destination.

		// some attempt at verifying that it updated the page structure
		private void CheckPageIsCustomizable(XmlElement newPage)
		{
			Assert.That(newPage.Attributes["class"].Value, Is.StringContaining("customPage"));
		}

		private void CheckPageLineage(XmlElement oldPage, XmlElement newPage, string oldGuid, string newGuid)
		{
			var oldLineage = oldPage.Attributes["data-pagelineage"].Value;
			var newLineage = newPage.Attributes["data-pagelineage"].Value;
			Assert.That(newLineage, Is.EqualTo(oldLineage.Replace(oldGuid, newGuid)));
		}

		private void CheckEditableText(XmlElement page, string lang, string text, int groupIndex = 0)
		{
			var transGroup = (XmlElement)page.SafeSelectNodes("//div[contains(@class,'bloom-translationGroup')]")[groupIndex];
			var editDiv = (XmlElement)transGroup.SafeSelectNodes("div[@lang='" + lang + "' and contains(@class,'bloom-editable')]")[0];
			var actualText = editDiv.InnerXml;
			Assert.That(actualText.Trim(), Is.EqualTo(text));
		}
	}
}
