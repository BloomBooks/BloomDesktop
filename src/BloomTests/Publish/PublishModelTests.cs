using NUnit.Framework;
using Bloom.Book;
using Bloom.Publish;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace BloomTests.Publish
{
	[TestFixture]
	public class PublishModelTests
	{
		private const string kDataDivHtml = @"<!DOCTYPE html>
<html>
<body>
	<div id='bloomDataDiv'>
		<div data-book='contentLanguage1' lang='*'>en</div>
		<div data-book='contentLanguage2' lang='*'>fr</div>
		<div data-book='contentLanguage3' lang='*'>es</div>
		<div data-book='bookTitle' lang='en'><p>Test BL-7124</p></div>
		<div data-book='bookTitle' lang='fr'><p>Tester BL-7124</p></div>
		<div data-book='coverImage' lang='*'>100_4274.jpg</div>
		<div data-book='coverImageDescription' lang='*'>
			<div class='bloom-editable ImageDescriptionEdit-style' lang='de' contenteditable='true'>
				<p>kleiner See im Rocky Mountain National Park</p>
			</div>
			<div class='bloom-editable ImageDescriptionEdit-style bloom-content1 bloom-visibility-code-on' lang='en' contenteditable='true'>
				<p>small lake in Rocky Mountain National Park</p>
			</div>
			<div class='bloom-editable ImageDescriptionEdit-style bloom-contentNational2' lang='es' contenteditable='true'>
				<p>pequeño lago en el Parque Nacional de las Montañas Rocosas</p>
			</div>
			<div class='bloom-editable ImageDescriptionEdit-style bloom-contentNational1' lang='fr' contenteditable='true'>
				<p>petit lac dans Le Parc National des Montagnes Rocheuses</p>
			</div>
			<div class='bloom-editable ImageDescriptionEdit-style' lang='tl' contenteditable='true'>
				<p>maliit na lawa sa Rocky Mountain National Park</p>
			</div>
			<div class='bloom-editable ImageDescriptionEdit-style' lang='z' contenteditable='true'><p></p></div>
		</div>
		<div data-book='smallCoverCredits' lang='en'><p>Stephen McConnel</p></div>
		<div data-book='originalContributions' lang='fr'><p>Images par Stephen McConnel, © 2017 Stephen McConnel. CC-BY 4.0.</p></div>
		<div data-book='originalContributions' lang='de'><p>Bilder von Stephen McConnel, © 2017 Stephen McConnel. CC-BY 4.0.</p></div>
		<div data-book='funding' lang='fr'><p>Merci pour tout.</p></div>
		<div data-book='funding' lang='de'><p>Merci pour tout.</p></div>
		<div data-book='versionAcknowledgments' lang='fr'><p>Joe a traduit ce livre.</p></div>
		<div data-book='originalAcknowledgments' lang='fr'><p>Les tests ont produit ce livre.</p></div>
		<div data-book='outsideBackCover' lang='fr'>
			<p>C'est un livre fabuleux!</p>
			<p>(la mère de l'auteur)</p>
		</div>
		<div data-book='copyright' lang='*'>Copyright © 2019, Stephen McConnel</div>
		<div data-book='licenseUrl' lang='*'>http://creativecommons.org/licenses/by/4.0/</div>
		<div data-book='licenseDescription' lang='en'>
			http://creativecommons.org/licenses/by/4.0/<br />You are free to make commercial use of this work. You may adapt and add to this work. You must keep the copyright and credits for authors, illustrators, etc.
		</div>
		<div data-book='licenseImage' lang='*'>license.png</div>
		<div data-book='insideFontCover' lang='fr'><p>Ceci est à l'intérieur de la couverture avant.</p></div>
		<div data-book='insideBackCover' lang='fr'><p>Ceci est à l'intérieur de la couverture arrière.</p></div>
	</div>
</body>
</html>";

		[Test]
		public void RemoveUnwantedLanguageData_BloomDataDiv_RemovesNothing()
		{
			var dom = new HtmlDom(kDataDivHtml);

			// Check occurrences in original HTML.
			VerifyDataDivValues(dom);

			// SUT
			PublishModel.RemoveUnwantedLanguageData(dom, new[] {"en"}, false, new HashSet<string>());

			// Check occurrences in modified HTML.  This should be exactly the same as before.
			VerifyDataDivValues(dom);
		}

		[Test]
		public void RemoveUnwantedLanguageDataFromAllTitles_RemovesUnwantedTitles()
		{
			var allTitles =
				@"{""en"":""my title"", ""fr"":""french title"", ""de"":""german title"", ""tpi"":""tok pisin title"", ""qaa"":""mystery title""}";
			string result =
				PublishModel.RemoveUnwantedLanguageDataFromAllTitles(allTitles, new[] { "en", "fr", "tpi" });
			var allTitlesDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(result);
			Assert.That(allTitlesDict.Count, Is.EqualTo(3));
			Assert.That(allTitlesDict["en"], Is.EqualTo("my title"));
			Assert.That(allTitlesDict["fr"], Is.EqualTo("french title"));
			Assert.That(allTitlesDict["tpi"], Is.EqualTo("tok pisin title"));
		}

		[TestCase("en")]
		[TestCase("fr")]
		public void RemoveUnwantedLanguageData_BloomDataDiv_RemovesNothingEvenWithN1(string metadataLang1Code)
		{
			var dom = new HtmlDom(kDataDivHtml);

			// Check occurrences in original HTML.
			VerifyDataDivValues(dom);

			// SUT
			PublishModel.RemoveUnwantedLanguageData(dom, new[] {"en"}, true, new HashSet<string>(new [] {metadataLang1Code}));

			// Check occurrences in modified HTML.  This should be exactly the same as before.
			VerifyDataDivValues(dom);
		}

		private void VerifyDataDivValues(HtmlDom dom)
		{
			var assertThatDom = AssertThatXmlIn.Dom(dom.RawDom);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*' and @data-book='contentLanguage1']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*' and @data-book='contentLanguage2']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*' and @data-book='contentLanguage3']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and @data-book='bookTitle']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr' and @data-book='bookTitle']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*' and @data-book='coverImage']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*' and @data-book='coverImageDescription']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='de' and contains(@class,'ImageDescriptionEdit-style')]", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and contains(@class,'ImageDescriptionEdit-style')]", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es' and contains(@class,'ImageDescriptionEdit-style')]", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr' and contains(@class,'ImageDescriptionEdit-style')]", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='tl' and contains(@class,'ImageDescriptionEdit-style')]", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='z' and contains(@class,'ImageDescriptionEdit-style')]", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and @data-book='smallCoverCredits']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr' and @data-book='originalContributions']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='de' and @data-book='originalContributions']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr' and @data-book='funding']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='de' and @data-book='funding']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr' and @data-book='versionAcknowledgments']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr' and @data-book='originalAcknowledgments']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr' and @data-book='outsideBackCover']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*' and @data-book='copyright']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*' and @data-book='licenseUrl']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and @data-book='licenseDescription']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*' and @data-book='licenseImage']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr' and @data-book='insideFontCover']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr' and @data-book='insideBackCover']", 1);

			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang]", 27);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang and @data-book]", 21);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang and contains(@class,'ImageDescriptionEdit-style')]", 6);
		}

		string kFrontCoverHtml = @"<!DOCTYPE html>
<html>
<body>
	<div class='bloom-page cover coverColor bloom-frontMatter frontCover outsideFrontCover side-right A4Landscape' data-page='required singleton' data-export='front-matter-cover' data-xmatter-page='frontCover' id='f9b3d571-ea2d-4c67-9a4e-137a718c90d1' data-page-number=''>
		<div class='pageLabel' lang='en' data-i18n='TemplateBooks.PageLabel.Front Cover'>Front Cover</div>
		<div class='pageDescription' lang='en'></div>
		<div class='marginBox'>
			<div class='bloom-translationGroup bookTitle' data-default-languages='V,N1'>
				<label class='bubble'>Book title in {lang}</label>
				<div class='bloom-editable Title-On-Cover-style' lang='z' contenteditable='true' data-book='bookTitle'></div>
				<div class='bloom-editable Title-On-Cover-style' lang='tl' contenteditable='true' data-book='bookTitle'><p>Count tayo kasama ni Robin</p></div>
				<div class='bloom-editable Title-On-Cover-style bloom-content1 bloom-visibility-code-on' lang='en' contenteditable='true' data-book='bookTitle'><p>Counting</p></div>
				<div class='bloom-editable Title-On-Cover-style' lang='es' contenteditable='true' data-book='bookTitle'><p>Contando</p></div>
				<div class='bloom-editable Title-On-Cover-style' lang='fr' contenteditable='true' data-book='bookTitle'><p>Compte</p></div>
			</div>
			<div class='bloom-imageContainer'>
				<img data-book='coverImage' src='aor_ara008.png' data-copyright='Copyright SIL International 2009' data-creator='' data-license='cc-by-sa' alt=''></img>
				<div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement' data-default-languages='auto' data-book='coverImageDescription'>
					<div data-languagetipcontent='English' role='textbox' class='bloom-editable bloom-content1 bloom-visibility-code-on ImageDescriptionEdit-style' contenteditable='true' lang='en'><p></p></div>
					<div data-languagetipcontent='Tagalog' role='textbox' class='bloom-editable ImageDescriptionEdit-style' contenteditable='true' lang='tl'><p></p></div>
					<div data-languagetipcontent='Cebuano' role='textbox' class='bloom-editable ImageDescriptionEdit-style' contenteditable='true' lang='ceb'></div>
					<div class='bloom-editable ImageDescriptionEdit-style' contenteditable='true' lang='z'></div>
					<div data-languagetipcontent='Spanish' role='textbox' class='bloom-editable ImageDescriptionEdit-style' contenteditable='true' lang='es'><p></p></div>
					<div data-languagetipcontent='French' role='textbox' class='bloom-editable ImageDescriptionEdit-style' contenteditable='true' lang='fr'><p></p></div>
				</div>
			</div>
			<div class='bottomBlock'>
				<div data-book='cover-branding-left-html' lang='*'></div>
				<div class='bottomTextContent'>
					<div class='creditsRow' data-hint='You may use this space for author/illustrator, or anything else.'>
						<div class='bloom-translationGroup' data-default-languages='V'>
							<div class='bloom-editable smallCoverCredits Cover-Default-style bloom-content1 bloom-visibility-code-on' lang='en' contenteditable='true' data-book='smallCoverCredits'></div>
							<div class='bloom-editable smallCoverCredits Cover-Default-style' lang='z' contenteditable='true' data-book='smallCoverCredits'></div>
						</div>
					</div>
					<div class='bottomRow' data-have-topic='false'>
						<div class='coverBottomLangName Cover-Default-style' data-derived='languagesOfBook' lang='en'>Tagalog</div>
						<div class='coverBottomBookTopic bloom-alwaysShowBubble Cover-Default-style' data-derived='topic' data-functiononhintclick='ShowTopicChooser()' data-hint='Click to choose topic'></div>
					</div>
				</div>
			</div>
		</div>
	</div>
</body>
</html>";

		[Test]
		public void RemoveUnwantedLanguageData_FrontCoverPage_RemovesNothing()
		{
			var dom = new HtmlDom(kFrontCoverHtml);

			// Check occurrences in original HTML.
			VerifyFrontCoverValues(dom, false);

			// SUT
			PublishModel.RemoveUnwantedLanguageData(dom, new[] {"en"}, false, new HashSet<string>());

			// Check occurrences in modified HTML.  This should be exactly the same as before.
			VerifyFrontCoverValues(dom, false);
		}

		[Test]
		public void RemoveUnwantedLanguageData_FrontCoverPage_RemovesUnwantedButKeepsN1()
		{
			// Check occurrences in original HTML.
			var dom = new HtmlDom(kFrontCoverHtml);
			VerifyFrontCoverValues(dom, false);

			// SUT
			PublishModel.RemoveUnwantedLanguageData(dom, new[] { "tl" }, true, new HashSet<string>(new [] {"en"}));

			// Check occurrences in modified HTML.  This should NOT be exactly the same as before.
			VerifyFrontCoverValues(dom, true);
		}

		[Test]
		public void RemoveUnwantedLanguageData_FrontCoverPage_RemovesUnwantedButKeepsN2()
		{
			// Check occurrences in original HTML.
			var dom = new HtmlDom(kFrontCoverHtml);
			VerifyFrontCoverValues(dom, false);

			// SUT
			PublishModel.RemoveUnwantedLanguageData(dom, new[] { "tl" }, true, new HashSet<string>(new[] { "en" }));

			// Check occurrences in modified HTML.  This should NOT be exactly the same as before.
			VerifyFrontCoverValues(dom, true);
		}

		private void VerifyFrontCoverValues(HtmlDom dom, bool divsRemoved)
		{
			var divsExpected = divsRemoved ? 0 : 1;
			var assertThatDom = AssertThatXmlIn.Dom(dom.RawDom);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and contains(@class,'pageLabel')]", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and contains(@class,'pageDescription')]", 1);

			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='z' and contains(@class,'Title-On-Cover-style') and @data-book='bookTitle']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='tl' and contains(@class,'Title-On-Cover-style') and @data-book='bookTitle']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and contains(@class,'Title-On-Cover-style') and @data-book='bookTitle']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es' and contains(@class,'Title-On-Cover-style') and @data-book='bookTitle']", divsExpected);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr' and contains(@class,'Title-On-Cover-style') and @data-book='bookTitle']", divsExpected);

			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='z' and contains(@class,'ImageDescriptionEdit-style')]", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='tl' and contains(@class,'ImageDescriptionEdit-style')]", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and contains(@class,'ImageDescriptionEdit-style')]", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='ceb' and contains(@class,'ImageDescriptionEdit-style')]", divsExpected);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es' and contains(@class,'ImageDescriptionEdit-style')]", divsExpected);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr' and contains(@class,'ImageDescriptionEdit-style')]", divsExpected);

			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*' and @data-book='cover-branding-left-html']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and contains(@class,'smallCoverCredits') and @data-book='smallCoverCredits']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='z' and contains(@class,'smallCoverCredits') and @data-book='smallCoverCredits']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and contains(@class,'coverBottomLangName') and @data-derived='languagesOfBook']", 1);

			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang]", divsRemoved ? 12 : 17);
		}

		string kCreditsPageHtml = @"<!DOCTYPE html>
<html>
<body>
	<div class='bloom-page bloom-frontMatter credits' data-xmatter-page='credits' id='2cea9462-98e9-4077-be4e-d303a45e95b3' lang=''>
		<div data-after-content='Traditional Front/Back Matter' class='pageLabel' data-i18n='TemplateBooks.PageLabel.Credits Page' lang='en'>Credits Page</div>
		<div class='pageDescription' lang='en'></div>
		<div data-hasqtip='true' class='bloom-metaData licenseAndCopyrightBlock' data-functiononhintclick='bookMetadataEditor' data-hint='Click to Edit Copyright &amp; License' lang='en'>
			<div class='copyright Credits-Page-style' data-derived='copyright' lang='*'>Copyright © 2019, Stephen McConnel</div>
			<div class='licenseBlock'>
				<img class='licenseImage' src='license.png' data-derived='licenseImage' alt=''></img>
				<div class='licenseUrl' data-derived='licenseUrl' lang='*'>http://creativecommons.org/licenses/by/4.0/</div>
				<div class='licenseDescription Credits-Page-style' data-derived='licenseDescription' lang='en'>
					http://creativecommons.org/licenses/by/4.0/ <br />You are free to make commercial use of this work. You may adapt and add to this work. You must keep the copyright and credits for authors, illustrators, etc.
				</div>
				<div class='licenseNotes Credits-Page-style' data-derived='licenseNotes'></div>
			</div>
		</div>
		<div class='bloom-translationGroup versionAcknowledgments' data-default-languages='N1'>
			<div data-languagetipcontent='English' class='bloom-editable versionAcknowledgments Credits-Page-style bloom-content1' data-book='versionAcknowledgments' lang='en' contenteditable='true'></div>
			<div data-hasqtip='true' data-languagetipcontent='français' class='bloom-editable versionAcknowledgments Credits-Page-style bloom-content2 bloom-visibility-code-on' data-book='versionAcknowledgments' lang='fr' contenteditable='true'>
				<p>Joe a traduit ce livre.</p>
			</div>
			<div data-languagetipcontent='español' style='' class='bloom-editable versionAcknowledgments Credits-Page-style bloom-content3' data-book='versionAcknowledgments' lang='es' contenteditable='true'></div>
		</div>
		<div class='copyright Credits-Page-style' data-derived='originalCopyrightAndLicense'></div>
		<div class='bloom-translationGroup originalAcknowledgments' data-default-languages='N1'>
			<div data-languagetipcontent='English' style='' class='bloom-editable bloom-copyFromOtherLanguageIfNecessary Credits-Page-style bloom-content1' data-book='originalAcknowledgments' lang='en' contenteditable='true'></div>
			<div data-languagetipcontent='español' style='' class='bloom-editable bloom-copyFromOtherLanguageIfNecessary Credits-Page-style bloom-content3 bloom-contentNational2' data-book='originalAcknowledgments' lang='es' contenteditable='true'></div>
			<div data-hasqtip='true' data-languagetipcontent='français' class='bloom-editable bloom-copyFromOtherLanguageIfNecessary Credits-Page-style bloom-content2 bloom-contentNational1 bloom-visibility-code-on' data-book='originalAcknowledgments' lang='fr' contenteditable='true'>
				<p>Les tests ont produit ce livre.</p>
			</div>
			<div style='' class='bloom-editable bloom-copyFromOtherLanguageIfNecessary Credits-Page-style' data-book='originalAcknowledgments' lang='z' contenteditable='true'></div>
		</div>
		<div data-hasqtip='true' class='ISBNContainer' data-hint='International Standard Book Number. Leave blank if you do not have one of these.'>
			<span class='bloom-doNotPublishIfParentOtherwiseEmpty Credits-Page-style'>ISBN</span>
			<div class='bloom-translationGroup bloom-recording-optional' data-default-languages='*'>
				<div data-languagetipcontent='English' style='' class='bloom-editable Credits-Page-style bloom-content1' data-book='ISBN' lang='en' contenteditable='true'></div>
				<div data-languagetipcontent='español' style='' class='bloom-editable Credits-Page-style bloom-content3 bloom-contentNational2' data-book='ISBN' lang='es' contenteditable='true'></div>
				<div data-languagetipcontent='français' style='' class='bloom-editable Credits-Page-style bloom-content2 bloom-contentNational1' data-book='ISBN' lang='fr' contenteditable='true'></div>
				<div class='bloom-editable Credits-Page-style bloom-visibility-code-on' data-book='ISBN' lang='*' contenteditable='true'></div>
			</div>
		</div>
		<div data-book='credits-page-branding-bottom-html' lang='*'><img class='branding' src='butterfly.png' alt=''></img></div>
	</div>
</body>
</html>";

		[Test]
		public void RemoveUnwantedLanguageData_CreditsPage_RemovesNothing()
		{
			var dom = new HtmlDom(kCreditsPageHtml);
			// Check occurrences in original HTML.
			VerifyCreditsPageValues(dom, false);

			// SUT
			PublishModel.RemoveUnwantedLanguageData(dom, new[] {"en"}, false, new HashSet<string>());

			// Check occurrences in modified HTML.  This should be exactly the same as before.
			VerifyCreditsPageValues(dom, false);
		}

		[Test]
		public void RemoveUnwantedLanguageData_CreditsPage_RemovesRemovesUnwantedButKeepsN1()
		{
			var dom = new HtmlDom(kCreditsPageHtml);
			// Check occurrences in original HTML.
			VerifyCreditsPageValues(dom, false);

			// SUT
			PublishModel.RemoveUnwantedLanguageData(dom, new[] {"en"}, true, new HashSet<string>(new[] { "en" }));

			// Check occurrences in modified HTML.  This should NOT be exactly the same as before.
			VerifyCreditsPageValues(dom, true);
		}

		private void VerifyCreditsPageValues(HtmlDom dom, bool divsRemoved)
		{
			var divsExpected = divsRemoved ? 0 : 1;
			var assertThatDom = AssertThatXmlIn.Dom(dom.RawDom);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='' and contains(@class, 'bloom-page')]", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and @class='pageLabel']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and @class='pageDescription']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and contains(@class, 'licenseAndCopyrightBlock')]", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*' and contains(@class, 'copyright')]", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*' and @class='licenseUrl']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and contains(@class, 'licenseDescription')]", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and @data-book='versionAcknowledgments']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr' and @data-book='versionAcknowledgments']", divsExpected);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es' and @data-book='versionAcknowledgments']", divsExpected);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and @data-book='originalAcknowledgments']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es' and @data-book='originalAcknowledgments']", divsExpected);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr' and @data-book='originalAcknowledgments']", divsExpected);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='z' and @data-book='originalAcknowledgments']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and @data-book='ISBN']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es' and @data-book='ISBN']", divsExpected);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr' and @data-book='ISBN']", divsExpected);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*' and @data-book='ISBN']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*' and @data-book='credits-page-branding-bottom-html']", 1);

			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang]", divsRemoved ? 13 : 19);
		}

		[Test]
		public void RemoveUnwantedLanguageData_TextPage_RemovesUnwantedDivs()
		{
			var html = @"<!DOCTYPE html>
<html>
<body>
	<div class='bloom-page numberedPage customPage side-right A4Landscape bloom-monolingual' data-page='' id='ba82b94f-71ec-48f7-a1cc-a68d5765e255' data-page-number='2' testRemoves='false' lang=''>
		<div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Just Text' testRemoves='false' lang='en'>
			Just Text
		</div>
		<div class='pageDescription' testRemoves='false' lang='en'></div>
		<div class='marginBox'>
			<div class='split-pane-component-inner'>
				<div aria-describedby='qtip-0' data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto'>
					<div role='textbox' class='bloom-editable normal-style bloom-content1 bloom-visibility-code-on' contenteditable='true' testRemoves='false' lang='en'>
						<p>This is Robin.</p>
					</div>
					<div role='textbox' class='bloom-editable normal-style' contenteditable='true' testRemoves='true' lang='tl'>
						<p>Ako si Robin</p>
					</div>
					<div role='textbox' class='bloom-editable normal-style' contenteditable='true' testRemoves='true' lang='ceb'></div>
					<div class='bloom-editable normal-style' contenteditable='true' testRemoves='false' lang='z'></div>
				</div>
			</div>
		</div>
	</div>
</body>
</html>";
			// Check occurrences in original HTML.
			var dom = new HtmlDom(html);
			var assertThatDom = AssertThatXmlIn.Dom(dom.RawDom);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='' and contains(@class, 'bloom-page')]", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and @class='pageLabel']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and @class='pageDescription']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and contains(@class, 'bloom-editable') and @role='textbox']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='tl' and contains(@class, 'bloom-editable') and @role='textbox']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='ceb' and contains(@class, 'bloom-editable') and @role='textbox']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='z' and contains(@class, 'bloom-editable') and @contenteditable='true']", 1);

			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang]", 7);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang and @testRemoves='false']", 5);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang and @testRemoves='true']", 2);

			// SUT
			PublishModel.RemoveUnwantedLanguageData(dom, new[] {"en"}, false, new HashSet<string>());

			// Check occurrences in modified HTML.
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='' and contains(@class, 'bloom-page')]", 1);	// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and @class='pageLabel']", 1);			// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and @class='pageDescription']", 1);		// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and contains(@class, 'bloom-editable') and @role='textbox']", 1);	// unchanged
			assertThatDom.HasNoMatchForXpath("//div[@lang='tl' and contains(@class, 'bloom-editable') and @role='textbox']");		// removed editable textbox
			assertThatDom.HasNoMatchForXpath("//div[@lang='ceb' and contains(@class, 'bloom-editable') and @role='textbox']");		// removed editable textbox
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='z' and contains(@class, 'bloom-editable') and @contenteditable='true']", 1);	// unchanged

			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang]", 5);	// removed 2 editable textboxes
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang and @testRemoves='false']", 5);
			assertThatDom.HasNoMatchForXpath("//div[@lang and @testRemoves='true']");
		}

		[Test]
		public void RemoveUnwantedLanguageData_BloomPage_PreservesPageLabel()
		{
			var html = @"<!DOCTYPE html>
<html>
<body>
	<div class='bloom-page numberedPage customPage side-right A4Landscape bloom-monolingual' data-page='' id='ba82b94f-71ec-48f7-a1cc-a68d5765e255' data-page-number='2' testRemoves='false' lang=''>
		<div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Just Text' testRemoves='false' lang='en'>
			Just Text
		</div>
		<div class='pageDescription' testRemoves='false' lang='en'></div>
		<div class='marginBox'>
			<div class='split-pane-component-inner'>
				<div aria-describedby='qtip-0' data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto'>
					<div class='bloom-editable normal-style bloom-content2' testRemoves='true' lang='en'><p>This is Robin.</p></div>
					<div class='bloom-editable normal-style bloom-content1' testRemoves='false' lang='tl'><p>Ako si Robin</p></div>
				</div>
			</div>
		</div>
	</div>
</body>
</html>";
			// Check occurrences in original HTML.
			var dom = new HtmlDom(html);
			var assertThatDom = AssertThatXmlIn.Dom(dom.RawDom);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='' and contains(@class, 'bloom-page')]", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and @class='pageLabel']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and @class='pageDescription']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and contains(@class, 'bloom-editable')]", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='tl' and contains(@class, 'bloom-editable')]", 1);

			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang]", 5);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang and @testRemoves='false']", 4);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang and @testRemoves='true']", 1);

			// SUT
			PublishModel.RemoveUnwantedLanguageData(dom, new[] {"tl"}, false, new HashSet<string>());

			// Check occurrences in modified HTML.
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='' and contains(@class, 'bloom-page')]", 1);	// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and @class='pageLabel']", 1);			// unchanged despite lang='en'
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and @class='pageDescription']", 1);		// unchanged despite lang='en'
			assertThatDom.HasNoMatchForXpath("//div[@lang='en' and contains(@class, 'bloom-editable')]");				// removed editable textbox
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='tl' and contains(@class, 'bloom-editable')]", 1);	// unchanged

			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang]", 4);	// removed 1 editable textbox
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang and @testRemoves='false']", 4);
			assertThatDom.HasNoMatchForXpath("//div[@lang and @testRemoves='true']");
		}

		string kEmbeddedLangDivsHtml = @"<!DOCTYPE html>
<html>
<body>
	<div class='bloom-page numberedPage customPage side-right A4Landscape bloom-monolingual' data-page='' id='ba82b94f-71ec-48f7-a1cc-a68d5765e255' testRemoves='false' lang=''>
		<div class='marginBox'>
			<div class='split-pane-component-inner'>
				<div class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto' testRemoves='false' lang='de'>
					<div role='textbox' class='bloom-editable normal-style bloom-content1 bloom-visibility-code-on' contenteditable='true' testRemoves='false' lang='en'><p>Hello</p></div>
					<div class='bloom-editable normal-style' role='textbox' contenteditable='true' testRemoves='true' lang='de'></div>
					<div class='bloom-editable normal-style' role='textbox' contenteditable='true' testRemoves='true' lang='es'></div>
					<div class='bloom-editable normal-style' role='textbox' contenteditable='true' testRemoves='true' lang='fr'></div>
					<div class='bloom-editable normal-style' contenteditable='true' testRemoves='false' lang='z'></div>
				</div>
			</div>
		</div>
	</div>
</body>
</html>";

		[TestCase(true)]
		[TestCase(false)]
		public void RemoveUnwantedLanguageData_PreserveIfEmbeddedDivWanted(bool shouldPruneXmatter)
		{
			var dom = new HtmlDom(kEmbeddedLangDivsHtml);
			// Check occurrences in original HTML.
			VerifyOriginalEmbeddedDivsAreAllThere(dom);

			// SUT
			PublishModel.RemoveUnwantedLanguageData(dom, new[] {"en"}, shouldPruneXmatter, new HashSet<string>(new[] { "de" }));

			// Check occurrences in modified HTML.
			VerifyOnlyUnwantedEmbeddedDivsAreRemoved(dom, false);
		}

		string kEmbeddedLangDivsXMatterHtml = @"<!DOCTYPE html>
<html>
<body>
	<div class='bloom-page cover frontCover A4Landscape bloom-monolingual' data-page='required singleton'  data-xmatter-page='frontCover' id='ba82b94f-71ec-48f7-a1cc-a68d5765e255' testRemoves='false' lang=''>
		<div class='marginBox'>
			<div class='split-pane-component-inner'>
				<div class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto' testRemoves='false' lang='de'>
					<div role='textbox' class='bloom-editable normal-style bloom-content1 bloom-visibility-code-on' contenteditable='true' testRemoves='false' lang='en'><p>Hello</p></div>
					<div class='bloom-editable normal-style' role='textbox' contenteditable='true' testRemoves='true' lang='de'></div>
					<div class='bloom-editable normal-style' role='textbox' contenteditable='true' testRemoves='true' lang='es'></div>
					<div class='bloom-editable normal-style' role='textbox' contenteditable='true' testRemoves='true' lang='fr'></div>
					<div class='bloom-editable normal-style' contenteditable='true' testRemoves='false' lang='z'></div>
				</div>
			</div>
		</div>
	</div>
</body>
</html>";

		[Test]
		public void RemoveUnwantedLanguageData_PreserveIfEmbeddedDivWantedWithXmatter()
		{
			var dom = new HtmlDom(kEmbeddedLangDivsXMatterHtml);
			// Check occurrences in original HTML.
			VerifyOriginalEmbeddedDivsAreAllThere(dom);

			// SUT
			PublishModel.RemoveUnwantedLanguageData(dom, new[] {"en"}, shouldPruneXmatter: false, new HashSet<string>());

			// Check occurrences in modified HTML: nothing removed from xmatter unless shouldPruneXmatter is true
			VerifyOriginalEmbeddedDivsAreAllThere(dom);
		}


		[Test]
		public void RemoveUnwantedLanguageData_PreserveIfEmbeddedDivWantedWithXmatterN1()
		{
			var dom = new HtmlDom(kEmbeddedLangDivsXMatterHtml);
			// Check occurrences in original HTML.
			VerifyOriginalEmbeddedDivsAreAllThere(dom);

			// SUT
			PublishModel.RemoveUnwantedLanguageData(dom, new[] {"en"}, shouldPruneXmatter: true, new HashSet<string>(new [] {"de"}));

			// Check occurrences in modified HTML: German should be preserved, French and Spanish removed.
			VerifyOnlyUnwantedEmbeddedDivsAreRemoved(dom, true);
		}

		private void VerifyOriginalEmbeddedDivsAreAllThere(HtmlDom dom)
		{
			var assertThatDom = AssertThatXmlIn.Dom(dom.RawDom);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='' and contains(@class, 'bloom-page')]", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='de' and contains(@class, 'bloom-translationGroup')]", 1);	// bogus value for test
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and contains(@class, 'bloom-editable') and @role='textbox']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='de' and contains(@class, 'bloom-editable') and @role='textbox']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es' and contains(@class, 'bloom-editable') and @role='textbox']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr' and contains(@class, 'bloom-editable') and @role='textbox']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='z' and contains(@class, 'bloom-editable') and @contenteditable='true']", 1);

			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang]", 7);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@testRemoves='false']", 4);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@testRemoves='true']", 3);
		}

		private void VerifyOnlyUnwantedEmbeddedDivsAreRemoved(HtmlDom dom, bool isXMatterWithN1)
		{
			var countOfGerman = isXMatterWithN1 ? 1 : 0;
			var assertThatDom = AssertThatXmlIn.Dom(dom.RawDom);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='' and contains(@class, 'bloom-page')]", 1);	// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='de' and contains(@class, 'bloom-translationGroup')]", 1);	// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and contains(@class, 'bloom-editable') and @role='textbox']", 1);	// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='de' and contains(@class, 'bloom-editable') and @role='textbox']", countOfGerman);	// removed if German not N1
			assertThatDom.HasNoMatchForXpath("//div[@lang='es' and contains(@class, 'bloom-editable') and @role='textbox']");	// removed
			assertThatDom.HasNoMatchForXpath("//div[@lang='fr' and contains(@class, 'bloom-editable') and @role='textbox']");	// removed
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='z' and contains(@class, 'bloom-editable') and @contenteditable='true']", 1);	// unchanged

			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang]", 4 + countOfGerman);	// two or three div[@lang] removed
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@testRemoves='false']", 4);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@testRemoves='true']", countOfGerman);
		}

		string kCssStylesHtml = @"<!DOCTYPE html>
<html>
<head>
    <style type='text/css' title='userModifiedStyles'>
    /*<![CDATA[*/
    .normal-style[lang='tl'] { font-size: 19pt !important; font-family: XYZ !important; }
    .normal-style[lang='en'] { font-size: 16pt !important; font-family: Cambria !important; }
    .normal-style[lang='fr'] { font-size: 13pt !important; font-family: ABC !important; }
    .normal-style[lang='es'] { font-size: 10pt !important; font-family: Hola !important; }
	.normal-style[lang='xkal'] { font-size: 17pt !important; font-family: QED !important; }
    .normal-style { font-size: 16pt !important; }/*]]>*/
    </style>
</head>
<body>
	<div class='bloom-page bloom-frontMatter credits' data-xmatter-page='credits' id='2cea9462-98e9-4077-be4e-d303a45e95b3' lang=''>
		<div class='bloom-translationGroup versionAcknowledgments' data-default-languages='N1'>
			<div data-hasqtip='true' data-languagetipcontent='français' class='bloom-editable versionAcknowledgments Credits-Page-style bloom-content2 bloom-visibility-code-on' data-book='versionAcknowledgments' lang='fr' contenteditable='true'>
				<p>Joe a traduit ce livre.</p>
			</div>
			<div data-languagetipcontent='español' style='' class='bloom-editable versionAcknowledgments Credits-Page-style bloom-content3' data-book='versionAcknowledgments' lang='es' contenteditable='true'></div>
		</div>
	</div>
</body>
</html>";

		#region BL-11357
		// All test cases produce the same result because we always preserve tl (L1) and en (L2) in the css styles regardless
		// of shouldPruneXmatter or the languagesToInclude (since languagesToInclude don't contain fr or es)
		// Note, we expect these tests to change or be replaced in 5.4 when we start being smarter about
		// removing styles which actually aren't used in the dom.
		[TestCase(true, new[] { "tl" })]
		[TestCase(false, new[] { "tl" })]
		[TestCase(true, new[] { "tl", "en" })]
		[TestCase(false, new[] { "tl", "en" })]
		public void RemoveUnwantedLanguageData_PreserveCssStylesForN1(bool shouldPruneXmatter, IEnumerable<string> languagesToInclude)
		{
			var dom = new HtmlDom(kCssStylesHtml);
			// Check occurrences in original HTML.
			Assert.That(dom.InnerXml.Contains(".normal-style[lang='tl'] { font-size: 19pt !important; font-family: XYZ !important; }"));
			Assert.That(dom.InnerXml.Contains(".normal-style[lang='en'] { font-size: 16pt !important; font-family: Cambria !important; }"));
			Assert.That(dom.InnerXml.Contains(".normal-style[lang='fr'] { font-size: 13pt !important; font-family: ABC !important; }"));
			Assert.That(dom.InnerXml.Contains(".normal-style[lang='es'] { font-size: 10pt !important; font-family: Hola !important; }"));
			Assert.That(dom.InnerXml.Contains(".normal-style[lang='xkal'] { font-size: 17pt !important; font-family: QED !important; }"));
			Assert.That(dom.InnerXml.Contains(".normal-style { font-size: 16pt !important; }"));

			// SUT
			PublishModel.RemoveUnwantedLanguageData(dom, languagesToInclude, shouldPruneXmatter, new HashSet<string>(new[] { "es", "xkal" }));

			// Check occurrences in modified HTML
			// We always preserve tl (L1) since it is in all the languagesToInclude sets
			Assert.That(dom.InnerXml.Contains(".normal-style[lang='tl'] { font-size: 19pt !important; font-family: XYZ !important; }"));
			if (languagesToInclude.Contains("en"))
			{
				Assert.That(dom.InnerXml.Contains(
					".normal-style[lang='en'] { font-size: 16pt !important; font-family: Cambria !important; }"));
			}
			else
			{
				Assert.That(!dom.InnerXml.Contains(".normal-style[lang='en']"));
			}

			// French rule should be kept if we are not pruning xmatter, since we will keep the french in the xmatter
			if (shouldPruneXmatter)
			{
				Assert.That(!dom.InnerXml.Contains(".normal-style[lang='fr']"));
			}
			else
			{
				Assert.That(dom.InnerXml.Contains(".normal-style[lang='fr'] { font-size: 13pt !important; font-family: ABC !important; }"));
			}

			// es is kept even if pruning xmatter, since it occurs in the xmatter and in the list of additional xmatter languages
			Assert.That(dom.InnerXml.Contains(".normal-style[lang='es'] { font-size: 10pt !important; font-family: Hola !important; }"));
			// We won't keep xkal even though it's in the list of additional xmatter languges, because the code is smart enough
			// to notice that there is no data in that language.
			Assert.That(!dom.InnerXml.Contains(".normal-style[lang='xkal']"));
			Assert.That(dom.InnerXml.Contains(".normal-style { font-size: 16pt !important; }"));
		}

		#endregion
	}
}
