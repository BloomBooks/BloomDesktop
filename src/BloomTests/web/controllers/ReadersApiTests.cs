using System.IO;
using Bloom.Api;
using Bloom.Book;
using Bloom.SafeXml;
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

            var controller = new ReadersApi(_bookSelection, null);
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
            var result = ApiTest.GetString(_server, "readers/io/test");
            Assert.That(result, Is.EqualTo("OK"));
        }

        [Test]
        public void GetTextOfContentPagesAsJson_OmitsUnwantedPages()
        {
            var htmlLeveledReader =
                $@"<html><head><meta charset='UTF-8'></meta></head>
<body class='leveled-reader' data-l1='en' data-l2='fr' data-l3='es'>
    <!-- ignore page with bloom-frontMatter class -->
    <div class='bloom-page bloom-frontMatter' id='b8408838-e9ed-4d76-bb8a-24ad0708b329' lang='fr'>
        <div class='marginBox'>
            <div class='bloom-translationGroup bookTitle' data-default-languages='V,N1'>
                <div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='en'>
                    <p>Level Two</p>
                </div>
            </div>
            <div class='bloom-imageContainer' title='Name: aor_IN16.png'>
                <img src='aor_IN16.png'></img>
                <div class='bloom-translationGroup bloom-imageDescription'>
                    <div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='en'>cat</div>
                </div>
            </div>
        </div>
    </div>
    <!-- ignore page with bloom-nonprinting class -->
    <div class='bloom-page bloom-noreader bloom-nonprinting screen-only' id='33b7e4c5-6cd1-4611-a50f-85837143cbf6' lang=''>
        <div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Translation Instructions' lang='en'>Translation Instructions</div>
        <div class='marginBox'>
            <div class='split-pane-component-inner'>
                <div class='bloom-translationGroup' data-default-languages='*'>
                    <div class='bloom-editable bloom-content1 bloom-visibility-code-on' data-book='' lang='en' contenteditable='true'>
                        <p>Instructions in vernacular language ...</p>
                    </div>
                </div>
            </div>
        </div>
    </div>
    <!-- skip over page with no bloom-content1 content apart from image descriptions -->
	<div class='bloom-page numberedPage' id='a2ecb8be-5c7f-440d-9ef5-d503476211cd' data-page-number='1' lang=''>
        <div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Just a Picture' lang='en'>Just a Picture</div>
        <div class='marginBox'>
            <div class='split-pane-component-inner'>
                <div class='bloom-imageContainer' title='Name: aor_acc034m.png'>
                    <img src='aor_acc034m.png' alt='cat staring at something outside the picture'></img>
                    <div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement'>
                        <div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='en'>
                            <p>cat staring at something outside the picture</p>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
    <!-- include content page with bloom-content1 content, ignoring image description -->
    <div class='bloom-page numberedPage' id='85a320a4-b73f-4149-87a1-9a1297ef04b0' data-page-number='2' lang=''>
        <div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Basic Text &amp; Picture' lang='en'>Basic Text &amp; Picture</div>
        <div class='marginBox'>
            <div class='split-pane horizontal-percent'>
                <div class='split-pane-component position-top'>
                    <div class='split-pane-component-inner'>
                        <div class='bloom-imageContainer bloom-leadingElement' title='Name: aor_Cat3.png'>
                            <img src='aor_Cat3.png' alt='cat lying down and staring at you through slitted eyes'></img>
                            <div class='bloom-translationGroup bloom-imageDescription'>
                                <div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='en'>
                                    <p>cat lying down and staring at you through slitted eyes</p>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
                <div class='split-pane-divider horizontal-divider'></div>
                <div class='split-pane-component position-bottom'>
                    <div class='split-pane-component-inner'>
                        <div class='bloom-translationGroup' data-default-languages='auto'>
                            <div class='bloom-editable bloom-content1 bloom-visibility-code-on' id='i57437cd1-c55c-499e-b0c3-d7195fba5f11' lang='en'>
                                <p>A cat stares at you.</p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
    <!-- include content page with empty bloom-content1 content, ignoring image description -->
    <div class='bloom-page numberedPage' id='d46e4259-2a99-4197-b21d-bf97a992b7d0' data-page-number='3' lang=''>
        <div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Basic Text &amp; Picture' lang='en'>Basic Text &amp; Picture</div>
        <div class='marginBox'>
            <div class='split-pane horizontal-percent'>
                <div class='split-pane-component position-top'>
                    <div class='split-pane-component-inner'>
                        <div class='bloom-imageContainer' title='Name: aor_ACC029M.png'>
                            <img src='aor_ACC029M.png' alt='cat sleeping in a box that is just large enough'></img>
                            <div class='bloom-translationGroup bloom-imageDescription'>
                                <div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='en'>
                                    <p>cat sleeping in a box that is just large enough</p>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
                <div class='split-pane-divider horizontal-divider'></div>
                <div class='split-pane-component position-bottom'>
                    <div class='split-pane-component-inner'>
                        <div class='bloom-translationGroup' data-default-languages='auto'>
                            <div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='en'>
                                <p></p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
    <!-- include content page with bloom-content1 content and no image -->
    <div class='bloom-page numberedPage' id='5a424678-ec70-4c97-a547-015ff38dfd11' data-page-number='4' lang=''>
        <div class='marginBox'>
            <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto'>
                <div class='bloom-editable normal-style bloom-content1 bloom-visibility-code-on' lang='en'>
                    <p>See the two kittens.</p>
                </div>
            </div>
        </div>
    </div>
	<!-- include page with non-vernacular text if it also has vernacular text -->
    <div class='bloom-page numberedPage' id='ebbd7f47-05fa-4e6d-a7a1-cb526bb5efb8' data-page-number='5' lang=''>
        <div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Custom' lang='en'>Custom</div>
        <div class='marginBox'>
            <div class='split-pane horizontal-percent'>
                <div class='split-pane-component position-top'>
                    <div class='split-pane-component-inner' min-width='60px 150px 250px' min-height='60px 150px'>
                        <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='N1'>
                            <div class='bloom-editable bloom-contentNational1 bloom-visibility-code-on' lang='fr'>
                                <p>national language text</p>
                            </div>
                        </div>
                    </div>
                </div>
                <div class='split-pane-divider horizontal-divider' title='33.2%'></div>
                <div class='split-pane-component position-bottom'>
                    <div class='split-pane-component position-top'>
                        <div class='split-pane-component-inner' min-width='60px 150px 250px' min-height='60px 150px'>
                            <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='V'>
                                <div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='en'>
                                    <p>local language text</p>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
	<!-- ignore page with bloom-ignoreForReaderStats class -->
    <div class='bloom-page numberedPage bloom-ignoreForReaderStats' id='ebad7a47-05fa-4e6d-a7a1-cb526bb5efb8' data-page-number='6' lang=''>
        <div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Custom' lang='en'>Custom</div>
        <div class='marginBox'>
            <div class='split-pane horizontal-percent'>
                <div class='split-pane-component position-top'>
                    <div class='split-pane-component-inner' min-width='60px 150px 250px' min-height='60px 150px'>
                        <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='N1'>
                            <div class='bloom-editable normal-style bloom-contentNational1 bloom-visibility-code-on' lang='fr'>
                                <p>This is in the national language.</p>
                            </div>
                        </div>
                    </div>
                </div>
                <div class='split-pane-divider horizontal-divider' title='33.2%'></div>
                <div class='split-pane-component position-bottom'>
                    <div class='split-pane-component position-top'>
                        <div class='split-pane-component-inner' min-width='60px 150px 250px' min-height='60px 150px'>
                            <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='V'>
                                <div class='bloom-editable normal-style bloom-content1 bloom-visibility-code-on' lang='en'>
                                    <p>This is in the local language.</p>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
    <!-- ignore page with bloom-backMatter class -->
    <div class='bloom-page titlePage bloom-backMatter' id='0f8d4d80-0519-42b6-bcde-6362b99ed1ff' lang='fr'>
        <div class='pageLabel' lang='en' data-i18n='TemplateBooks.PageLabel.Title Page'>Title Page</div>
        <div class='marginBox'>
            <div class='bloom-translationGroup' data-default-languages='V,N1' id='titlePageTitleBlock'>
                <div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='en'>
                    <p>Level Two</p>
                </div>
            </div>
        </div>
    </div>
</body></html>
";
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(htmlLeveledReader);
            var dom = new HtmlDom(doc);
            var storage = CreateMockStorage(dom, "GetPagesForReader");
            var book = new Bloom.Book.Book(storage.Object.BookInfo, storage.Object);
            _bookSelection.SelectBook(book);

            var result = ApiTest.GetString(_server, "readers/io/textOfContentPages");
            Assert.That(
                result,
                Is.EqualTo(
                    "{\"85a320a4-b73f-4149-87a1-9a1297ef04b0\":\"<p>A cat stares at you.</p>\",\"d46e4259-2a99-4197-b21d-bf97a992b7d0\":\"<p />\",\"5a424678-ec70-4c97-a547-015ff38dfd11\":\"<p>See the two kittens.</p>\",\"ebbd7f47-05fa-4e6d-a7a1-cb526bb5efb8\":\"<p>local language text</p>\"}"
                )
            );
        }

        Mock<IBookStorage> CreateMockStorage(HtmlDom htmlDom, string subfolder)
        {
            var tempFolderPath = Path.Combine(Path.GetTempPath(), subfolder);
            var storage = new Mock<IBookStorage>();
            storage.Setup(x => x.GetLooksOk()).Returns(true);

            storage.SetupGet(x => x.Dom).Returns(htmlDom);
            storage.SetupGet(x => x.Key).Returns("testkey");
            storage
                .Setup(x => x.GetRelocatableCopyOfDom(true))
                .Returns(() => storage.Object.Dom.Clone()); // review: the real thing does more than just clone
            storage
                .Setup(x => x.MakeDomRelocatable(It.IsAny<HtmlDom>()))
                .Returns(
                    (HtmlDom x) =>
                    {
                        return x.Clone();
                    }
                ); // review: the real thing does more than just clone

            var fileLocator = new Moq.Mock<IFileLocator>();
            storage.Setup(x => x.GetFileLocator()).Returns(() => fileLocator.Object);

            storage.SetupGet(x => x.FolderPath).Returns(tempFolderPath); // review: the real thing does more than just clone
            var metadata = new BookInfo(tempFolderPath, true);
            storage.SetupGet(x => x.BookInfo).Returns(metadata);
            storage
                .Setup(x => x.HandleRetiredXMatterPacks(It.IsAny<HtmlDom>(), It.IsAny<string>()))
                .Returns(
                    (HtmlDom dom, string y) =>
                    {
                        return y == "BigBook" ? "Factory" : y;
                    }
                );
            return storage;
        }
    }
}
