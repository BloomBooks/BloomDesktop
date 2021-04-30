using System.IO;
using System.Xml;
using Bloom.Api;
using Bloom.Book;
using Moq;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.web
{
	[TestFixture]
	public class ReadersApiTests
	{
		private BloomServer _server;
		private BookSelection _bookSelection;

		[SetUp]
		public void Setup()
		{
			_bookSelection = new BookSelection();
			_bookSelection.SelectBook(new Bloom.Book.Book());
			_server = new BloomServer(_bookSelection);

			var controller = new ReadersApi(_bookSelection);
			controller.RegisterWithApiHandler(_server.ApiHandler);
		}

		[TearDown]
		public void TearDown()
		{
			_server.Dispose();
			_server = null;
		}

		[Test]
		public void IsReceivingApiCalls()
		{
			var result = ApiTest.GetString(_server,"readers/io/test");
			Assert.That(result, Is.EqualTo("OK"));
		}

		[Test]
		public void GetTextOfContentPagesAsJson_OmitsUnwantedPages()
		{
			var htmlLeveledReader = $@"<html><head><meta charset='UTF-8'></meta></head>
<body class='leveled-reader' data-l1='en' data-l2='fr' data-l3='es'>
    <div class='bloom-page cover coverColor bloom-frontMatter frontCover outsideFrontCover A5Portrait side-right' data-page='required singleton' data-export='front-matter-cover' data-xmatter-page='frontCover' id='b8408838-e9ed-4d76-bb8a-24ad0708b329' lang='fr' data-page-number=''>
        <div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Front Cover' data-after-content='Device Front/Back Matter' lang='en'>Front Cover</div>
        <div class='marginBox'>
            <div class='bloom-translationGroup bookTitle' data-default-languages='V,N1'>
                <div class='bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-padForOverflow bloom-content1 bloom-visibility-code-on' data-book='bookTitle' style='padding-bottom: 0px;' data-languagetipcontent='English' tabindex='0' spellcheck='true' role='textbox' aria-label='false' data-hasqtip='true' aria-describedby='qtip-0' lang='en' contenteditable='true'>
                    <p>Level Two</p>
                </div>
            </div>
            <div class='bloom-imageContainer' title='Name: aor_IN16.png Size: 25.28 kb Dots: 900 x 894 For the current paper size: • The image container is 444 x 511 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ⚠ This image would print at 195 DPI. • An image with 1388 x 1597 dots would fill this container at 300 DPI.'>
                <img data-book='coverImage' src='aor_IN16.png' data-copyright='Copyright SIL International 2009' data-creator='' data-license='cc-by-sa' alt=''></img>
                <div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement' data-default-languages='auto'>
                    <div class='bloom-editable ImageDescriptionEdit-style bloom-content1 bloom-visibility-code-on' data-book='coverImageDescription' data-languagetipcontent='English' tabindex='0' spellcheck='true' role='textbox' aria-label='false' lang='en' contenteditable='true'>cat</div>
                </div>
            </div>
        </div>
    </div>
    <div class='bloom-page bloom-noreader bloom-nonprinting screen-only A5Portrait bloom-monolingual side-left' id='33b7e4c5-6cd1-4611-a50f-85837143cbf6' data-page='' data-pagelineage='d054bb76-dc6b-4452-a4ab-f4b83ffa10cc' data-page-number='' lang=''>
        <div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Translation Instructions' lang='en'>
            Translation Instructions
        </div>
        <div class='marginBox'>
            <div class='split-pane-component-inner'>
                <div class='bloom-translationGroup' data-default-languages='*'>
                    <div class='bloom-editable bloom-noAudio bloom-visibility-code-on' data-book='' lang='*' contenteditable='true'>
                        <p>Instructions in some unknown language ...</p>
                    </div>
                </div>
            </div>
        </div>
    </div>
    <div class='bloom-page numberedPage customPage A5Portrait side-right bloom-monolingual' data-page='' id='a2ecb8be-5c7f-440d-9ef5-d503476211cd' data-pagelineage='adcd48df-e9ab-4a07-afd4-6a24d0398385' data-page-number='1' lang=''>
        <div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Just a Picture' lang='en'>Just a Picture</div>
        <div class='marginBox'>
            <div class='split-pane-component-inner'>
                <div class='bloom-imageContainer' title='Name: aor_acc034m.png Size: 72.13 kb Dots: 1239 x 1500 For the current paper size: • The image container is 402 x 677 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ⚠ This image would print at 296 DPI. • An image with 1257 x 2116 dots would fill this container at 300 DPI.'>
                    <img src='aor_acc034m.png' alt='cat staring at something outside the picture' data-copyright='Copyright SIL International 2009' data-creator='' data-license='cc-by-sa'></img>
                    <div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement'>
                        <div class='bloom-editable ImageDescriptionEdit-style bloom-content1 bloom-visibility-code-on' data-languagetipcontent='English' style='min-height: 24px;' tabindex='0' spellcheck='true' role='textbox' aria-label='false' lang='en' contenteditable='true'>
                            <p>cat staring at something outside the picture</p>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
    <div class='bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-left bloom-monolingual' data-page='' id='85a320a4-b73f-4149-87a1-9a1297ef04b0' data-pagelineage='adcd48df-e9ab-4a07-afd4-6a24d0398382' data-page-number='2' lang=''>
        <div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Basic Text &amp; Picture' lang='en'>Basic Text &amp; Picture</div>
        <div class='marginBox'>
            <div class='split-pane horizontal-percent' style='min-height: 42px;'>
                <div class='split-pane-component position-top' style='bottom: 50%'>
                    <div class='split-pane-component-inner'>
                        <div class='bloom-imageContainer bloom-leadingElement' title='Name: aor_Cat3.png Size: 50.04 kb Dots: 1500 x 1248 For the current paper size: • The image container is 402 x 334 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ✓ This image would print at 359 DPI. • An image with 1257 x 1044 dots would fill this container at 300 DPI.'>
                            <img src='aor_Cat3.png' alt='cat lying down and staring at you through slitted eyes' data-copyright='Copyright SIL International 2009' data-creator='' data-license='cc-by-sa'></img>
                            <div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement'>
                                <div class='bloom-editable ImageDescriptionEdit-style bloom-content1 bloom-visibility-code-on' data-languagetipcontent='English' style='min-height: 24px;' tabindex='0' spellcheck='true' role='textbox' aria-label='false' lang='en' contenteditable='true'>
                                    <p>cat lying down and staring at you through slitted eyes</p>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
                <div class='split-pane-divider horizontal-divider' style='bottom: 50%'></div>
                <div class='split-pane-component position-bottom' style='height: 50%'>
                    <div class='split-pane-component-inner'>
                        <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto'>
                            <div class='bloom-editable normal-style audio-sentence bloom-content1 bloom-visibility-code-on' style='min-height: 24px;' tabindex='0' spellcheck='true' role='textbox' aria-label='false' data-audiorecordingmode='TextBox' id='i57437cd1-c55c-499e-b0c3-d7195fba5f11' data-languagetipcontent='English' lang='en' contenteditable='true'>
                                <p>A cat stares at you.</p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
    <div class='bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual' data-page='' id='d46e4259-2a99-4197-b21d-bf97a992b7d0' data-pagelineage='adcd48df-e9ab-4a07-afd4-6a24d0398382' data-page-number='3' lang=''>
        <div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Basic Text &amp; Picture' lang='en'>Basic Text &amp; Picture</div>
        <div class='marginBox'>
            <div class='split-pane horizontal-percent' style='min-height: 42px;'>
                <div class='split-pane-component position-top' style='bottom: 50%'>
                    <div class='split-pane-component-inner'>
                        <div class='bloom-imageContainer bloom-leadingElement' title='Name: aor_ACC029M.png Size: 39.65 kb Dots: 1500 x 806 For the current paper size: • The image container is 402 x 334 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ✓ This image would print at 358 DPI. • An image with 1257 x 1044 dots would fill this container at 300 DPI.'>
                            <img src='aor_ACC029M.png' alt='cat sleeping in a box that is just large enough' data-copyright='Copyright SIL International 2009' data-creator='' data-license='cc-by-sa'></img>
                            <div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement'>
                                <div class='bloom-editable ImageDescriptionEdit-style bloom-content1 bloom-visibility-code-on' data-languagetipcontent='English' style='min-height: 24px;' tabindex='0' spellcheck='true' role='textbox' aria-label='false' lang='en' contenteditable='true'>
                                    <p>cat sleeping in a box that is just large enough</p>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
                <div class='split-pane-divider horizontal-divider' style='bottom: 50%'></div>
                <div class='split-pane-component position-bottom' style='height: 50%'>
                    <div class='split-pane-component-inner'>
                        <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto'>
                            <div class='bloom-editable normal-style bloom-content1 bloom-visibility-code-on' style='min-height: 24px;' tabindex='0' spellcheck='true' role='textbox' aria-label='false' data-languagetipcontent='English' lang='en' contenteditable='true'>
                                <p>The cat is sleeping.</p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
    <div class='bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-left bloom-monolingual' data-page='' id='5a424678-ec70-4c97-a547-015ff38dfd11' data-pagelineage='adcd48df-e9ab-4a07-afd4-6a24d0398382' data-page-number='4' lang=''>
        <div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Basic Text &amp; Picture' lang='en'>Basic Text &amp; Picture</div>
        <div class='marginBox'>
            <div class='split-pane horizontal-percent' style='min-height: 42px;'>
                <div class='split-pane-component position-top' style='bottom: 50%'>
                    <div class='split-pane-component-inner'>
                        <div class='bloom-imageContainer bloom-leadingElement' title='Name: aor_acc14.png Size: 27.47 kb Dots: 1500 x 1020 For the current paper size: • The image container is 402 x 334 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ✓ This image would print at 358 DPI. • An image with 1257 x 1044 dots would fill this container at 300 DPI.'>
                            <img src='aor_acc14.png' alt='two kittens sitting together' data-copyright='Copyright SIL International 2009' data-creator='' data-license='cc-by-sa'></img>
                            <div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement'>
                                <div class='bloom-editable ImageDescriptionEdit-style bloom-content1 bloom-visibility-code-on' data-languagetipcontent='English' style='min-height: 24px;' tabindex='0' spellcheck='true' role='textbox' aria-label='false' lang='en' contenteditable='true'>
                                    <p>two kittens sitting together</p>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
                <div class='split-pane-divider horizontal-divider' style='bottom: 50%'></div>
                <div class='split-pane-component position-bottom' style='height: 50%'>
                    <div class='split-pane-component-inner'>
                        <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto'>
                            <div class='bloom-editable normal-style bloom-content1 bloom-visibility-code-on' style='min-height: 24px;' tabindex='0' spellcheck='true' role='textbox' aria-label='false' data-languagetipcontent='English' lang='en' contenteditable='true'>
                                <p>See the two kittens.</p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
    <div class='bloom-page numberedPage customPage A5Portrait side-left bloom-monolingual' data-page='' id='ebad7a47-05fa-4e6d-a7a1-cb526bb5efb8' data-pagelineage='5dcd48df-e9ab-4a07-afd4-6a24d0398386' data-page-number='12' lang=''>
        <div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Custom' lang='en'>Custom</div>
        <div class='marginBox'>
            <div class='split-pane horizontal-percent' style='min-height: 42px;'>
                <div class='split-pane-component position-top' style='bottom: 66.7647%;'>
                    <div class='split-pane-component-inner' min-width='60px 150px 250px' min-height='60px 150px'>
                        <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='N1'>
                            <div class='bloom-editable normal-style bloom-contentNational1 bloom-visibility-code-on' style='min-height: 24px;' tabindex='0' spellcheck='true' role='textbox' aria-label='false' data-languagetipcontent='français' lang='fr' contenteditable='true'>
                                <p>This is in the national language.</p>
                            </div>
                        </div>
                    </div>
                </div>
                <div class='split-pane-divider horizontal-divider' title='33.2%' style='bottom: 66.7647%;'></div>
                <div class='split-pane-component position-bottom' style='height: 66.7647%;'>
                    <div class='split-pane horizontal-percent' style='min-height: 42px;'>
                        <div class='split-pane-component position-top'>
                            <div class='split-pane-component-inner' min-width='60px 150px 250px' min-height='60px 150px'>
                                <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='N2'>
                                    <div class='bloom-editable normal-style bloom-contentNational2 bloom-visibility-code-on' style='min-height: 24px;' data-languagetipcontent='español' lang='es' contenteditable='true'>
                                        <p>This is in the regional language.</p>
                                    </div>
                                </div>
                            </div>
                        </div>
                        <div class='split-pane-divider horizontal-divider'></div>
                        <div class='split-pane-component position-bottom'>
                            <div class='split-pane-component-inner' min-width='60px 150px 250px' min-height='60px 150px'>
                                <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='V'>
                                    <div class='bloom-editable normal-style bloom-content1 bloom-visibility-code-on' data-languagetipcontent='English' style='min-height: 24px;' tabindex='0' spellcheck='true' role='textbox' aria-label='false' lang='en' contenteditable='true'>
                                        <p>This is in the local language.</p>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
    <div class='bloom-page titlePage bloom-backMatter A5Portrait side-right' data-page='required singleton' data-xmatter-page='titlePage' id='0f8d4d80-0519-42b6-bcde-6362b99ed1ff' lang='fr' data-export='front-matter-title-page' data-page-number=''>
        <div class='pageLabel' lang='en' data-i18n='TemplateBooks.PageLabel.Title Page'>Title Page</div>
        <div class='marginBox'>
            <div class='bloom-translationGroup' data-default-languages='V,N1' id='titlePageTitleBlock'>
                <div class='bloom-editable bloom-nodefaultstylerule Title-On-Title-Page-style bloom-padForOverflow bloom-content1 bloom-visibility-code-on' lang='en' contenteditable='true' data-book='bookTitle' style='padding-bottom: 0px;'>
                    <p>Level Two</p>
                </div>
            </div>
            <div class='largeFlexGap'></div>
            <div class='bloom-translationGroup' data-default-languages='N1' id='originalContributions'>
                <div class='bloom-editable credits bloom-copyFromOtherLanguageIfNecessary Content-On-Title-Page-style bloom-contentNational1 bloom-visibility-code-on' lang='fr' contenteditable='true' data-book='originalContributions'></div>
            </div>
            <div class='smallFlexGap'></div>
        </div>
    </div>
    <div class='bloom-page bloom-backMatter theEndPage A5Portrait side-left' data-page='required singleton' data-xmatter-page='endPage' id='7d758661-5e5e-41f1-b3c4-daff8884acf1' lang='fr' data-page-number=''>
        <div class='pageLabel' lang='en' data-i18n='TemplateBooks.PageLabel.The End'>The End</div>
        <div class='marginBox'>
            <div data-book='outside-back-cover-branding-top-html' lang='*'></div>
            <div data-book='outside-back-cover-branding-bottom-html' lang='*'><img class='portraitOnly branding' src='backPortrait.svg' alt=''></img><img class='landscapeOnly branding' src='backLandscape.svg' alt=''></img></div>
        </div>
    </div>
</body></html>
";
			var doc = new XmlDocument();
			doc.LoadXml(htmlLeveledReader);
			var dom = new HtmlDom(doc);
			var storage = CreateMockStorage(dom, "GetPagesForReader");
			var book = new Bloom.Book.Book(storage.Object.BookInfo, storage.Object);
			_bookSelection.SelectBook(book);

			var result = ApiTest.GetString(_server, "readers/io/textOfContentPages");
			Assert.That(result, Is.EqualTo("{\"85a320a4-b73f-4149-87a1-9a1297ef04b0\":\"A cat stares at you.\",\"d46e4259-2a99-4197-b21d-bf97a992b7d0\":\"The cat is sleeping.\",\"5a424678-ec70-4c97-a547-015ff38dfd11\":\"See the two kittens.\"}"));
		}

		Mock<IBookStorage> CreateMockStorage(HtmlDom htmlDom, string subfolder)
		{
			var tempFolderPath = Path.Combine(Path.GetTempPath(), subfolder);
			var storage = new Mock<IBookStorage>();
			storage.Setup(x => x.GetLooksOk()).Returns(true);

			storage.SetupGet(x => x.Dom).Returns(htmlDom);
			storage.SetupGet(x => x.Key).Returns("testkey");
			storage.SetupGet(x => x.FileName).Returns("testTitle");
			storage.Setup(x => x.GetRelocatableCopyOfDom()).Returns(() => storage.Object.Dom.Clone());// review: the real thing does more than just clone
			storage.Setup(x => x.MakeDomRelocatable(It.IsAny<HtmlDom>())).Returns(
				(HtmlDom x) => { return x.Clone(); });// review: the real thing does more than just clone

			var fileLocator = new Moq.Mock<IFileLocator>();
			storage.Setup(x => x.GetFileLocator()).Returns(() => fileLocator.Object);

			storage.SetupGet(x => x.FolderPath).Returns(tempFolderPath);// review: the real thing does more than just clone
			var metadata = new BookInfo(tempFolderPath, true);
			storage.SetupGet(x => x.BookInfo).Returns(metadata);
			storage.Setup(x => x.HandleRetiredXMatterPacks(It.IsAny<HtmlDom>(), It.IsAny<string>()))
				.Returns((HtmlDom dom, string y) => { return y == "BigBook" ? "Factory" : y; });
			return storage;
		}
	}
}
