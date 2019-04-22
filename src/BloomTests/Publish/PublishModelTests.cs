using System;
using System.IO;
using NUnit.Framework;
using SIL.IO;
using Bloom.Book;
using Bloom.Publish;

namespace BloomTests.Publish
{
	[TestFixture]
	public class BloomModelTests
	{
		[Test]
		public void RemoveUnwantedLanguageData_HandlesBloomDataDiv()
		{
			var html = @"<!DOCTYPE html>
<html>
<body>
	<div id='bloomDataDiv'>
		<div data-book='contentLanguage1' lang='*'>en</div>
		<div data-book='contentLanguage1Rtl' lang='*'>False</div>
		<div data-book='languagesOfBook' lang='*'>English</div>
		<div data-book='coverImage' lang='*'>aor_ara008.png</div>
		<div data-book='bookTitle' lang='en'><p>Counting</p></div>
		<div data-book='bookTitle' lang='tl'><p>Count tayo kasama</p></div>
		<div data-book='coverImageDescription' lang='*'>
			<div data-languagetipcontent='English' class='bloom-editable bloom-content1 bloom-contentNational1 bloom-visibility-code-on ImageDescriptionEdit-style' contenteditable='true' lang='en'>
				<p></p>
			</div>
			<div data-languagetipcontent='Tagalog' class='bloom-editable ImageDescriptionEdit-style' contenteditable='true' lang='tl'>
				<p></p>
			</div>
			<div data-languagetipcontent='Cebuano' class='bloom-editable ImageDescriptionEdit-style' contenteditable='true' lang='ceb'></div>
			<div class='bloom-editable ImageDescriptionEdit-style' contenteditable='true' lang='z'></div>
		</div>
		<div data-book='copyright' lang='*'>Copyright © 2019, John Doe</div>
		<div data-book='licenseUrl' lang='*'>http://creativecommons.org/licenses/by/4.0/</div>
		<div data-book='licenseDescription' lang='en'>
			http://creativecommons.org/licenses/by/4.0/<br />You are free to make commercial use of this work. You may adapt and add to this work. You must keep the copyright and credits for authors, illustrators, etc.
		</div>
		<div data-book='licenseImage' lang='*'>license.png</div>
	</div>
</body>
</html>";
			// Check counts in original HTML.
			var dom = new HtmlDom(html);
			var assertThatDom = AssertThatXmlIn.Dom(dom.RawDom);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 3);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='tl']", 2);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='ceb']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='z']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*']", 8);
			assertThatDom.HasNoMatchForXpath("//div[@lang='']");
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang]", 15);

			// SUT
			PublishModel.RemoveUnwantedLanguageData(dom, new[] {"en"});

			// Check counts in modified HTML.
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 3);	// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='tl']", 1);	// lost image description
			assertThatDom.HasNoMatchForXpath("//div[@lang='ceb']");						// lost image description
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='z']", 1);	// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*']", 8);	// unchanged
			assertThatDom.HasNoMatchForXpath("//div[@lang='']");						// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang]", 13);		// unchanged
		}

		[Test]
		public void RemoveUnwantedLanguageData_HandlesStandardTextPage()
		{
			var html = @"<!DOCTYPE html>
<html>
<body>
	<div class='bloom-page numberedPage customPage side-right A4Landscape bloom-monolingual' data-page='' id='ba82b94f-71ec-48f7-a1cc-a68d5765e255' data-pagelineage='a31c38d8-c1cb-4eb9-951b-d2840f6a8bdb;b0503546-bdb5-4e85-a77c-3f8d5ab43397;94f328c2-a113-4d75-8551-ccb19f1de268' data-page-number='2' lang=''>
		<div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Just Text' lang='en'>
			Just Text
		</div>
		<div class='pageDescription' lang='en'></div>
		<div class='marginBox'>
			<div class='split-pane-component-inner'>
				<div aria-describedby='qtip-0' data-hasqtip='true' class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto'>
					<div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 124px;' class='bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' contenteditable='true' lang='en'>
						<p>This is Robin.</p>
					</div>
					<div data-languagetipcontent='Tagalog' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 124px;' class='bloom-editable normal-style' contenteditable='true' lang='tl'>
						<p>Ako si Robin</p>
					</div>
					<div data-languagetipcontent='Cebuano' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 124px;' class='bloom-editable normal-style' contenteditable='true' lang='ceb'></div>
					<div class='bloom-editable normal-style' contenteditable='true' lang='z'></div>
				</div>
			</div>
		</div>
	</div>
</body>
</html>";
			// Check counts in original HTML.
			var dom = new HtmlDom(html);
			var assertThatDom = AssertThatXmlIn.Dom(dom.RawDom);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 3);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='tl']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='ceb']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='z']", 1);
			assertThatDom.HasNoMatchForXpath("//div[@lang='*']");
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang]", 7);

			// SUT
			PublishModel.RemoveUnwantedLanguageData(dom, new[] {"en"});

			// Check counts in modified HTML.
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 3);	// unchanged
			assertThatDom.HasNoMatchForXpath("//div[@lang='tl']");
			assertThatDom.HasNoMatchForXpath("//div[@lang='ceb']");
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='z']", 1);	// unchanged
			assertThatDom.HasNoMatchForXpath("//div[@lang='*']");						// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='']", 1);	// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang]", 5);
		}

		[Test]
		public void RemoveUnwantedLanguageData_HandlesFrontCoverPage()
		{
			var html = @"<!DOCTYPE html>
<html>
<body>
	<div class='bloom-page cover coverColor bloom-frontMatter frontCover outsideFrontCover side-right A4Landscape' data-page='required singleton' data-export='front-matter-cover' data-xmatter-page='frontCover' id='f9b3d571-ea2d-4c67-9a4e-137a718c90d1' data-page-number=''>
		<div class='pageLabel' lang='en' data-i18n='TemplateBooks.PageLabel.Front Cover'>
			Front Cover
		</div>
		<div class='pageDescription' lang='en'></div>
		<div class='marginBox'>
			<div class='bloom-translationGroup bookTitle' data-default-languages='V,N1'>
				<label class='bubble'>Book title in {lang}</label>
				<div class='bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-padForOverflow' lang='z' contenteditable='true' data-book='bookTitle'></div>
				<div class='bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-padForOverflow' lang='tl' contenteditable='true' data-book='bookTitle'>
					<p>Count tayo kasama ni Robin</p>
				</div>
				<div class='bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-padForOverflow bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='en' contenteditable='true' data-book='bookTitle'>
					<p>Counting</p>
				</div>
			</div>
			<div class='bloom-imageContainer'>
				<img data-book='coverImage' src='aor_ara008.png' data-copyright='Copyright SIL International 2009' data-creator='' data-license='cc-by-sa' alt=''></img>

				<div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement' data-default-languages='auto' data-book='coverImageDescription'>
					<div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable bloom-content1 bloom-contentNational1 bloom-visibility-code-on ImageDescriptionEdit-style' contenteditable='true' lang='en'>
						<p></p>
					</div>
					<div data-languagetipcontent='Tagalog' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable ImageDescriptionEdit-style' contenteditable='true' lang='tl'>
						<p></p>
					</div>
					<div data-languagetipcontent='Cebuano' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable ImageDescriptionEdit-style' contenteditable='true' lang='ceb'></div>
					<div class='bloom-editable ImageDescriptionEdit-style' contenteditable='true' lang='z'></div>
				</div>
			</div>
			<div class='bottomBlock'>
				<div data-book='cover-branding-left-html' lang='*'></div>
				<div class='bottomTextContent'>
					<div class='creditsRow' data-hint='You may use this space for author/illustrator, or anything else.'>
						<div class='bloom-translationGroup' data-default-languages='V'>
							<div class='bloom-editable smallCoverCredits Cover-Default-style' lang='z' contenteditable='true' data-book='smallCoverCredits'></div>
							<div class='bloom-editable smallCoverCredits Cover-Default-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='en' contenteditable='true' data-book='smallCoverCredits'></div>
						</div>
					</div>
					<div class='bottomRow' data-have-topic='false'>
						<div class='coverBottomLangName Cover-Default-style' data-book='languagesOfBook'>
							English
						</div>
						<div class='coverBottomBookTopic bloom-userCannotModifyStyles bloom-alwaysShowBubble Cover-Default-style' data-derived='topic' data-functiononhintclick='ShowTopicChooser()' data-hint='Click to choose topic'></div>
					</div>
				</div>
			</div>
		</div>
	</div>
</body>
</html>";
			// Check counts in original HTML.
			var dom = new HtmlDom(html);
			var assertThatDom = AssertThatXmlIn.Dom(dom.RawDom);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 5);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='tl']", 2);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='ceb']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='z']", 3);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*']", 1);
			assertThatDom.HasNoMatchForXpath("//div[@lang='']");
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang]", 12);

			// SUT
			PublishModel.RemoveUnwantedLanguageData(dom, new[] {"en"});

			// Check counts in modified HTML.
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 5);	// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='tl']", 1);	// lost image description
			assertThatDom.HasNoMatchForXpath("//div[@lang='ceb']");						// lost image description
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='z']", 3);	// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*']", 1);	// unchanged
			assertThatDom.HasNoMatchForXpath("//div[@lang='']");						// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang]", 10);
		}

		[Test]
		public void RemoveUnwantedLanguageData_HandlesMultipleContentLanguageBloomDataDiv()
		{
			var html = @"<!DOCTYPE html>
<html>
<body>
	<div id='bloomDataDiv'>
		<div data-book='contentLanguage1' lang='*'>en</div>
		<div data-book='contentLanguage2' lang='*'>fr</div>
		<div data-book='contentLanguage3' lang='*'>es</div>
		<div data-book='bookTitle' lang='en'>
			<p>Test BL-7124</p>
		</div>
		<div data-book='bookTitle' lang='fr'>
			<p>Tester BL-7124</p>
		</div>
		<div data-book='coverImage' lang='*'>100_4274.jpg</div>

		<div data-book='coverImageDescription' lang='*'>
			<div class='bloom-editable ImageDescriptionEdit-style bloom-content1 bloom-visibility-code-on' lang='en' contenteditable='true'>
				<p>small lake in Rocky Mountain National Park</p>
			</div>
			<div class='bloom-editable ImageDescriptionEdit-style' lang='z' contenteditable='true'>
				<p></p>
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
			<div class='bloom-editable ImageDescriptionEdit-style' lang='de' contenteditable='true'>
				<p>kleiner See im Rocky Mountain National Park</p>
			</div>
		</div>
		<div data-book='smallCoverCredits' lang='en'>
			<p>Stephen McConnel</p>
		</div>
		<div data-book='originalContributions' lang='fr'>
			<p>Images par Stephen McConnel, © 2017 Stephen McConnel. CC-BY 4.0.</p>
		</div>
		<div data-book='originalContributions' lang='de'>
			<p>Bilder von Stephen McConnel, © 2017 Stephen McConnel. CC-BY 4.0.</p>
		</div>
		<div data-book='funding' lang='fr'>
			<p>Merci pour tout.</p>
		</div>
		<div data-book='funding' lang='de'>
			<p>Merci pour tout.</p>
		</div>
		<div data-book='versionAcknowledgments' lang='fr'>
			<p>Joe a traduit ce livre.</p>
		</div>
		<div data-book='originalAcknowledgments' lang='fr'>
			<p>Les tests ont produit ce livre.</p>
		</div>
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
		<div data-book='insideFontCover' lang='fr'>
			<p>Ceci est à l'intérieur de la couverture avant.</p>
		</div>
		<div data-book='insideBackCover' lang='fr'>
			<p>Ceci est à l'intérieur de la couverture arrière.</p>
		</div>
	</div>
</body>
</html>";
			// Check counts in original HTML.
			var dom = new HtmlDom(html);
			var assertThatDom = AssertThatXmlIn.Dom(dom.RawDom);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='de']", 3);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 4);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']", 9);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='tl']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='z']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*']", 8);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang]", 27);

			// SUT
			PublishModel.RemoveUnwantedLanguageData(dom, new[] {"en"});

			// Check counts in modified HTML.
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='de']", 2);	// lost image description
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 4);	// unchanged
			assertThatDom.HasNoMatchForXpath("//div[@lang='es']");						// lost image description
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']", 8);	// lost image description
			assertThatDom.HasNoMatchForXpath("//div[@lang='tl']");						// lost image description
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='z']", 1);	// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*']", 8);	// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang]", 23);
		}

		[Test]
		public void RemoveUnwantedLanguageData_HandlesCreditsPage()
		{
			var html = @"<!DOCTYPE html>
<html>
<body>
	<div class='bloom-page bloom-frontMatter credits' data-xmatter-page='credits' id='2cea9462-98e9-4077-be4e-d303a45e95b3' lang=''>
		<div data-after-content='Traditional Front/Back Matter' class='pageLabel' data-i18n='TemplateBooks.PageLabel.Credits Page' lang='en'>
			Credits Page
		</div>
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
			<div data-languagetipcontent='English' style='' class='bloom-editable versionAcknowledgments Credits-Page-style bloom-content1' data-book='versionAcknowledgments' lang='en' contenteditable='true'>
			</div>
			<div data-hasqtip='true' data-languagetipcontent='français' class='bloom-editable versionAcknowledgments Credits-Page-style bloom-content2 bloom-contentNational1 bloom-visibility-code-on' data-book='versionAcknowledgments' lang='fr' contenteditable='true'>
				<p>Joe a traduit ce livre.</p>
			</div>
			<div data-languagetipcontent='español' style='' class='bloom-editable versionAcknowledgments Credits-Page-style bloom-content3 bloom-contentNational2' data-book='versionAcknowledgments' lang='es' contenteditable='true'>
			</div>
		</div>
		<div class='copyright Credits-Page-style' data-derived='originalCopyrightAndLicense'></div>
		<div class='bloom-translationGroup originalAcknowledgments' data-default-languages='N1'>
			<div data-languagetipcontent='English' style='' class='bloom-editable bloom-copyFromOtherLanguageIfNecessary Credits-Page-style bloom-content1' data-book='originalAcknowledgments' lang='en' contenteditable='true'>
			</div>
			<div style='' class='bloom-editable bloom-copyFromOtherLanguageIfNecessary Credits-Page-style' data-book='originalAcknowledgments' lang='z' contenteditable='true'>
			</div>
			<div data-languagetipcontent='español' style='' class='bloom-editable bloom-copyFromOtherLanguageIfNecessary Credits-Page-style bloom-content3 bloom-contentNational2' data-book='originalAcknowledgments' lang='es' contenteditable='true'></div>
			<div data-hasqtip='true' data-languagetipcontent='français' class='bloom-editable bloom-copyFromOtherLanguageIfNecessary Credits-Page-style bloom-content2 bloom-contentNational1 bloom-visibility-code-on' data-book='originalAcknowledgments' lang='fr' contenteditable='true'>
				<p>Les tests ont produit ce livre.</p>
			</div>
		</div>
		<div data-hasqtip='true' class='ISBNContainer' data-hint='International Standard Book Number. Leave blank if you do not have one of these.'>
			<span class='bloom-doNotPublishIfParentOtherwiseEmpty Credits-Page-style'>ISBN</span>
			<div class='bloom-translationGroup bloom-recording-optional' data-default-languages='*'>
				<div data-languagetipcontent='English' style='' class='bloom-editable Credits-Page-style bloom-content1' data-book='ISBN' lang='en' contenteditable='true'></div>
				<div class='bloom-editable Credits-Page-style bloom-visibility-code-on' data-book='ISBN' lang='*' contenteditable='true'></div>
				<div data-languagetipcontent='español' style='' class='bloom-editable Credits-Page-style bloom-content3 bloom-contentNational2' data-book='ISBN' lang='es' contenteditable='true'></div>
				<div data-languagetipcontent='français' style='' class='bloom-editable Credits-Page-style bloom-content2 bloom-contentNational1' data-book='ISBN' lang='fr' contenteditable='true'></div>
			</div>
		</div>
		<div data-book='credits-page-branding-bottom-html' lang='*'><img class='branding' src='butterfly.png' alt=''></img></div>
	</div>
</body>
</html>";
			// Check counts in original HTML.
			var dom = new HtmlDom(html);
			var assertThatDom = AssertThatXmlIn.Dom(dom.RawDom);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 7);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es']", 3);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']", 3);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='z']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*']", 4);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='']", 1);
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang]", 19);

			// SUT
			PublishModel.RemoveUnwantedLanguageData(dom, new[] {"en"});

			// Check counts in modified HTML.
			assertThatDom.HasNoMatchForXpath("//div[@lang='de']");
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 7);	// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es']", 3);	// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']", 3);	// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='z']", 1);	// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='*']", 4);	// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='']", 1);	// unchanged
			assertThatDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang]", 19);
		}
	}
}
