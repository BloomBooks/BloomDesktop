using System.Linq;
using System.Xml;
using Bloom.Book;
using Bloom.SafeXml;
using NUnit.Framework;

namespace BloomTests.Publish
{
    [TestFixture]
    // This class implements what is conceptually one or two tests in the ExportEpubTests group.
    // But as there are many different outcomes to verify, and a much more complicated book to
    // create, it's cleaner to make a distinct class, and do the complete book creation setup in
    // OneTimeSetup.
    public class ExportEpubWithVideoTests : ExportEpubTestsBaseClass
    {
        [OneTimeSetUp]
        public override void OneTimeSetup()
        {
            base.OneTimeSetup();
            base.Setup(); // since this class represents just one test, we can do it here.

            var book = SetupBookLong(
                "This is some text",
                "xyz",
                " bloom-frontMatter frontCover' data-page='required singleton",
                optionalDataDiv: @"
<div id='bloomDataDiv'>
	<div data-book='contentLanguage1' lang='*'>xyz</div>
	<div data-book='contentLanguage2' lang='*'>en</div>
	<div data-book='coverImage' lang='*' src='DevilsSlide.png' data-copyright='Copyright © 2015, Stephen McConnel' data-creator='Stephen McConnel' data-license='cc-by-sa'>DevilsSlide.png</div>
	<div data-book='coverImageDescription' lang='xyz'><p>Photograph of a rock formation with two parallel spines coming down a hill</p></div>
	<div data-book='bookTitle' lang='xyz'><p>New Book</p></div>
	<div data-book='originalContributions' lang='en'>
		<p>words <em>carefully</em> chosen by Stephen McConnel</p>
		<p>Image on page Front Cover by Stephen McConnel, © 2012 Stephen McConnel. CC-BY-SA 4.0.</p>
	</div>
	<div data-book='copyright' lang='*'>Copyright © 2020, Dr. Steve Himself</div>
	<div data-book='licenseUrl' lang='*'>http://creativecommons.org/licenses/by/4.0/</div>
	<div data-book='licenseDescription' lang='xyz'>
		http://creativecommons.org/licenses/by/4.0/<br />You are free to make commercial use of this work. You may adapt and add to this work. You must keep the copyright and credits for authors, illustrators, etc.
	</div>
</div>
",
                // All pages have a box for a video above a box for text
                // page 1: video selected and present, with text
                // page 2: no video selected, with text
                // page 3: video selected and present, no text
                // page 4: video selected but missing/deleted, with text
                // page 5: no video selected, no text
                extraPages: @"
<div class='bloom-page numberedPage' data-page-number='1'>
	<div class='pageDescription' lang='xyz'></div>
	<div class='marginBox'>
		<div style='min-height: 42px;' class='split-pane horizontal-percent'>
			<div style='bottom: 30.6306%;' class='split-pane-component position-top'>
				<div min-height='60px 150px' min-width='60px 150px 250px' style='position: relative;' class='split-pane-component-inner'>
					<div class='box-header-off bloom-translationGroup'>
						<div data-languagetipcontent='Xyzzy' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='xyz' contenteditable='true'>
							<p></p>
						</div>
					</div>
					<div class='bloom-videoContainer bloom-leadingElement bloom-selected'>
						<video>
						<source src='video/a0c5c8dd-d84b-4bf6-9f53-c4bb5caf38d0.mp4' type='video/mp4'></source></video>
					</div>
				</div>
			</div>
			<div style='bottom: 30.6306%;' title='69.4%' class='split-pane-divider horizontal-divider'></div>
			<div style='height: 30.6306%;' class='split-pane-component position-bottom'>
				<div min-height='60px' min-width='60px 150px 250px' style='position: relative;' class='split-pane-component-inner adding'>
					<div class='box-header-off bloom-translationGroup'>
						<div data-languagetipcontent='Xyzzy' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='xyz' contenteditable='true'>
							<p></p>
						</div>
					</div>
					<div class='bloom-translationGroup bloom-trailingElement normal-style'>
						<div data-languagetipcontent='Xyzzy' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 32px;' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='xyz' contenteditable='true'>
							<p>This video shows me counting to five on my fingers.</p>
						</div>
					</div>
				</div>
			</div>
		</div>
	</div>
</div>
<div class='bloom-page numberedPage' data-page-number='2'>
	<div class='pageDescription' lang='xyz'></div>
	<div class='marginBox'>
		<div style='min-height: 42px;' class='split-pane horizontal-percent'>
			<div style='bottom: 30.6306%;' class='split-pane-component position-top'>
				<div min-height='60px 150px' min-width='60px 150px 250px' style='position: relative;' class='split-pane-component-inner'>
					<div class='box-header-off bloom-translationGroup'>
						<div data-languagetipcontent='Xyzzy' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='xyz' contenteditable='true'>
							<p></p>
						</div>
					</div>
					<div class='bloom-videoContainer bloom-leadingElement bloom-noVideoSelected bloom-selected'></div>
				</div>
			</div>
			<div style='bottom: 30.6306%;' title='69.4%' class='split-pane-divider horizontal-divider'></div>
			<div style='height: 30.6306%;' class='split-pane-component position-bottom'>
				<div min-height='60px' min-width='60px 150px 250px' style='position: relative;' class='split-pane-component-inner adding'>
					<div class='box-header-off bloom-translationGroup'>
						<div aria-label='false' role='textbox' spellcheck='true' tabindex='0' data-languagetipcontent='Xyzzy' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='xyz' contenteditable='true'>
							<p></p>
						</div>
					</div>
					<div class='bloom-translationGroup bloom-trailingElement normal-style'>
						<div aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 32px;' data-languagetipcontent='Xyzzy' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='xyz' contenteditable='true'>
							<p>This page has an empty spot for a video.  None has been selected yet.</p>
						</div>
					</div>
				</div>
			</div>
		</div>
	</div>
</div>
<div class='bloom-page numberedPage' data-page-number='3'>
	<div class='pageDescription' lang='xyz'></div>
	<div class='marginBox'>
		<div style='min-height: 42px;' class='split-pane horizontal-percent'>
			<div style='bottom: 30.6306%;' class='split-pane-component position-top'>
				<div min-height='60px 150px' min-width='60px 150px 250px' style='position: relative;' class='split-pane-component-inner'>
					<div class='box-header-off bloom-translationGroup'>
						<div data-languagetipcontent='Xyzzy' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='xyz' contenteditable='true'>
							<p></p>
						</div>
					</div>
					<div class='bloom-videoContainer bloom-leadingElement bloom-selected'>
						<video>
						<source src='video/importedvideo.mp4' type='video/mp4'></source></video>
					</div>
				</div>
			</div>
			<div style='bottom: 30.6306%;' title='69.4%' class='split-pane-divider horizontal-divider'></div>
			<div style='height: 30.6306%;' class='split-pane-component position-bottom'>
				<div min-height='60px' min-width='60px 150px 250px' style='position: relative;' class='split-pane-component-inner adding'>
					<div class='box-header-off bloom-translationGroup'>
						<div data-languagetipcontent='Xyzzy' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='xyz' contenteditable='true'>
							<p></p>
						</div>
					</div>
					<div class='bloom-translationGroup bloom-trailingElement normal-style'>
						<div data-languagetipcontent='Xyzzy' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 32px;' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='xyz' contenteditable='true'>
							<p></p>
						</div>
					</div>
				</div>
			</div>
		</div>
	</div>
</div>
<div class='bloom-page numberedPage' data-page-number='4'>
	<div class='pageDescription' lang='xyz'></div>
	<div class='marginBox'>
		<div style='min-height: 42px;' class='split-pane horizontal-percent'>
			<div style='bottom: 30.6306%;' class='split-pane-component position-top'>
				<div min-height='60px 150px' min-width='60px 150px 250px' style='position: relative;' class='split-pane-component-inner'>
					<div class='box-header-off bloom-translationGroup'>
						<div data-languagetipcontent='Xyzzy' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='xyz' contenteditable='true'>
							<p></p>
						</div>
					</div>
					<div class='bloom-videoContainer bloom-leadingElement bloom-selected'>
						<video>
						<source src='video/deletedvideo.mp4' type='video/mp4'></source></video>
					</div>
				</div>
			</div>
			<div style='bottom: 30.6306%;' title='69.4%' class='split-pane-divider horizontal-divider'></div>
			<div style='height: 30.6306%;' class='split-pane-component position-bottom'>
				<div min-height='60px' min-width='60px 150px 250px' style='position: relative;' class='split-pane-component-inner adding'>
					<div class='box-header-off bloom-translationGroup'>
						<div data-languagetipcontent='Xyzzy' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='xyz' contenteditable='true'>
							<p></p>
						</div>
					</div>
					<div class='bloom-translationGroup bloom-trailingElement normal-style'>
						<div data-languagetipcontent='Xyzzy' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 32px;' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='xyz' contenteditable='true'>
							<p>This page had a video selected, but it is now missing.</p>
						</div>
					</div>
				</div>
			</div>
		</div>
	</div>
</div>
<div class='bloom-page numberedPage' data-page-number='5'>
	<div class='pageDescription' lang='xyz'></div>
	<div class='marginBox'>
		<div style='min-height: 42px;' class='split-pane horizontal-percent'>
			<div style='bottom: 30.6306%;' class='split-pane-component position-top'>
				<div min-height='60px 150px' min-width='60px 150px 250px' style='position: relative;' class='split-pane-component-inner'>
					<div class='box-header-off bloom-translationGroup'>
						<div data-languagetipcontent='Xyzzy' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-content1 bloom-contentNational1 bloom-visibility-code-on' contenteditable='true' lang='xyz'>
							<p></p>
						</div>
					</div>
					<div class='bloom-videoContainer bloom-leadingElement bloom-noVideoSelected bloom-selected'></div>
				</div>
			</div>
			<div style='bottom: 30.6306%;' title='69.4%' class='split-pane-divider horizontal-divider'></div>
			<div style='height: 30.6306%;' class='split-pane-component position-bottom'>
				<div min-height='60px' min-width='60px 150px 250px' style='position: relative;' class='split-pane-component-inner adding'>
					<div class='box-header-off bloom-translationGroup'>
						<div data-languagetipcontent='Xyzzy' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-content1 bloom-contentNational1 bloom-visibility-code-on' contenteditable='true' lang='xyz'>
							<p></p>
						</div>
					</div>
					<div class='bloom-translationGroup bloom-trailingElement normal-style'>
						<div data-languagetipcontent='Xyzzy' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 32px;' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-content1 bloom-contentNational1 bloom-visibility-code-on' contenteditable='true' lang='xyz'>
							<p></p>
						</div>
					</div>
				</div>
			</div>
		</div>
	</div>
</div>
"
            );
            MakeImageFiles(book, "DevilsSlide");
            MakeVideoFiles(book, "a0c5c8dd-d84b-4bf6-9f53-c4bb5caf38d0", "importedvideo");
            // Without a branding, Bloom Enterprise-only features are removed
            var branding = "Test";
            // May need to try more than once on Linux to make the epub without an exception for failing to complete loading the document.
            MakeEpubWithRetries(
                kMakeEpubTrials,
                "output",
                "ExportEpubWithVideo",
                book,
                BookInfo.HowToPublishImageDescriptions.OnPage,
                branding
            );
            GetPageOneData();
            _ns = GetNamespaceManager();
        }

        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            base.TearDown(); // since we did Setup in OneTimeSetup
            base.OneTimeTearDown();
        }

        public override void Setup()
        {
            // do nothing; we call base.Setup() for this class in OneTimeSetup().
        }

        public override void TearDown()
        {
            // do nothing; we call base.TearDown() for this class in OneTimeTearDown().
        }

        [Test]
        public void CheckEpubBasics()
        {
            CheckBasicsInPage("DevilsSlide");
            CheckBasicsInManifest();
            CheckAccessibilityInManifest(false, true, false, _defaultSourceValue, false); // no sound files, but some image files
            CheckFolderStructure();
        }

        [Test]
        public void CheckPageWithVideoAndText()
        {
            var pageData = GetPageNData(2);
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(pageData);
            var list = doc.SafeSelectNodes(
                    "//div[contains(@class,'bloom-videoContainer')]/video/source"
                )
                .Cast<SafeXmlElement>();
            Assert.AreEqual(1, list.Count());
            Assert.AreEqual(
                kVideoSlash + "a0c5c8dd-d84b-4bf6-9f53-c4bb5caf38d0.mp4",
                list.First().GetAttribute("src")
            );
            list = doc.SafeSelectNodes("//div[contains(@class,'bloom-trailingElement')]")
                .Cast<SafeXmlElement>();
            Assert.AreEqual(1, list.Count());
            Assert.AreEqual(
                "This video shows me counting to five on my fingers.",
                list.First().InnerText.Trim()
            );
            CheckAccessibilityInManifest(false, true, true, _defaultSourceValue, false); // no sound files, but some image and video files
        }

        [Test]
        public void CheckPageWithTextButNoVideo()
        {
            var pageData = GetPageNData(3);
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(pageData);
            var list = doc.SafeSelectNodes(
                    "//div[contains(@class,'bloom-videoContainer')]/video/source"
                )
                .Cast<SafeXmlElement>();
            Assert.AreEqual(0, list.Count());
            list = doc.SafeSelectNodes("//div[contains(@class,'bloom-videoContainer')]")
                .Cast<SafeXmlElement>();
            Assert.AreEqual(1, list.Count());
            Assert.Contains("bloom-noVideoSelected", list.First().GetAttribute("class").Split(' '));
            list = doc.SafeSelectNodes("//div[contains(@class,'bloom-trailingElement')]")
                .Cast<SafeXmlElement>();
            Assert.AreEqual(1, list.Count());
            Assert.AreEqual(
                "This page has an empty spot for a video.  None has been selected yet.",
                list.First().InnerText.Trim()
            );
        }

        [Test]
        public void CheckPageWithVideoButNoText()
        {
            var pageData = GetPageNData(4);
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(pageData);
            var list = doc.SafeSelectNodes(
                    "//div[contains(@class,'bloom-videoContainer')]/video/source"
                )
                .Cast<SafeXmlElement>();
            Assert.AreEqual(1, list.Count());
            Assert.AreEqual(kVideoSlash + "importedvideo.mp4", list.First().GetAttribute("src"));
            list = doc.SafeSelectNodes("//div[contains(@class,'bloom-trailingElement')]")
                .Cast<SafeXmlElement>();
            Assert.AreEqual(1, list.Count());
            Assert.AreEqual("", list.First().InnerText.Trim());
            CheckAccessibilityInManifest(false, true, true, _defaultSourceValue, false); // no sound files, but some image and video files
        }

        [Test]
        public void CheckPageWithTextAndDeletedVideo()
        {
            var pageData = GetPageNData(5);
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(pageData);
            var list = doc.SafeSelectNodes(
                    "//div[contains(@class,'bloom-videoContainer')]/video/source"
                )
                .Cast<SafeXmlElement>();
            Assert.AreEqual(1, list.Count());
            Assert.AreEqual(kVideoSlash + "deletedvideo.mp4", list.First().GetAttribute("src"));
            list = doc.SafeSelectNodes("//div[contains(@class,'bloom-trailingElement')]")
                .Cast<SafeXmlElement>();
            Assert.AreEqual(1, list.Count());
            Assert.AreEqual(
                "This page had a video selected, but it is now missing.",
                list.First().InnerText.Trim()
            );
        }

        [Test]
        public void CheckPageWithNeitherVideoNorText()
        {
            // This page should not exist.  There should be a title page in its spot.
            var pageData = GetPageNData(6);
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(pageData);
            var list = doc.SafeSelectNodes(
                    "//div[contains(@class,'bloom-page') and contains(@class,'titlePage') and contains(@class,'bloom-backMatter')]"
                )
                .Cast<SafeXmlElement>();
            Assert.AreEqual(1, list.Count());
            Assert.AreEqual("Title Page", list.First().GetAttribute("aria-label"));
            Assert.AreEqual("contentinfo", list.First().GetAttribute("role"));

            var list2 = doc.SafeSelectNodes("//div[contains(@class, 'numberedPage')]")
                .Cast<SafeXmlElement>();
            Assert.AreEqual(0, list2.Count());
        }
    }
}
