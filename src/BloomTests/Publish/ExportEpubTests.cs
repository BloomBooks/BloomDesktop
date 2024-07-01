using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Bloom;
using Bloom.Book;
using Bloom.Publish;
using Bloom.Publish.Epub;
using Bloom.web.controllers;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;
using SIL.Extensions;
using SIL.PlatformUtilities;
using BloomBook = Bloom.Book.Book;

namespace BloomTests.Publish
{
    [TestFixture]
    public class ExportEpubTests : ExportEpubTestsBaseClass
    {
        public override void Setup()
        {
            base.Setup();
            _ns = ExportEpubTestsBaseClass.GetNamespaceManager();
            _bookServer = CreateBookServer();
        }

        [Test]
        public void HandlesWhiteSpaceInImageNames()
        {
#if __MonoCS__
            // This tests only the whitespace difference on Linux because filenames differing by case are different!
            var book = SetupBook("This is some text", "en", "my_Image", "my%20Image");
            MakeImageFiles(book, "my_Image", "my Image");
#else
            // These two names (my_Image and "my image") are especially interesting because they differ by case and also white space.
            // The case difference is not important to the Windows file system.
            // The white space must be removed to make an XML ID.
            var book = SetupBook("This is some text", "en", "my_Image", "my%20image");
            MakeImageFiles(book, "my_Image", "my image");
#endif
            MakeEpub("output", "HandlesWhiteSpaceInImageNames", book);
            CheckBasicsInPage();
#if __MonoCS__
            CheckBasicsInManifest("my_Image", "my_Image1");
            CheckBasicsInGivenPage(2, "my_Image", "my_Image1");
#else
            CheckBasicsInManifest("my_Image", "my_image1");
            CheckBasicsInGivenPage(2, "my_Image", "my_image1");
#endif
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data);
            CheckEpubTypeAttributes(page2Data, null);
            CheckNavPage();
            CheckFontStylesheet();
        }

        [Test]
        public void HandlesNonRomanFileNames()
        {
            // NonRoman filenames should not be changed.
            string nonRomanName = "ปูกับมด";
            var book = SetupBook("This is some text", "en", nonRomanName, "my%20image");
            MakeImageFiles(book, nonRomanName, "my image");
            MakeEpub(nonRomanName, "HandlesNonRomanFileNames", book);
            CheckBasicsInManifest(nonRomanName, "my_image");
            CheckBasicsInPage();
            CheckBasicsInGivenPage(2, nonRomanName, "my_image");
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data);
            CheckEpubTypeAttributes(page2Data, null);
            CheckNavPage();
            CheckFontStylesheet();
        }

        private BloomBook SetupBook(string text, string lang, params string[] images)
        {
            return SetupBookLong(text, lang, images: images);
        }

        // we were testing that not having bloom enterprise would cause images descriptions to be removed.
        // but now, image descriptions should never be removed: they no longer require bloom enterprise.
        [TestCase(BookInfo.HowToPublishImageDescriptions.None)]
        [TestCase(BookInfo.HowToPublishImageDescriptions.OnPage)]
        public void ImageDescriptions_NotBloomEnterprise_AreNotRemoved(
            BookInfo.HowToPublishImageDescriptions howToPublish
        )
        {
            var book = SetupBookLong(
                "This is a simple page",
                "xyz",
                images: new[] { "image1" },
                imageDescriptions: new[] { "This describes image 1" }
            );
            MakeEpub(
                "output",
                $"ImageDescriptions_NotBloomEnterprise_AreRemoved_{howToPublish}",
                book,
                howToPublish
            );
            var page2Data = GetPageNData(2);
            var assertThatPageTwoData = AssertThatXmlIn.String(page2Data);
            assertThatPageTwoData.HasSpecifiedNumberOfMatchesForXpath(
                "(//xhtml:div[contains(@class,'bloom-imageDescription')]|//xhtml:aside[contains(@class,'imageDescription')])",
                _ns,
                1
            );
            assertThatPageTwoData.HasAtLeastOneMatchForXpath(
                "(//xhtml:div[.='This describes image 1']|//xhtml:aside[.='This describes image 1'])",
                _ns
            );
        }

        // The original test here proved that we removed image descriptions when not displaying them.
        // But removing them completely broke the audio which needed text to be connected to it.
        // So now we verify that we _don't_ remove them (for Bloom Enterprise).
        [Test]
        public void ImageDescriptions_BloomEnterprise_HowToPublishImageDescriptionsNone_AreNotRemoved()
        {
            var book = SetupBookLong(
                "This is a simple page",
                "xyz",
                images: new[] { "image1" },
                imageDescriptions: new[] { "This describes image 1" }
            );
            MakeEpub(
                "output",
                "ImageDescriptions_BloomEnterprise_HowToPublishImageDescriptionsNone_AreNotRemoved",
                book,
                branding: "Test"
            );
            var page2Data = GetPageNData(2);
            AssertThatXmlIn
                .String(page2Data)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:div[contains(@class,'bloom-imageDescription')]",
                    _ns,
                    1
                );
        }

        // Also checks for handling of branding images, to avoid another whole epub creation.
        [Test]
        public void ImageDescriptions_BloomEnterprise_HowToPublishImageDescriptionsOnPage_ConvertedToAsides()
        {
            var book = SetupBookLong(
                "This is a simple page",
                "xyz",
                images: new[] { "image1" },
                imageDescriptions: new[] { "This describes image 1" },
                extraContentOutsideTranslationGroup: "<img class='branding' src='back-cover.png'/>"
            );
            MakeImageFiles(book, "back-cover");
            MakeEpub(
                "output",
                "ImageDescriptions_BloomEnterprise_HowToPublishImageDescriptionsOnPage_ConvertedToAsides",
                book,
                BookInfo.HowToPublishImageDescriptions.OnPage,
                branding: "Test"
            );
            var page2Data = GetPageNData(2);
            var assertThatPageTwoData = AssertThatXmlIn.String(page2Data);
            assertThatPageTwoData.HasNoMatchForXpath(
                "//xhtml:div[contains(@class,'bloom-imageDescription')]",
                _ns
            );
            assertThatPageTwoData.HasSpecifiedNumberOfMatchesForXpath(
                "//xhtml:div[@class='marginBox']/xhtml:div/xhtml:aside[.='This describes image 1']",
                _ns,
                1
            );
            assertThatPageTwoData.HasSpecifiedNumberOfMatchesForXpath(
                "//xhtml:img[@class='branding' and (@alt='' or @alt='Logo of the book sponsors') and @role='presentation']",
                _ns,
                1
            );
        }

        [TestCase(TalkingBookApi.AudioRecordingMode.Sentence)]
        [TestCase(TalkingBookApi.AudioRecordingMode.TextBox)]
        public void ImageDescriptions_BloomEnterprise_HowToPublishImageDescriptionsOnPage_ConvertedToAsidesCorrectlyOrdered(
            TalkingBookApi.AudioRecordingMode audioRecordingMode
        )
        {
            string extraContentOutsideTranslationGroup =
                @"<div title='image1.png' class='bloom-imageContainer'>
					<img src='image1.png' />
					<div class='bloom-translationGroup bloom-imageDescription'>
						<div class='bloom-editable bloom-contentNational2 bloom-visibility-code-on' lang='fr'>
							<p><span id='frdescguid' class='audio-sentence'>French image description</span></p>
						</div>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='xyz'>
							<p><span id='xyzdescguid' class='audio-sentence'>Vernacular image description</span></p>
						</div>
						<div class='bloom-editable bloom-contentNational3' lang='xunk'>
							<p><span id='nondescguid' class='audio-sentence'>Non-selected image description</span></p>
						</div>
						<div class='bloom-editable bloom-contentNational1 bloom-visibility-code-on' lang='en'>
							<p><span id='engdescguid' class='audio-sentence'>English image description</span></p>
						</div>
					</div>
				</div>";
            if (audioRecordingMode == TalkingBookApi.AudioRecordingMode.TextBox)
            {
                extraContentOutsideTranslationGroup =
                    @"<div title='image1.png' class='bloom-imageContainer'>
					<img src='image1.png' />
					<div class='bloom-translationGroup bloom-imageDescription'>
						<div class='bloom-editable bloom-contentNational2 bloom-visibility-code-on audio-sentence' id='frdescguid'  lang='fr'>
							<p>French image description</p>
						</div>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on audio-sentence' id='xyzdescguid' lang='xyz'>
							<p>Vernacular image description</p>
						</div>
						<div class='bloom-editable bloom-contentNational3 audio-sentence' id='nondescguid' lang='xunk'>
							<p>Non-selected image description</p>
						</div>
						<div class='bloom-editable bloom-contentNational1 bloom-visibility-code-on audio-sentence' id='engdescguid' lang='en'>
							<p>English image description</p>
						</div>
					</div>
				</div>";
            }

            var book = SetupBookLong(
                "This is a more complicated page page",
                "xyz",
                optionalDataDiv: // activate all 3 lgs
                @"<div id='bloomDataDiv'>
						<div data-book='contentLanguage1' lang='*'>xyz</div>
						<div data-book='contentLanguage2' lang='*'>en</div>
						<div data-book='contentLanguage3' lang='*'>fr</div>
					</div>",
                extraContentOutsideTranslationGroup: extraContentOutsideTranslationGroup,
                createPhysicalFile: true
            );
            MakeImageFiles(book, "image1"); // otherwise the img tag gets stripped out
            MakeEpub(
                "output",
                $"ImageDescriptions_BloomEnterprise_HowToPublishImageDescriptionsOnPage_ConvertedToAsidesCorrectlyOrdered_{audioRecordingMode}",
                book,
                BookInfo.HowToPublishImageDescriptions.OnPage,
                branding: "Test"
            );
            // MakeEpub (when using a physical file as we are) creates Device Xmatter, so usually page one is cover, page 2 is ours.
            // If we go to default theme, the cover will be empty and get deleted, and we'll need to test page 1.
            var assertThatPageOneData = AssertThatXmlIn.String(GetPageNData(2));
            assertThatPageOneData.HasNoMatchForXpath(
                "//xhtml:div[contains(@class,'bloom-imageDescription')]",
                _ns
            );
            assertThatPageOneData.HasSpecifiedNumberOfMatchesForXpath(
                "//xhtml:div[@class='marginBox']/xhtml:div/xhtml:aside",
                _ns,
                3
            );
            assertThatPageOneData.HasNoMatchForXpath(
                "//xhtml:div[@class='marginBox']/xhtml:div/xhtml:aside[.='Non-selected image description']",
                _ns
            );
            assertThatPageOneData.HasSpecifiedNumberOfMatchesForXpath(
                "//xhtml:div[@class='asideContainer']/xhtml:aside[1][.='Vernacular image description']",
                _ns,
                1
            );
            assertThatPageOneData.HasSpecifiedNumberOfMatchesForXpath(
                "//xhtml:div[@class='asideContainer']/xhtml:aside[2][.='English image description']",
                _ns,
                1
            );
            assertThatPageOneData.HasSpecifiedNumberOfMatchesForXpath(
                "//xhtml:div[@class='asideContainer']/xhtml:aside[3][.='French image description']",
                _ns,
                1
            );

            // since this test creates actual image files, we can test the AriaAccessibilityMarkup
            assertThatPageOneData.HasSpecifiedNumberOfMatchesForXpath(
                "//xhtml:img[@id='bookfig1']",
                _ns,
                1
            );
            assertThatPageOneData.HasSpecifiedNumberOfMatchesForXpath(
                "//xhtml:img[@alt='Vernacular image description']",
                _ns,
                1
            );

            // Some day, we want this to be the commented line instead, but there is a bug in Ace's handling of aria-describedby.
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-6426.
            //assertThatPageOneData.HasSpecifiedNumberOfMatchesForXpath("//xhtml:img[@aria-describedby='figdesc1.0 figdesc1.1 figdesc1.2']", _ns, 1);
            assertThatPageOneData.HasSpecifiedNumberOfMatchesForXpath(
                "//xhtml:img[@aria-describedby='figdesc1.0']",
                _ns,
                1
            );

            assertThatPageOneData.HasSpecifiedNumberOfMatchesForXpath(
                "//xhtml:div[@class='asideContainer']/xhtml:aside[1][@id='figdesc1.0']",
                _ns,
                1
            );
            assertThatPageOneData.HasSpecifiedNumberOfMatchesForXpath(
                "//xhtml:div[@class='asideContainer']/xhtml:aside[2][@id='figdesc1.1']",
                _ns,
                1
            );
            assertThatPageOneData.HasSpecifiedNumberOfMatchesForXpath(
                "//xhtml:div[@class='asideContainer']/xhtml:aside[3][@id='figdesc1.2']",
                _ns,
                1
            );
        }

        [Test]
        public void NavPageHasLangAttr()
        {
            var book = SetupBook("This is some text", "en");
            MakeEpub("NavPageHasLangAttr", "NavPageHasLangAttr", book);
            var navPageData = CheckNavPage();
            AssertThatXmlIn
                .String(navPageData)
                .HasSpecifiedNumberOfMatchesForXpath("xhtml:html[@lang='en']", _ns, 1);
        }

        private string CheckNavPage()
        {
            XNamespace opf = "http://www.idpf.org/2007/opf";

            var navPage = _manifestDoc.Root
                .Element(opf + "manifest")
                .Elements(opf + "item")
                .Last()
                .Attribute("href")
                .Value;
            var navPageData = StripXmlHeader(
                ExportEpubTestsBaseClass.GetZipContent(
                    _epub,
                    Path.GetDirectoryName(_manifestFile) + "/" + navPage
                )
            );
            AssertThatXmlIn
                .String(navPageData)
                .HasAtLeastOneMatchForXpath(
                    "xhtml:html/xhtml:body/xhtml:nav[@epub:type='toc' and @id='toc']/xhtml:ol/xhtml:li/xhtml:a[@href='1.xhtml']",
                    _ns
                );
            return navPageData; // in case the test wants more extensive checking
        }

        private void CheckFontStylesheet()
        {
            var fontCssData = ExportEpubTestsBaseClass.GetZipContent(
                _epub,
                "content/" + EpubMaker.kCssFolder + "/fonts.css"
            );
            Assert.That(
                fontCssData,
                Does.Contain(
                    "@font-face {font-family:'Andika'; font-weight:normal; font-style:normal; src:url('../"
                        + EpubMaker.kFontsFolder
                        + "/Andika-Regular.woff2') format('woff2');}"
                )
            );
            // Currently we're not embedding bold and italic fonts (BL-4202)
            //Assert.That(fontCssData,
            //	Does.Contain(
            //		"@font-face {font-family:'Andika'; font-weight:bold; font-style:normal; src:url(Andika-Bold.woff2) format('woff2');}"));
            //Assert.That(fontCssData,
            //	Does.Contain(
            //		"@font-face {font-family:'Andika'; font-weight:normal; font-style:italic; src:url(Andika-Italic.woff2) format('woff2');}"));
            //Assert.That(fontCssData,
            //	Does.Contain(
            //		"@font-face {font-family:'Andika'; font-weight:bold; font-style:italic; src:url(Andika-BoldItalic.woff2) format('woff2');}"));
        }

        private void CheckPageBreakMarker(
            string pageData,
            string pageId = "pg1",
            string pageLabel = "1"
        )
        {
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/span[@role='doc-pagebreak' and @id='"
                        + pageId
                        + "' and @aria-label='"
                        + pageLabel
                        + "']",
                    1
                );
        }

        private void CheckEpubTypeAttributes(
            string currentPage,
            string pageType,
            params string[] otherEpubTypeValues
        )
        {
            if (String.IsNullOrEmpty(pageType))
                AssertThatXmlIn
                    .String(currentPage)
                    .HasSpecifiedNumberOfMatchesForXpath("//xhtml:body/xhtml:section", _ns, 0);
            else
                AssertThatXmlIn
                    .String(currentPage)
                    .HasSpecifiedNumberOfMatchesForXpath(
                        "//xhtml:body/xhtml:section[@epub:type='" + pageType + "']",
                        _ns,
                        1
                    );

            AssertThatXmlIn
                .String(currentPage)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:div[@epub:type]",
                    _ns,
                    otherEpubTypeValues.Count()
                );
            foreach (var val in otherEpubTypeValues)
                AssertThatXmlIn
                    .String(currentPage)
                    .HasSpecifiedNumberOfMatchesForXpath(
                        "//xhtml:div[@epub:type='" + val + "']",
                        _ns,
                        1
                    );
        }

        /// <summary>
        /// Make an ePUB out of the specified book. Sets up several instance variables with commonly useful parts of the results.
        /// </summary>
        /// <returns></returns>
        protected ZipFile MakeEpub(
            string mainFileName,
            string folderName,
            BloomBook book,
            BookInfo.HowToPublishImageDescriptions howToPublishImageDescriptions =
                BookInfo.HowToPublishImageDescriptions.None,
            string branding = "Default",
            Action<EpubMaker> extraInit = null
        )
        {
            // May need to try more than once on Linux to make the epub without an exception for failing to complete loading the document.
            var result = MakeEpubWithRetries(
                kMakeEpubTrials,
                mainFileName,
                folderName,
                book,
                howToPublishImageDescriptions,
                branding,
                extraInit
            );
            GetPageOneData();
            return result;
        }

        [TestCase(TalkingBookApi.AudioRecordingMode.Sentence)]
        [TestCase(TalkingBookApi.AudioRecordingMode.TextBox)]
        public void Missing_Audio_Ignored(TalkingBookApi.AudioRecordingMode audioRecordingMode)
        {
            string[] images = new string[] { "1my$Image", "my%20image" };
            BloomBook book;
            switch (audioRecordingMode)
            {
                case TalkingBookApi.AudioRecordingMode.Sentence:
                    // Similar input as the basic SaveAudio, (also verifies that IDs are really adjusted), but this time we don't create one of the expected audio files.
                    book = SetupBook(
                        "<p><span class='audio-sentence' id='e993d14a-0ec3-4316-840b-ac9143d59a2c'>This is some text.</span><span class='audio-sentence' id='i0d8e9910-dfa3-4376-9373-a869e109b763'>Another sentence</span></p>",
                        "xyz",
                        images
                    );
                    break;
                case TalkingBookApi.AudioRecordingMode.TextBox:
                    book = SetupBookLong(
                        text: "<p>This is some text.</p>",
                        lang: "xyz",
                        images: images,
                        extraEditDivClasses: "audio-sentence' id='e993d14a-0ec3-4316-840b-ac9143d59a2c", // Injecting other attributes into the "class" field as well in order to create the ID attribute simultaneously
                        extraContentOutsideTranslationGroup: "<div class='bloom-translationGroup'><div lang='xyz' class='bloom-editable audio-sentence' id='i0d8e9910-dfa3-4376-9373-a869e109b763'><p>Another sentence</p></div></div>"
                    );
                    break;
                default:
                    book = null;
                    Assert.Fail("Invalid test input");
                    break;
            }
            MakeImageFiles(book, "my image");
            MakeFakeAudio(
                book.FolderPath.CombineForPath("audio", "e993d14a-0ec3-4316-840b-ac9143d59a2c.mp3")
            );
            // But don't make a fake audio file for the second span
            MakeEpub("output", $"Missing_Audio_Ignored_{audioRecordingMode}", book); // Note: need to ensure they that multiple test cases do not use the same file
            CheckBasicsInManifest("my_image");
            CheckBasicsInPage();
            CheckBasicsInGivenPage(2, "my_image");
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data);
            CheckEpubTypeAttributes(page2Data, null);
            CheckNavPage();
            CheckFontStylesheet();

            // xpath search for slash in attribute value fails (something to do with interpreting it as a namespace reference?)
            var assertManifest = AssertThatXmlIn.String(
                FixContentForXPathValueSlash(_manifestContent)
            );
            assertManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='f2' and @href='2.xhtml' and @media-overlay='f2_overlay']"
            );
            assertManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='f2_overlay' and @href='2_overlay.smil' and @media-type='application^slash^smil+xml']"
            );

            var smilData = StripXmlHeader(
                ExportEpubTestsBaseClass.GetZipContent(_epub, "content/2_overlay.smil")
            );
            var mgr = new XmlNamespaceManager(new NameTable());
            mgr.AddNamespace("smil", "http://www.w3.org/ns/SMIL");
            mgr.AddNamespace("epub", "http://www.idpf.org/2007/ops");
            var assertSmil = AssertThatXmlIn.String(FixContentForXPathValueSlash(smilData));
            assertSmil.HasAtLeastOneMatchForXpath(
                "smil:smil/smil:body/smil:seq[@epub:textref='2.xhtml' and @epub:type='bodymatter chapter']",
                mgr
            );
            assertSmil.HasAtLeastOneMatchForXpath(
                "smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:text[@src='2.xhtml#e993d14a-0ec3-4316-840b-ac9143d59a2c']",
                mgr
            );
            assertSmil.HasNoMatchForXpath(
                "smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:text[@src='2.xhtml#i0d8e9910-dfa3-4376-9373-a869e109b763']",
                mgr
            );
            assertSmil.HasAtLeastOneMatchForXpath(
                "smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:audio[@src='"
                    + kAudioSlash
                    + "e993d14a-0ec3-4316-840b-ac9143d59a2c.mp3']",
                mgr
            );
            assertSmil.HasNoMatchForXpath(
                "smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:audio[@src='"
                    + kAudioSlash
                    + "i0d8e9910-dfa3-4376-9373-a869e109b763.mp3']",
                mgr
            );

            VerifyEpubItemExists(
                "content/" + EpubMaker.kAudioFolder + "/e993d14a-0ec3-4316-840b-ac9143d59a2c.mp3"
            );
            VerifyEpubItemDoesNotExist(
                "content/" + EpubMaker.kAudioFolder + "/i0d8e9910-dfa3-4376-9373-a869e109b763.mp3"
            );
        }

        [Test]
        public void BookSwitchedToDeviceXMatter()
        {
            // Title makes the front cover non-empty so it will not be omitted.
            var book = SetupBookLong(
                "This is some text",
                "en",
                createPhysicalFile: true,
                optionalDataDiv: @"<div id='bloomDataDiv'>
					<div data-book='contentLanguage1' lang='*'>xyz</div>
					<div data-book='contentLanguage2' lang='*'>en</div>
					<div data-book='bookTitle' lang='en'>title</div>
				</div>"
            );
            MakeEpub("output", "BookSwitchedToDeviceXMatter", book);
            // This is a rather crude way to test that it is switched to device XMatter, but we aren't even sure we
            // really want to do that yet. This at least gets something in there which should fail if we somehow
            // lose that fix, although currently the failure that happens if I take out the conversion is not
            // a simple failure of this assert...something goes wrong before that making a real physical file
            // for the unit test.
            var currentPage = String.Empty;
            int pageCount = 0;
            for (int i = 1; ; ++i)
            {
                var entryPath = "content/" + i + ".xhtml";
                var entry = _epub.GetEntry(entryPath);
                if (entry == null)
                    break;
                ++pageCount;
                currentPage = ExportEpubTestsBaseClass.GetZipContent(_epub, entryPath);
                switch (i)
                {
                    case 1:
                        CheckPageBreakMarker(currentPage, "pgFrontCover", "Front Cover");
                        break;
                    case 2:
                        CheckPageBreakMarker(currentPage, "pg1", "1");
                        CheckEpubTypeAttributes(currentPage, null);
                        break;
                    case 3:
                        CheckPageBreakMarker(currentPage, "pgTitlePage", "Title Page");
                        CheckEpubTypeAttributes(currentPage, null, "titlepage");
                        break;
                    case 4:
                        CheckPageBreakMarker(currentPage, "pgCreditsPage", "Credits Page");
                        break;
                    case 5:
                        CheckPageBreakMarker(
                            currentPage,
                            "pgOutsideBackCover",
                            "Outside Back Cover"
                        );
                        CheckEpubTypeAttributes(currentPage, null);
                        break;
                    default:
                        // We should never get here!
                        Assert.IsTrue(
                            i > 0 && i < 6,
                            "unexpected page number {0} should be between 1 and 5 inclusive",
                            i
                        );
                        break;
                }
            }
            // device xmatter currently has 1 front and a default of 3 back pages, so we should have exactly five pages.
            Assert.AreEqual(5, pageCount);
            AssertThatXmlIn
                .String(currentPage)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'outsideBackCover')]",
                    1
                );
            AssertThatXmlIn
                .String(currentPage)
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'theEndPage')]", 0);
            var navPageData = CheckNavPage();
            AssertThatXmlIn
                .String(navPageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "xhtml:html/xhtml:body/xhtml:nav[@epub:type='toc' and @id='toc']/xhtml:ol/xhtml:li/xhtml:a[@href='2.xhtml']",
                    _ns,
                    1
                );
            AssertThatXmlIn
                .String(navPageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "xhtml:html/xhtml:body/xhtml:nav[@epub:type='toc' and @id='toc']/xhtml:ol/xhtml:li/xhtml:a[@href='3.xhtml']",
                    _ns,
                    1
                );
            AssertThatXmlIn
                .String(navPageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "xhtml:html/xhtml:body/xhtml:nav[@epub:type='toc' and @id='toc']/xhtml:ol/xhtml:li/xhtml:a[@href='4.xhtml']",
                    _ns,
                    1
                );
            AssertThatXmlIn
                .String(navPageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "xhtml:html/xhtml:body/xhtml:nav[@epub:type='toc' and @id='toc']/xhtml:ol/xhtml:li/xhtml:a[@href='5.xhtml']",
                    _ns,
                    1
                );
            AssertThatXmlIn
                .String(navPageData)
                .HasNoMatchForXpath(
                    "xhtml:html/xhtml:body/xhtml:nav[@epub:type='toc' and @id='toc']/xhtml:ol/xhtml:li/xhtml:a[@href='6.xhtml']",
                    _ns
                );
            AssertThatXmlIn
                .String(navPageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "xhtml:html/xhtml:body/xhtml:nav[@epub:type='page-list']/xhtml:ol/xhtml:li/xhtml:a[@href='1.xhtml#pgFrontCover']",
                    _ns,
                    1
                );
            AssertThatXmlIn
                .String(navPageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "xhtml:html/xhtml:body/xhtml:nav[@epub:type='page-list']/xhtml:ol/xhtml:li/xhtml:a[@href='2.xhtml#pg1']",
                    _ns,
                    1
                );
            AssertThatXmlIn
                .String(navPageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "xhtml:html/xhtml:body/xhtml:nav[@epub:type='page-list']/xhtml:ol/xhtml:li/xhtml:a[@href='3.xhtml#pgTitlePage']",
                    _ns,
                    1
                );
            AssertThatXmlIn
                .String(navPageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "xhtml:html/xhtml:body/xhtml:nav[@epub:type='page-list']/xhtml:ol/xhtml:li/xhtml:a[@href='4.xhtml#pgCreditsPage']",
                    _ns,
                    1
                );
            AssertThatXmlIn
                .String(navPageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "xhtml:html/xhtml:body/xhtml:nav[@epub:type='page-list']/xhtml:ol/xhtml:li/xhtml:a[@href='5.xhtml#pgOutsideBackCover']",
                    _ns,
                    1
                );
            AssertThatXmlIn
                .String(navPageData)
                .HasNoMatchForXpath(
                    "xhtml:html/xhtml:body/xhtml:nav[@epub:type='page-list']/xhtml:ol/xhtml:li/xhtml:a[contains(@href, '6.xhtml')]",
                    _ns
                );
        }

        [TestCase(TalkingBookApi.AudioRecordingMode.Sentence)]
        [TestCase(TalkingBookApi.AudioRecordingMode.TextBox)]
        public void Missing_Audio_CreatedFromWav(
            TalkingBookApi.AudioRecordingMode audioRecordingMode
        )
        {
            var images = new string[] { "1my$Image", "my%20image" };

            BloomBook book;
            switch (audioRecordingMode)
            {
                case TalkingBookApi.AudioRecordingMode.Sentence:
                    book = SetupBook(
                        "<p><span id='e993d14a-0ec3-4316-840b-ac9143d59a2c' class='audio-sentence'>This is some text.</span><span id='i0d8e9910-dfa3-4376-9373-a869e109b763'>Another sentence</span></p>",
                        "xyz",
                        images
                    );
                    break;
                case TalkingBookApi.AudioRecordingMode.TextBox:
                    var extraEditDivClassesAndAttributes =
                        "audio-sentence' id='e993d14a-0ec3-4316-840b-ac9143d59a2c"; // Injecting other attributes into the "class" field as well in order to create the ID attribute simultaneously
                    var extraContentOutsideTranslationGroup =
                        "<div class='bloom-translationGroup'><div lang='xyz' class='bloom-editable audio-sentence' id='i0d8e9910-dfa3-4376-9373-a869e109b763'><p>Text for editable 2</p></div></div>";
                    book = SetupBookLong(
                        "<p>This is some text.Another sentence</p>",
                        "xyz",
                        images: images,
                        extraEditDivClasses: extraEditDivClassesAndAttributes,
                        extraContentOutsideTranslationGroup: extraContentOutsideTranslationGroup
                    );
                    break;
                default:
                    return;
            }
            // Similar input as the basic Missing_Audio_Ignored, (also verifies that IDs are really adjusted), but this time we don't create one of the expected audio files.
            MakeImageFiles(book, "my image");
            MakeFakeAudio(
                book.FolderPath.CombineForPath("audio", "e993d14a-0ec3-4316-840b-ac9143d59a2c.mp3")
            );
            // But don't make a fake audio file for the second span
            MakeEpub("output", $"Missing_Audio_CreatedFromWav_{audioRecordingMode}", book);
            CheckBasicsInManifest("my_image");
            CheckAccessibilityInManifest(true, true, false, _defaultSourceValue); // both sound and image files
            CheckBasicsInPage();
            CheckBasicsInGivenPage(2, "my_image");
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data);
            CheckEpubTypeAttributes(page2Data, null);
            CheckNavPage();
            CheckFontStylesheet();

            // xpath search for slash in attribute value fails (something to do with interpreting it as a namespace reference?)
            var assertManifest = AssertThatXmlIn.String(
                FixContentForXPathValueSlash(_manifestContent)
            );
            assertManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='f2' and @href='2.xhtml' and @media-overlay='f2_overlay']"
            );
            assertManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='f2_overlay' and @href='2_overlay.smil' and @media-type='application^slash^smil+xml']"
            );

            var smilData = StripXmlHeader(
                ExportEpubTestsBaseClass.GetZipContent(_epub, "content/2_overlay.smil")
            );
            var mgr = new XmlNamespaceManager(new NameTable());
            mgr.AddNamespace("smil", "http://www.w3.org/ns/SMIL");
            mgr.AddNamespace("epub", "http://www.idpf.org/2007/ops");
            var assertSmil = AssertThatXmlIn.String(FixContentForXPathValueSlash(smilData));
            assertSmil.HasAtLeastOneMatchForXpath(
                "smil:smil/smil:body/smil:seq[@epub:textref='2.xhtml' and @epub:type='bodymatter chapter']",
                mgr
            );
            assertSmil.HasAtLeastOneMatchForXpath(
                "smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:text[@src='2.xhtml#e993d14a-0ec3-4316-840b-ac9143d59a2c']",
                mgr
            );
            assertSmil.HasNoMatchForXpath(
                "smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:text[@src='2.xhtml#i0d8e9910-dfa3-4376-9373-a869e109b763']",
                mgr
            );
            assertSmil.HasAtLeastOneMatchForXpath(
                "smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:audio[@src='"
                    + kAudioSlash
                    + "e993d14a-0ec3-4316-840b-ac9143d59a2c.mp3']",
                mgr
            );
            assertSmil.HasNoMatchForXpath(
                "smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:audio[@src='"
                    + kAudioSlash
                    + "i0d8e9910-dfa3-4376-9373-a869e109b763.mp3']",
                mgr
            );

            VerifyEpubItemExists(
                "content/" + EpubMaker.kAudioFolder + "/e993d14a-0ec3-4316-840b-ac9143d59a2c.mp3"
            );
            VerifyEpubItemDoesNotExist(
                "content/" + EpubMaker.kAudioFolder + "/i0d8e9910-dfa3-4376-9373-a869e109b763.mp3"
            );
        }

        /// <summary>
        /// Motivated by "El Nino" from bloom library, which (to defeat caching?) has such a query param in one of its src attrs.
        /// </summary>
        [Test]
        public void ImageSrcQuery_IsIgnored()
        {
            var book = SetupBook("This is some text", "en", "myImage.png?1023456");
            MakeImageFiles(book, "myImage");
            MakeEpub("output", "ImageSrcQuery_IsIgnored", book);
            CheckBasicsInManifest("myImage");
            CheckAccessibilityInManifest(false, true, false, _defaultSourceValue); // no sound files, but a nontrivial image file
            CheckBasicsInPage();
            CheckBasicsInGivenPage(2, "myImage");
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data);
            CheckEpubTypeAttributes(page2Data, null);
        }

        [Test]
        public void HandlesMultiplePages()
        {
            var book = SetupBookLong(
                "This is some text",
                "en",
                extraPages: @"<div class='bloom-page numberedPage' data-page-number='2'>
						<div id='anotherId' class='marginBox'>
							<div id='test' class='bloom-translationGroup bloom-requiresParagraphs' lang=''>
								<div aria-describedby='qtip-1' class='bloom-editable' lang='en'>
									Page two text
								</div>
								<div lang = '*'>more text</div>
							</div>
						</div>
					</div>"
            );
            MakeEpub("output", "HandlesMultiplePages", book);
            CheckBasicsInManifest();
            CheckAccessibilityInManifest(false, false, false, _defaultSourceValue); // neither sound nor image files
            CheckBasicsInPage();
            CheckBasicsInGivenPage(2);
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data);
            CheckEpubTypeAttributes(page2Data, null);

            var page3Data = ExportEpubTestsBaseClass.GetZipContent(
                _epub,
                Path.GetDirectoryName(_manifestFile) + "/" + "3.xhtml"
            );
            AssertThatXmlIn
                .String(page3Data)
                .HasAtLeastOneMatchForXpath("//xhtml:div[@id='anotherId']", _ns);
            CheckPageBreakMarker(page3Data, "pg2", "2");
            CheckEpubTypeAttributes(page3Data, null);
            var navPageData = CheckNavPage();
            AssertThatXmlIn
                .String(navPageData)
                .HasNoMatchForXpath(
                    "xhtml:html/xhtml:body/xhtml:nav[@epub:type='toc' and @id='toc']/xhtml:ol/xhtml:li/xhtml:a[@href='3.xhtml']",
                    _ns
                );
            AssertThatXmlIn
                .String(navPageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "xhtml:html/xhtml:body/xhtml:nav[@epub:type='page-list']/xhtml:ol/xhtml:li/xhtml:a[@href='2.xhtml#pg1']",
                    _ns,
                    1
                );
            AssertThatXmlIn
                .String(navPageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "xhtml:html/xhtml:body/xhtml:nav[@epub:type='page-list']/xhtml:ol/xhtml:li/xhtml:a[@href='3.xhtml#pg2']",
                    _ns,
                    1
                );
        }

        [Test]
        public void OmitsNonPrintingPages()
        {
            var book = SetupBookLong(
                "This is some text",
                "en",
                extraPages: @"<div class='bloom-page bloom-nonprinting'>
						<div id='anotherId' class='marginBox'>
							<div id='test' class='bloom-translationGroup bloom-requiresParagraphs' lang=''>
								<div aria-describedby='qtip-1' class='bloom-editable' lang='en'>
									Page two text
								</div>
								<div lang = '*'>more text</div>
							</div>
						</div>
					</div>
					<div class='bloom-page bloom-interactive-page'>
						<div id='anotherId' class='marginBox'>
							<div id='test' class='bloom-translationGroup bloom-requiresParagraphs' lang=''>
								<div aria-describedby='qtip-1' class='bloom-editable' lang='en'>
									Page two text
								</div>
								<div lang = '*'>more text</div>
							</div>
						</div>
					</div>"
            );
            MakeEpub("output", "OmitsNonPrintingPages", book);
            CheckBasicsInManifest();
            CheckBasicsInPage();
            CheckBasicsInGivenPage(2);
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data);
            CheckEpubTypeAttributes(page2Data, null);

            var page3Data = GetPageNData(3);
            var assertThatPage3 = AssertThatXmlIn.String(page3Data);
            assertThatPage3.HasNoMatchForXpath("//div[contains(@class,'bloom-nonprinting')]");
            assertThatPage3.HasSpecifiedNumberOfMatchesForXpath(
                "//div[contains(@class,'bloom-page') and contains(@class,'titlePage')]",
                1
            );
        }

        /// <summary>
        /// Motivated by "Look in the sky. What do you see?" from bloom library, if we can't find an image,
        /// remove the element. Also exercises some other edge cases of missing or empty src attrs for images.
        /// </summary>
        [Test]
        public void ImageMissing_IsRemoved()
        {
            var book = SetupBookLong(
                "This is some text",
                "en",
                extraContentOutsideTranslationGroup: @"<div><img></img></div>
							<div><img src=''></img></div>
							<div><img src='?1023456'></img></div>",
                images: new[] { "myImage.png?1023456" }
            );
            // Purposely do NOT create any images.
            MakeEpub("output", "ImageMissing_IsRemoved", book);
            CheckBasicsInManifest();
            CheckBasicsInPage();
            CheckBasicsInGivenPage(2);
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data);
            CheckEpubTypeAttributes(page2Data, null);

            AssertThatXmlIn
                .String(_manifestContent)
                .HasNoMatchForXpath(
                    "package/manifest/item[@id='fmyImage' and @href='myImage.png']"
                );
            AssertThatXmlIn.String(page2Data).HasNoMatchForXpath("//img");
        }

        [Test]
        public void LeftToRight_SpineDoesNotDeclareDirection()
        {
            var book = SetupBook("This is some text", "xyz");
            book.BookData.Language1.IsRightToLeft = false;
            MakeEpub("output", "SpineDoesNotDeclareDirection", book);
            AssertThatXmlIn
                .String(_manifestContent)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//spine[not(@page-progression-direction)]",
                    1
                );
        }

        [Test]
        public void RightToLeft_SpineDeclaresRtlDirection()
        {
            var book = SetupBook("This is some text", "xyz");
            book.BookData.Language1.IsRightToLeft = true;
            MakeEpub("output", "SpineDeclaresRtlDirection", book);
            AssertThatXmlIn
                .String(_manifestContent)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//spine[@page-progression-direction='rtl']",
                    1
                );
            ;
        }

        [Test]
        public void Manifest_IncludesCopyrightAndIsbnInfo()
        {
            const string copyrightString = "Copyright © 2020, some copyright holder";
            const string isbnContents = "<p>123456ISBN</p>";
            var book = SetupBookLong(
                "This is some text",
                "xyz",
                optionalDataDiv: @"<div id='bloomDataDiv'>
						<div data-book='contentLanguage1' lang='*'>xyz</div>
						<div data-book='contentLanguage2' lang='*'>en</div>
						<div data-book='copyright' lang='*'>"
                    + copyrightString
                    + @"</div>
						<div data-book='ISBN' lang='xyz'>"
                    + isbnContents
                    + @"</div>
					</div>"
            );
            book.BookInfo.Isbn = isbnContents;
            book.BookInfo.Copyright = copyrightString;
            MakeEpub("output", "CopyrightAndISBN", book);
            AssertThatXmlIn
                .String(_manifestContent)
                .HasAtLeastOneMatchForXpath(
                    "opf:package/opf:metadata/dc:identifier[text()='urn:isbn:123456ISBN']",
                    GetNamespaceManager()
                );
            AssertThatXmlIn
                .String(_manifestContent)
                .HasAtLeastOneMatchForXpath(
                    "package/metadata/meta[@property='dcterms:dateCopyrighted' and text()='2020']"
                );
            AssertThatXmlIn
                .String(_manifestContent)
                .HasAtLeastOneMatchForXpath(
                    "package/metadata/meta[@property='dcterms:rightsHolder' and text()='some copyright holder']"
                );
        }

        [Test]
        public void Manifest_HandlesEmptyCopyrightAndIsbnInfo()
        {
            var book = SetupBook("This is some text", "xyz");
            MakeEpub("output", "NonCopyrightAndISBN", book);
            AssertThatXmlIn
                .String(_manifestContent)
                .HasNoMatchForXpath(
                    "opf:package/opf:metadata/dc:identifier[contains(text(),'urn:isbn:')]",
                    GetNamespaceManager()
                );
            AssertThatXmlIn
                .String(_manifestContent)
                .HasNoMatchForXpath("package/metadata/meta[@property='dcterms:dateCopyrighted']");
            AssertThatXmlIn
                .String(_manifestContent)
                .HasNoMatchForXpath("package/metadata/meta[@property='dcterms:rightsHolder']");
        }

        /// <summary>
        /// Motivated by "Look in the sky. What do you see?" from bloom library, if elements with class bloom-ui have
        /// somehow been left in the book, don't put them in the ePUB.
        /// </summary>
        [Test]
        public void BloomUi_IsRemoved()
        {
            var book = SetupBookLong(
                "This is some text",
                "en",
                extraContent: "<div class='bloom-ui rubbish'><img src='myImage.png?1023456'></img></div>",
                images: new[] { "myImage.png?1023456" }
            );
            MakeImageFiles(book, "myImage"); // Even though the image exists we should not use it.
            MakeEpub("output", "BloomUi_IsRemoved", book);
            CheckBasicsInManifest();
            CheckBasicsInPage();
            CheckBasicsInGivenPage(2);
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data);
            CheckEpubTypeAttributes(page2Data, null);

            AssertThatXmlIn
                .String(_manifestContent)
                .HasNoMatchForXpath(
                    "package/manifest/item[@id='fmyImage' and @href='myImage.png']"
                );
            AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//img[@src='myImage.png']");
            AssertThatXmlIn
                .String(_page1Data)
                .HasNoMatchForXpath("//div[@class='bloom-ui rubbish']");
        }

        [Test]
        public void Tabindex_IsRemoved()
        {
            var book = SetupBookLong(
                "This is some text",
                "en",
                extraContentOutsideTranslationGroup: "<div class='bloom-translationGroup' tabindex='1'><div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='xyz' tabindex='2'>This could be focused</div></div>",
                images: new[] { "myImage.png?1023456" }
            );
            MakeEpub("output", "Tabindex_IsRemoved", book);
            CheckBasicsInManifest();

            AssertThatXmlIn.String(_page1Data).HasNoMatchForXpath("//*[@tabindex]");
        }

        [Test]
        public void StandardStyleSheets_AreRemoved()
        {
            var book = SetupBookLong("Some text", "en");
            MakeEpub("output", "StandardStyleSheets_AreRemoved", book);
            CheckBasicsInManifest();
            CheckBasicsInPage();
            CheckBasicsInGivenPage(2);
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data);
            CheckEpubTypeAttributes(page2Data, null);
            CheckNavPage();
            CheckFontStylesheet();
            // Check that the standard stylesheet, not wanted in the ePUB, is removed.
            AssertThatXmlIn
                .String(page2Data)
                .HasNoMatchForXpath("//xhtml:head/xhtml:link[@href='basePage.css']", _ns); // standard stylesheet should be removed.
            Assert.That(_page1Data, Does.Not.Contain("basePage.css")); // make sure it's stripped completely
            Assert.That(_epub.GetEntry("content/basePage.ss"), Is.Null);
        }

        /// <summary>
        /// Test that content that shouldn't show up in the ePUB gets removed.
        /// -- display: none
        /// -- pageDescription
        /// -- pageLabel
        /// -- label elements
        /// -- bubbles
        /// </summary>
        [Test]
        public void InvisibleAndUnwantedContentRemoved()
        {
            var book = SetupBookLong(
                "Page with a picture on top and a large, centered word below.",
                "en",
                extraContent: @"<div class='bloom-editable' lang='xyz'><label class='bubble'>Book title in {lang} should be removed</label>vernacular text should always display</div>
					<div class='bloom-editable' lang='fr'>French text should only display if configured</div>
					<div class='bloom-editable' lang='de'>German should never display in this collection</div>",
                extraContentOutsideTranslationGroup: @"
					<div class='split-pane'>
						<div class='split-pane-component'>
							<div class='split-pane-component-inner'>
								<div id='testSecondTextBox' class='bloom-translationGroup bloom-requiresParagraphs' lang=''>
									<div class='bloom-editable' lang='xyz'>2nd text box vernacular</div>
									<div class='bloom-editable' lang='en'>Second text box</div>
								</div>
							</div>
						</div>
						<div class='split-pane-component'>
							<div class='split-pane'>
								<div class='split-pane-component'>
									<div class='split-pane-component-inner'>
										<div id='testThirdTextBox' class='bloom-translationGroup bloom-requiresParagraphs' lang=''>
											<div class='bloom-editable' lang='xyz'>3rd text box vernacular</div>
											<div class='bloom-editable' lang='en'>Third text box</div>
										</div>
									</div>
								</div>
								<div class='split-pane-divider'></div>
								<div class='split-pane-component'>
									<div class='split-pane-component-inner'>
<!-- class 'box-header-off' here pointed out a flaw in our deletion code in InvisibleAndUnwantedContentRemoved() -->
										<div id='testFourthTextBox' class='box-header-off bloom-translationGroup bloom-requiresParagraphs' lang=''>
											<div class='bloom-editable' lang='xyz'><p></p></div>
											<div class='bloom-editable' lang='en'><p></p></div>
										</div>
										<div id='testFifthTextBox' class='bloom-translationGroup bloom-requiresParagraphs' lang=''>
											<div class='bloom-editable' lang='xyz'>5th text box vernacular</div>
											<div class='bloom-editable' lang='en'>Fifth text box</div>
										</div>
									</div>
								</div>
							</div>
						</div>
					</div>",
                defaultLanguages: "V",
                createPhysicalFile: true
            );

            MakeEpub("output", "InvisibleAndUnwantedContentRemoved", book);
            CheckBasicsInManifest();
            CheckBasicsInPage();
            CheckBasicsInGivenPage(2);
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data);
            CheckEpubTypeAttributes(page2Data, null);
            CheckNavPage();
            CheckFontStylesheet();

            var assertThatPage2 = AssertThatXmlIn.String(page2Data);
            assertThatPage2.HasAtLeastOneMatchForXpath("//xhtml:div[@lang='xyz']", _ns);
            assertThatPage2.HasNoMatchForXpath("//xhtml:div[@class='pageDescription']", _ns);
            assertThatPage2.HasNoMatchForXpath("//xhtml:div[@lang='en']", _ns); // one language by default
            assertThatPage2.HasNoMatchForXpath("//xhtml:div[@lang='fr']", _ns);
            assertThatPage2.HasNoMatchForXpath("//xhtml:div[@lang='de']", _ns);
            assertThatPage2.HasNoMatchForXpath("//xhtml:label", _ns); // labels are hidden
            assertThatPage2.HasNoMatchForXpath("//xhtml:div[@class='pageLabel']", _ns);
            assertThatPage2.HasNoMatchForXpath("//xhtml:script", _ns);
        }

        /// <summary>
        /// Content whose display properties resolves to display:None should be removed.
        /// This should not include National1 in XMatter.
        /// </summary>
        [Test]
        public void National1_InXMatter_IsNotRemoved()
        {
            // We are using real stylesheet info here to determine what should be visible, so the right classes must be carefully applied.
            var book = SetupBookLong(
                "English text (first national language) should display in title.",
                "en",
                extraPageClass: " bloom-frontMatter frontCover' data-page='required singleton",
                extraContent: @"<div class='bloom-editable' lang='xyz' data-book='bookTitle'><label class='bubble'>Book title in {lang} should be removed</label>vernacular text (content1) should always display</div>
							<div class='bloom-editable' lang='fr' data-book='bookTitle'>French text (second national language) should not display</div>
							<div class='bloom-editable' lang='de' data-book='bookTitle'>German should never display in this collection</div>",
                optionalDataDiv: @"<div id='bloomDataDiv'>
						<div data-book='contentLanguage1' lang='*'>xyz</div>
						<div data-book='contentLanguage2' lang='*'>en</div>
					</div>",
                extraEditGroupClasses: "bookTitle",
                defaultLanguages: "V,N1",
                // This test needs real stylesheets to determine what should be visible.
                createPhysicalFile: true
            );

            MakeEpub("output", "National1_InXMatter_IsNotRemoved", book);
            CheckBasicsInManifest();
            CheckBasicsInPage();
            CheckPageBreakMarker(_page1Data, "pgFrontCover", "Front Cover");

            var assertThatPage1 = AssertThatXmlIn.String(_page1Data);
            assertThatPage1.HasAtLeastOneMatchForXpath("//xhtml:div[@lang='xyz']", _ns);
            assertThatPage1.HasAtLeastOneMatchForXpath("//xhtml:div[@lang='en']", _ns);
            assertThatPage1.HasNoMatchForXpath("//xhtml:div[@lang='fr']", _ns);
            assertThatPage1.HasNoMatchForXpath("//xhtml:div[@lang='de']", _ns);
            assertThatPage1.HasNoMatchForXpath("//xhtml:label", _ns); // labels are hidden
            assertThatPage1.HasNoMatchForXpath("//xhtml:div[@class='pageLabel']", _ns);
        }

        [Test]
        public void RemoveFontSizes_CausesFontSizesInEmbeddedStylesheets_ToBeRemoved()
        {
            var userStyleSheet =
                @"/*<![CDATA[*/
    .BigWords-style { font-size: 45pt ! important; text-align: center ! important; }
    .normal-style[lang=""en""] { font-size: 18pt ! important; }
				.normal-style { font-size: 18pt ! important; }
			/*]]>*/";
            var book = SetupBookLong(
                "Content unimportant for this test",
                "en",
                extraHeadContent: "<style type='text/css' title='userModifiedStyles'>"
                    + userStyleSheet
                    + "</style>"
            );

            MakeEpub(
                "output",
                "RemoveFontSizes_CausesFontSizesInEmbeddedStylesheets_ToBeRemoved",
                book,
                extraInit: maker => maker.RemoveFontSizes = true
            );
            Assert.That(_page1Data, Does.Not.Contain("font-size"));
            Assert.That(_page1Data, Does.Not.Contain("18pt"));
            // And a few checks to try to make sure it doesn't take out too much.
            Assert.That(_page1Data.Contains("/*<![CDATA[*/"));
            Assert.That(_page1Data.Contains("/*]]>*/"));
            Assert.That(_page1Data.Contains(".BigWords-style { text-align: center ! important; }"));
        }

        [Test]
        public void NoRemoveFontSizes_FontSizesNotRemoved()
        {
            var userStyleSheet =
                @"/*<![CDATA[*/
    .BigWords-style { font-size: 45pt ! important; text-align: center ! important; }
    .normal-style[lang=""en""] { font-size: 18pt ! important; }
				.normal-style { font-size: 18pt ! important; }
			/*]]>*/";
            var book = SetupBookLong(
                "Content unimportant for this test",
                "en",
                extraHeadContent: "<style type='text/css' title='userModifiedStyles'>"
                    + userStyleSheet
                    + "</style>"
            );

            MakeEpub("output", "NoRemoveFontSizes_FontSizesNotRemoved", book);
            // This is a bit too strong, because some whitespace changes would be harmless.
            Assert.That(_page1Data, Does.Contain(userStyleSheet));
        }

        [Test]
        public void HeadingN_convertedToHN()
        {
            var book = SetupBookLong(
                "Content of level 1 heading",
                "xyz",
                extraEditDivClasses: "Heading1",
                extraContentOutsideTranslationGroup: @"<div class='bloom-translationGroup'><div class='bloom-editable Heading2' lang='xyz'><p>Level 2 heading</p></div></div>
							<div class='bloom-translationGroup'><div class='bloom-editable Heading3' lang='xyz'><p><span id='xyzzy'>Level</span> 3 heading</p></div></div>"
            );

            MakeEpub("output", "HeadingN_convertedToHN", book);
            // This is a bit too strong, because some whitespace changes would be harmless.
            var page2Data = GetPageNData(2);
            var assertThatPage2 = AssertThatXmlIn.String(page2Data);
            assertThatPage2.HasSpecifiedNumberOfMatchesForXpath(
                "//xhtml:h1[contains(@class, 'bloom-editable') and contains(@class, 'Heading1') and contains(text(), 'Content of level 1 heading')]",
                _ns,
                1
            );
            assertThatPage2.HasSpecifiedNumberOfMatchesForXpath(
                "//xhtml:h2[contains(@class,'bloom-editable Heading2') and text()='Level 2 heading']",
                _ns,
                1
            );
            assertThatPage2.HasSpecifiedNumberOfMatchesForXpath(
                "//xhtml:h3[contains(@class,'bloom-editable Heading3') and text()=' 3 heading']",
                _ns,
                1
            );
            assertThatPage2.HasSpecifiedNumberOfMatchesForXpath(
                "//xhtml:h3[contains(@class,'bloom-editable Heading3') and text()=' 3 heading']/xhtml:span[@id='xyzzy' and text()='Level']",
                _ns,
                1
            );
            assertThatPage2.HasNoMatchForXpath("//xhtml:h1/xhtml:p", _ns);
            assertThatPage2.HasNoMatchForXpath("//xhtml:h2/xhtml:p", _ns);
            assertThatPage2.HasNoMatchForXpath("//xhtml:h3/xhtml:p", _ns);
        }

        /// <summary>
        /// The critical point here is that passing defaultLanguages:N1 makes N1 the value of the data-default-languages attribute
        /// of the translation group, which makes only N1 (English) visible. We want to verify that this results in only that
        /// language being even PRESENT in the epub.
        /// </summary>
        [Test]
        public void UserSpecifiedNoVernacular_VernacularRemoved()
        {
            // We are using real stylesheet info here to determine what should be visible, so the right classes must be carefully applied.
            var book = SetupBookLong(
                "English text (first national language) should display but vernacular shouldn't.",
                "en",
                extraContent: @"<div class='bloom-editable' lang='xyz'>vernacular text should not display in this case because the user turned it off</div>
							<div class='bloom-editable' lang='fr'>French text (second national language) should not display</div>
							<div class='bloom-editable' lang='de'>German should never display in this collection</div>",
                defaultLanguages: "N1",
                createPhysicalFile: true
            );

            MakeEpub("output", "UserSpecifiedNoVernacular_VernacularRemoved", book);
            CheckBasicsInManifest();
            CheckBasicsInPage();
            CheckBasicsInGivenPage(2);
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data);
            CheckEpubTypeAttributes(page2Data, null);

            var assertThatPage2 = AssertThatXmlIn.String(page2Data);
            assertThatPage2.HasNoMatchForXpath("//xhtml:div[@lang='xyz']", _ns);
            assertThatPage2.HasAtLeastOneMatchForXpath("//xhtml:div[@lang='en']", _ns);
            assertThatPage2.HasNoMatchForXpath("//xhtml:div[@lang='fr']", _ns);
            assertThatPage2.HasNoMatchForXpath("//xhtml:div[@lang='de']", _ns);
        }

        /// <summary>
        /// Content whose display properties resolves to display:None should be removed.
        /// The default rules on a credits page show original acknowledgments only in national language.
        /// </summary>
        [Test]
        public void OriginalAcknowledgments_InCreditsPage_InVernacular_IsRemoved()
        {
            // We are using real stylesheet info here to determine what should be visible, so the right classes must be carefully applied.
            var book = SetupBookLong(
                "Acknowledgments should only show in national 1.",
                "en",
                extraPageClass: " bloom-frontMatter credits' data-page='required singleton",
                extraContent: @"<div class='bloom-editable' lang='xyz'><label class='bubble'>Book title in {lang} should be removed</label>acknowledgments in vernacular not displayed</div>
							<div class='bloom-editable' lang='fr'>National 2 should not be displayed</div>
							<div class='bloom-editable' lang='de'>German should never display in this collection</div>",
                optionalDataDiv: @"<div id=""bloomDataDiv"">
						<div data-book=""contentLanguage1"" lang=""*"">xyz</div>
						<div data-book=""originalAcknowledgments"" lang=""fr""><p>French Acknowledgments</p></div>
						<div data-book=""originalAcknowledgments"" lang=""de""><p>German Acknowledgments</p></div>
						<div data-book=""originalAcknowledgments"" lang=""es""><p>Spanish Acknowledgments</p></div>
						<div data-book=""originalAcknowledgments"" lang=""en""><p>English Acknowledgments</p></div>
					</div>",
                extraEditGroupClasses: "originalAcknowledgments",
                defaultLanguages: "N1",
                createPhysicalFile: true
            );

            MakeEpub(
                "output",
                "OriginalAcknowledgments_InCreditsPage_InVernacular_IsRemoved",
                book
            );
            CheckBasicsInManifest();
            CheckBasicsInPage();
            CheckBasicsInGivenPage(2);
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data, "pgTitlePage", "Title Page");
            CheckEpubTypeAttributes(page2Data, null, "titlepage");

            var page3Data = GetPageNData(3);
            CheckPageBreakMarker(page3Data, "pgCreditsPage", "Credits Page");
            CheckEpubTypeAttributes(page3Data, null);

            var assertThatPage3 = AssertThatXmlIn.String(page3Data);
            assertThatPage3.HasSpecifiedNumberOfMatchesForXpath("//xhtml:div[@lang='xyz']", _ns, 1);
            assertThatPage3.HasSpecifiedNumberOfMatchesForXpath(
                "//xhtml:div[@lang='xyz' and contains(@class,'licenseAndCopyrightBlock')]",
                _ns,
                1
            );
            // The next two lines are what check that there is no actual text in lang='xyz' despite one parent div in that language.
            assertThatPage3.HasNoMatchForXpath(
                "//xhtml:div[@lang='xyz']/child::text()[normalize-space()!='']",
                _ns
            );
            assertThatPage3.HasNoMatchForXpath(
                "//xhtml:div[@lang='xyz']//xhtml:div[not(@lang)]/child::text()[normalize-space()!='']",
                _ns
            );
            assertThatPage3.HasAtLeastOneMatchForXpath("//xhtml:div[@lang='en']", _ns);
            assertThatPage3.HasNoMatchForXpath("//xhtml:div[@lang='fr']", _ns);
            assertThatPage3.HasNoMatchForXpath("//xhtml:div[@lang='de']", _ns);
            assertThatPage3.HasNoMatchForXpath("//xhtml:label", _ns); // labels are hidden
            assertThatPage3.HasNoMatchForXpath("//xhtml:div[@class='pageLabel']", _ns);
        }

        /// <summary>
        /// Content whose display properties resolves to display:none should be removed.
        /// </summary>
        [Test]
        public void InCreditsPage_LicenseUrlAndISBN_AreRemoved()
        {
            // We are using real stylesheet info here to determine what should be visible, so the right classes must be carefully applied.
            var book = SetupBookLong(
                "License url and ISBN should be removed from the epub",
                "en",
                extraPageClass: " bloom-frontMatter credits' data-page='required singleton",
                extraContentOutsideTranslationGroup: @"<div class=""bloom-metaData licenseAndCopyrightBlock"" data-functiononhintclick=""bookMetadataEditor"" data-hint=""Click to Edit Copyright &amp; License"">
						<div class=""copyright Credits-Page-style"" data-derived=""copyright"" lang=""*"">
							Copyright © 2017, me
						</div>
						<div class=""licenseBlock"">
							<img class=""licenseImage"" src=""license.png"" data-derived=""licenseImage""></img>
							<div class=""licenseUrl"" data-derived=""licenseUrl"" lang=""*"">
								http://creativecommons.org/licenses/by/4.0/
							</div>
							<div class=""licenseDescription Credits-Page-style"" data-derived=""licenseDescription"" lang=""en"">
								http://creativecommons.org/licenses/by/4.0/<br></br>
								You are free to make commercial use of this work. You may adapt and add to this work. You must keep the copyright and credits for authors, illustrators, etc.
							</div>
							<div class=""licenseNotes Credits-Page-style"" data-derived=""licenseNotes""></div>
						</div>
					</div>
					<div class=""ISBNContainer"" data-hint=""International Standard Book Number. Leave blank if you don't have one of these."">
						<span class=""bloom-doNotPublishIfParentOtherwiseEmpty Credits-Page-style"">ISBN</span>
						<div class=""bloom-translationGroup"" data-default-languages=""*"">
							<div role=""textbox"" spellcheck=""true"" tabindex=""0"" class=""bloom-editable Credits-Page-style cke_editable cke_editable_inline cke_contents_ltr bloom-content1"" data-book=""ISBN"" contenteditable=""true"" lang=""enc"">
								<p></p>
							</div>
							<div class=""bloom-editable Credits-Page-style"" data-book=""ISBN"" contenteditable=""true"" lang=""*"">
								ABCDEFG
							</div>
							<div role=""textbox"" spellcheck=""true"" tabindex=""0"" class=""bloom-editable Credits-Page-style cke_editable cke_editable_inline cke_contents_ltr bloom-contentNational1"" data-book=""ISBN"" contenteditable=""true"" lang=""en"">
								<p></p>
							</div>
						</div>
					</div>",
                optionalDataDiv: @"<div id=""bloomDataDiv"">
						<div data-book=""contentLanguage1"" lang=""*"">en</div>
						<div data-book=""ISBN"" lang=""*"">ABCDEFG</div>
						<div data-book=""ISBN"" lang=""en"">ABCDEFG-en</div>
						<div data-book=""copyright"" lang=""*"">Copyright © 2017, me</div>
						<div data-book=""licenseUrl"" lang=""*"">http://creativecommons.org/licenses/by/4.0/</div>
						<div data-book=""licenseDescription"" lang=""en"">http://creativecommons.org/licenses/by/4.0/<br></br>You are free to make commercial use of this work. You may adapt and add to this work. You must keep the copyright and credits for authors, illustrators, etc.</div>
						<div data-book=""licenseImage"" lang=""*"">license.png</div>
					</div>",
                createPhysicalFile: true
            );
            MakeImageFiles(book, "license");

            MakeEpub("output", "InCreditsPage_LicenseUrlAndISBN_AreRemoved", book);
            CheckBasicsInManifest();
            CheckAccessibilityInManifest(false, false, false, "urn:isbn:ABCDEFG"); // no sound or nontrivial image files
            CheckBasicsInPage();
            CheckBasicsInGivenPage(2);
            var page2Data = GetPageNData(3);
            CheckPageBreakMarker(page2Data, "pgCreditsPage", "Credits Page");
            CheckEpubTypeAttributes(page2Data, null, "copyright-page");

            var assertThatPageTwo = AssertThatXmlIn.String(page2Data);
            assertThatPageTwo.HasNoMatchForXpath("//xhtml:div[@class='ISBNContainer']", _ns);
            assertThatPageTwo.HasNoMatchForXpath("//xhtml:div[@class='licenseUrl']", _ns);
            assertThatPageTwo.HasSpecifiedNumberOfMatchesForXpath(
                "//div[contains(@class, 'licenseDescription')]",
                1
            );
            assertThatPageTwo.HasSpecifiedNumberOfMatchesForXpath(
                "//img[@class='licenseImage']",
                1
            );
            // These temp Ids are added and removed during the creation process
            assertThatPageTwo.HasNoMatchForXpath(
                "//xhtml:div[contains(@id, '" + PublishHelper.kTempIdMarker + "')]",
                _ns
            );
        }

        [Test]
        public void FindFontsUsedInCss_FindsSimpleFontFamily()
        {
            var results = new HashSet<string>();
            HtmlDom.FindFontsUsedInCss("body {font-family:Arial}", results, true);
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results.Contains("Arial"));
        }

        [Test]
        public void FindFontsUsedInCss_FindsQuotedFontFamily()
        {
            var results = new HashSet<string>();
            HtmlDom.FindFontsUsedInCss("body {font-family:'Times New Roman'}", results, true);
            HtmlDom.FindFontsUsedInCss("body {font-family:\"Andika New Basic\"}", results, true);
            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results.Contains("Times New Roman"));
            Assert.That(results.Contains("Andika New Basic"));
        }

        [Test]
        public void FindFontsUsedInCss_IncludeFallbackFontsTrue_FindsMultipleFontFamilies()
        {
            var results = new HashSet<string>();
            HtmlDom.FindFontsUsedInCss(
                "body {font-family: 'Times New Roman', Arial,\"Andika New Basic\";}",
                results,
                true
            );
            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(results.Contains("Times New Roman"));
            Assert.That(results.Contains("Andika New Basic"));
            Assert.That(results.Contains("Arial"));
        }

        [Test]
        public void FindFontsUsedInCss_IncludeFallbackFontsFalse_FindsFirstFontInEachList()
        {
            var results = new HashSet<string>();
            HtmlDom.FindFontsUsedInCss(
                "body {font-family: 'Times New Roman', Arial,\"Andika New Basic\";} "
                    + "div {font-family: Font1, \"Font2\";} "
                    + "p {font-family: \"Font3\";}",
                results,
                false
            );
            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(results.Contains("Times New Roman"));
            Assert.That(results.Contains("Font1"));
            Assert.That(results.Contains("Font3"));
        }

        [TestCase("Arial !important")]
        [TestCase("Arial ! important")]
        [TestCase("Arial ! important ")]
        [TestCase("Arial  ! important , Times New Roman")]
        public void FindFontsUsedInCss_RemovesBangImportant(string fontFamily)
        {
            var results = new HashSet<string>();
            HtmlDom.FindFontsUsedInCss("body {font-family:" + fontFamily + "}", results, false);
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results.Contains("Arial"));
        }

        [Test]
        public void FindFontsUsedInCss_IgnoresInherit()
        {
            var results = new HashSet<string>();
            HtmlDom.FindFontsUsedInCss("body {font-family:inherit}", results, false);
            Assert.That(results, Has.Count.EqualTo(0));
        }

        [TestCase("Segoe UI", true)]
        [TestCase("Segoe UI", false)]
        [TestCase("segoe ui,Arial", false)]
        public void FindFontsUsedInCss_IgnoresSegoeUi(string fontFamily, bool includeFallbackFonts)
        {
            var results = new HashSet<string>();
            HtmlDom.FindFontsUsedInCss(
                "body {font-family:" + fontFamily + "}",
                results,
                includeFallbackFonts
            );
            Assert.That(results, Has.Count.EqualTo(0));
        }

        [TestCase("segoe ui,Arial")]
        public void FindFontsUsedInCss_IgnoresSegoeUi(string fontFamily)
        {
            var results = new HashSet<string>();
            HtmlDom.FindFontsUsedInCss("body {font-family:" + fontFamily + "}", results, true);
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results.Contains("Arial"));
        }

        [TestCase("A5Portrait", 297.0 / 2.0)]
        [TestCase("HalfLetterLandscape", 8.5 * 25.4)]
        [Test]
        public void ImageStyles_ConvertedToPercent(string sizeClass, double pageWidthMm)
        {
            var book = SetupBookLong(
                "This is some text",
                "en",
                extraPageClass: " " + sizeClass + " numberedPage' data-page-number='5",
                extraContentOutsideTranslationGroup: @"<div><img src='image1.png' width='334' height='220' style='width:334px; height:220px; margin-left: 34px; margin-top: 0px;'></img></div>
							<div><img src='image2.png' width='330' height='220' style='width:330px; height: 220px; margin-left: 33px; margin-top: 0px;'></img></div>"
            );
            MakeImageFiles(book, "image1", "image2");
            MakeEpub("output", "ImageStyles_ConvertedToPercent" + sizeClass, book);
            CheckBasicsInManifest();
            CheckBasicsInPage();
            CheckBasicsInGivenPage(2);
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data, "pg1", "1"); // BringBookUpToDate in MakeEpub fixes the page numbering back to reality...
            CheckEpubTypeAttributes(page2Data, null);

            // A5Portrait page is 297/2 mm wide
            // Percent size however is relative to containing block, typically the marginBox,
            // which is inset 40mm from page
            // a px in a printed book is exactly 1/96 in.
            // 25.4mm.in
            var marginboxInches = (pageWidthMm - 40) / 25.4;
            var picWidthInches = 334 / 96.0;
            var widthPercent = Math.Round(picWidthInches / marginboxInches * 1000) / 10;
            var picIndentInches = 34 / 96.0;
            var picIndentPercent = Math.Round(picIndentInches / marginboxInches * 1000) / 10;
            AssertThatXmlIn
                .String(page2Data)
                .HasAtLeastOneMatchForXpath(
                    "//xhtml:img[@style='width:"
                        + widthPercent.ToString("F1")
                        + "%; height:auto; margin-left: "
                        + picIndentPercent.ToString("F1")
                        + "%; margin-top: 0px;']",
                    _ns
                );

            picWidthInches = 330 / 96.0;
            widthPercent = Math.Round(picWidthInches / marginboxInches * 1000) / 10;
            picIndentInches = 33 / 96.0;
            picIndentPercent = Math.Round(picIndentInches / marginboxInches * 1000) / 10;
            AssertThatXmlIn
                .String(page2Data)
                .HasAtLeastOneMatchForXpath(
                    "//xhtml:img[@style='width:"
                        + widthPercent.ToString("F1")
                        + "%; height:auto; margin-left: "
                        + picIndentPercent.ToString("F1")
                        + "%; margin-top: 0px;']",
                    _ns
                );
        }

        [Test]
        public void ImageStyles_ConvertedToPercent_SpecialCases()
        {
            // First image triggers special case for missing height
            // Second image triggers special cases for no width at all.
            var book = SetupBookLong(
                "This is some text",
                "en",
                extraPageClass: " A5Portrait numberedPage' data-page-number='3",
                extraContentOutsideTranslationGroup: @"<div><img src='image1.png' width='334' height='220' style='width:334px; margin-left: 34px; margin-top: 0px;'></img></div>
							<div><img src='image2.png' width='330' height='220' style='margin-top: 0px;'></img></div>"
            );
            MakeImageFiles(book, "image1", "image2");
            MakeEpub("output", "ImageStyles_ConvertedToPercent_SpecialCases", book);
            CheckBasicsInManifest();
            CheckBasicsInPage();
            CheckBasicsInGivenPage(2);
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data, "pg1", "1"); // page numbers restored to reality by BringBookUpToDate in MakeEpub
            CheckEpubTypeAttributes(page2Data, null);

            AssertThatXmlIn
                .String(page2Data)
                .HasAtLeastOneMatchForXpath("//xhtml:img[@style='margin-top: 0px;']", _ns);
            var marginboxInches = (297.0 / 2.0 - 40) / 25.4;
            var picWidthInches = 334 / 96.0;
            var widthPercent = Math.Round(picWidthInches / marginboxInches * 1000) / 10;
            var picIndentInches = 34 / 96.0;
            var picIndentPercent = Math.Round(picIndentInches / marginboxInches * 1000) / 10;
            AssertThatXmlIn
                .String(page2Data)
                .HasAtLeastOneMatchForXpath(
                    "//xhtml:img[@style='height:auto; width:"
                        + widthPercent.ToString("F1")
                        + "%; margin-left: "
                        + picIndentPercent.ToString("F1")
                        + "%; margin-top: 0px;']",
                    _ns
                );
            AssertThatXmlIn
                .String(page2Data)
                .HasAtLeastOneMatchForXpath("//xhtml:img[@style='margin-top: 0px;']", _ns);
        }

        [Test]
        public void ImageStyles_PercentsAdjustForContainingPercentDivs()
        {
            var book = SetupBookLong(
                "This is some text",
                "en",
                extraPageClass: " A5Portrait numberedPage' data-page-number='99",
                extraContentOutsideTranslationGroup: @"<div id='anotherWrapper' style='width:80%'>
								<div id='innerrWrapper' style='width:50%'>
									<div><img src='image1.png' width='40' height='220' style='width:40px; height:220px; margin-left: 14px; margin-top: 0px;'></img></div>
								</div>
							</div>"
            );
            MakeImageFiles(book, "image1");
            MakeEpub("output", "ImageStyles_PercentsAdjustForContainingPercentDivs", book);
            CheckBasicsInManifest("image1");
            CheckBasicsInPage();
            CheckBasicsInGivenPage(2, "image1");
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data, "pg1", "1"); // fixed by BringBookUpToDate in MakeEpub
            CheckEpubTypeAttributes(page2Data, null);

            // A5Portrait page is 297/2 mm wide
            // Percent size however is relative to containing block,
            // which in this case is 50% of 80% of the marginBox,
            // which is inset 40mm from page
            // a px in a printed book is exactly 1/96 in.
            // 25.4mm.in
            var marginboxInches = (297.0 / 2.0 - 40) / 25.4;
            var picWidthInches = 40 / 96.0;
            var parentWidthInches = marginboxInches * 0.8 * 0.5;
            var widthPercent = Math.Round(picWidthInches / parentWidthInches * 1000) / 10;
            var picIndentInches = 14 / 96.0;
            var picIndentPercent = Math.Round(picIndentInches / parentWidthInches * 1000) / 10;
            AssertThatXmlIn
                .String(page2Data)
                .HasAtLeastOneMatchForXpath(
                    "//xhtml:img[@style='width:"
                        + widthPercent.ToString("F1")
                        + "%; height:auto; margin-left: "
                        + picIndentPercent.ToString("F1")
                        + "%; margin-top: 0px;']",
                    _ns
                );
        }

        #region Audio Overlay tests
        [TestCase(TalkingBookApi.AudioRecordingMode.Sentence)]
        [TestCase(TalkingBookApi.AudioRecordingMode.TextBox)]
        public void BookWithAudio_ProducesOverlay_OmitsInvalidAttrs(
            TalkingBookApi.AudioRecordingMode audioRecordingMode
        )
        {
            BloomBook book;
            if (audioRecordingMode == TalkingBookApi.AudioRecordingMode.Sentence)
            {
                book = SetupBook(
                    "<p><span id='a123' class='audio-sentence' recordingmd5='undefined'>This is some text.</span><span class='audio-sentence' id='a23'>Another sentence</span></p>",
                    "xyz"
                );
            }
            else if (audioRecordingMode == TalkingBookApi.AudioRecordingMode.TextBox)
            {
                book = SetupBookLong(
                    text: "<p>This is some text.</p>",
                    lang: "xyz",
                    extraEditDivClasses: "audio-sentence' id='a123' recordingmd5='undefined", // Injecting other attributes into the "class" field as well in order to create the ID attribute simultaneously
                    extraContentOutsideTranslationGroup: "<div class='bloom-translationGroup'><div lang='xyz' class='bloom-editable audio-sentence' id='a23'><p>Another sentence</p></div></div>"
                );
            }
            else
            {
                book = null;
                Assert.Fail("Not implemented");
            }

            // Note: in an earlier version, this test used an mp4 extension for a123. We no longer know why.
            // It seems quite unrelated to removing any attributes. This led to confusion because MakeFakeAudio
            // makes a .wav file as well as the extension it is asked to make, and (possibly some other change)
            // caused HasFullAudio to detect the wav file and decide the book had complete audio coverage,
            // which at that point the test was not expecting.
            // We decided to change it so the book is expected to have full audio coverage.
            MakeFakeAudio(book.FolderPath.CombineForPath("audio", "a123.mp3"));
            MakeFakeAudio(book.FolderPath.CombineForPath("audio", "a23.mp3"));
            MakeEpub("output", $"BookWithAudio_ProducesOverlay_{audioRecordingMode}", book);
            CheckBasicsInManifest();

            CheckAccessibilityInManifest(
                true,
                false,
                false,
                _defaultSourceValue,
                hasFullAudio: true
            );

            CheckBasicsInPage();
            CheckBasicsInGivenPage(2);
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data);
            CheckEpubTypeAttributes(page2Data, null);
            CheckNavPage();
            CheckFontStylesheet();

            // xpath search for slash in attribute value fails (something to do with interpreting it as a namespace reference?)
            var assertThatManifest = AssertThatXmlIn.String(
                FixContentForXPathValueSlash(_manifestContent)
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='f2' and @href='2.xhtml' and @media-overlay='f2_overlay']"
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='f2_overlay' and @href='2_overlay.smil' and @media-type='application^slash^smil+xml']"
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='a23' and @href='"
                    + kAudioSlash
                    + "a23.mp3' and @media-type='audio^slash^mpeg']"
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='a123' and @href='"
                    + kAudioSlash
                    + "a123.mp3' and @media-type='audio^slash^mpeg']"
            );

            var smilData = StripXmlHeader(
                ExportEpubTestsBaseClass.GetZipContent(_epub, "content/2_overlay.smil")
            );
            var assertThatSmil = AssertThatXmlIn.String(FixContentForXPathValueSlash(smilData));
            assertThatSmil.HasAtLeastOneMatchForXpath(
                "smil:smil/smil:body/smil:seq[@epub:textref='2.xhtml' and @epub:type='bodymatter chapter']",
                _ns
            );
            assertThatSmil.HasAtLeastOneMatchForXpath(
                "smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:text[@src='2.xhtml#a123']",
                _ns
            );
            assertThatSmil.HasAtLeastOneMatchForXpath(
                "smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:text[@src='2.xhtml#a23']",
                _ns
            );
            if (Platform.IsLinux)
            {
                // Ffmpeg based audio time calculations on Linux don't quite match what NAudio produces on Windows.
                assertThatSmil.HasAtLeastOneMatchForXpath(
                    "smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:audio[@src='"
                        + kAudioSlash
                        + "a123.mp3' and @clipBegin='0:00:00.000' and @clipEnd='0:00:01.600']",
                    _ns
                );
                assertThatSmil.HasAtLeastOneMatchForXpath(
                    "smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:audio[@src='"
                        + kAudioSlash
                        + "a23.mp3' and @clipBegin='0:00:00.000' and @clipEnd='0:00:01.600']",
                    _ns
                );
            }
            else
            {
                var audioDuration = GetFakeAudioDurationSecs();
                // use culture invariant form for test
                string clipEnd =
                    "0:00:0" + audioDuration.ToString("0.000", CultureInfo.InvariantCulture);
                assertThatSmil.HasAtLeastOneMatchForXpath(
                    "smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:audio[@src='"
                        + kAudioSlash
                        + $"a123.mp3' and @clipBegin='0:00:00.000' and @clipEnd='{clipEnd}']",
                    _ns
                );
                assertThatSmil.HasAtLeastOneMatchForXpath(
                    "smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:audio[@src='"
                        + kAudioSlash
                        + $"a23.mp3' and @clipBegin='0:00:00.000' and @clipEnd='{clipEnd}']",
                    _ns
                );
            }

            if (audioRecordingMode == TalkingBookApi.AudioRecordingMode.Sentence)
            {
                AssertThatXmlIn
                    .String(page2Data)
                    .HasAtLeastOneMatchForXpath("//span[@id='a123' and not(@recordingmd5)]");
            }
            else if (audioRecordingMode == TalkingBookApi.AudioRecordingMode.TextBox)
            {
                AssertThatXmlIn
                    .String(page2Data)
                    .HasAtLeastOneMatchForXpath("//div[@id='a123' and not(@recordingmd5)]");
            }

            VerifyEpubItemExists("content/" + EpubMaker.kAudioFolder + "/a123.mp3");
            VerifyEpubItemExists("content/" + EpubMaker.kAudioFolder + "/a23.mp3");
        }

        [TestCase(TalkingBookApi.AudioRecordingMode.Sentence, true)]
        [TestCase(TalkingBookApi.AudioRecordingMode.Sentence, false)]
        [TestCase(TalkingBookApi.AudioRecordingMode.TextBox, true)]
        [TestCase(TalkingBookApi.AudioRecordingMode.TextBox, false)]
        public void BookWithAudio_OneAudioPerPage_ProducesOneMp3(
            TalkingBookApi.AudioRecordingMode audioRecordingMode,
            bool startWithWav
        )
        {
            BloomBook book;
            if (audioRecordingMode == TalkingBookApi.AudioRecordingMode.Sentence)
            {
                book = SetupBook(
                    "<span class='audio-sentence' id='a123' recordingmd5='undefined'>This is some text.</span><span class='audio-sentence' id='a23'>Another sentence</span>",
                    "xyz"
                );
            }
            else if (audioRecordingMode == TalkingBookApi.AudioRecordingMode.TextBox)
            {
                book = SetupBookLong(
                    text: "<p>This is some text.</p>",
                    lang: "xyz",
                    extraEditDivClasses: "audio-sentence' id='a123", // Injecting other attributes into the "class" field as well in order to create the ID attribute simultaneously
                    extraContentOutsideTranslationGroup: "<div class='bloom-translationGroup'><div lang='xyz' class='bloom-editable audio-sentence' id='a23'><p>Another sentence</p></div></div>"
                );
            }
            else
            {
                book = null;
                Assert.Fail("Invalid test input");
            }

            MakeFakeAudio(book.FolderPath.CombineForPath("audio", "a123.mp3"), startWithWav);
            MakeFakeAudio(book.FolderPath.CombineForPath("audio", "a23.mp3"), startWithWav);
            MakeEpub(
                "output",
                $"BookWithAudio_MergeAudio_ProducesOneMp3_{audioRecordingMode}_{startWithWav}",
                book,
                extraInit: maker => maker.OneAudioPerPage = true
            );

            // xpath search for slash in attribute value fails (something to do with interpreting it as a namespace reference?)
            var assertThatManifest = AssertThatXmlIn.String(
                FixContentForXPathValueSlash(_manifestContent)
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='f2' and @href='2.xhtml' and @media-overlay='f2_overlay']"
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='f2_overlay' and @href='2_overlay.smil' and @media-type='application^slash^smil+xml']"
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='page2' and @href='"
                    + kAudioSlash
                    + "page2.mp3' and @media-type='audio^slash^mpeg']"
            );

            var smilData = StripXmlHeader(
                ExportEpubTestsBaseClass.GetZipContent(_epub, "content/2_overlay.smil")
            );
            var assertThatSmil = AssertThatXmlIn.String(FixContentForXPathValueSlash(smilData));
            assertThatSmil.HasAtLeastOneMatchForXpath(
                "smil:smil/smil:body/smil:seq[@epub:textref='2.xhtml' and @epub:type='bodymatter chapter']",
                _ns
            );
            assertThatSmil.HasAtLeastOneMatchForXpath(
                "smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:text[@src='2.xhtml#a123']",
                _ns
            );
            assertThatSmil.HasAtLeastOneMatchForXpath(
                "smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:text[@src='2.xhtml#a23']",
                _ns
            );
            if (Platform.IsLinux)
            {
                // Ffmpeg based audio time calculations on Linux don't quite match what NAudio produces on Windows.
                assertThatSmil.HasAtLeastOneMatchForXpath(
                    "smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:audio[@src='"
                        + kAudioSlash
                        + "page2.mp3' and @clipBegin='0:00:00.000' and @clipEnd='0:00:01.600']",
                    _ns
                );
                assertThatSmil.HasAtLeastOneMatchForXpath(
                    "smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:audio[@src='"
                        + kAudioSlash
                        + "page2.mp3' and @clipBegin='0:00:01.600' and @clipEnd='0:00:03.200']",
                    _ns
                );
            }
            else
            {
                var audioDuration = GetFakeAudioDurationSecs();
                // Make these clip timings culture invariant for testing.
                string clipEnd1 =
                    "0:00:0" + audioDuration.ToString("0.000", CultureInfo.InvariantCulture);
                string clipEnd2 =
                    "0:00:0" + (audioDuration * 2).ToString("0.000", CultureInfo.InvariantCulture);
                assertThatSmil.HasAtLeastOneMatchForXpath(
                    "smil:smil/smil:body/smil:seq/smil:par[@id='s1']/smil:audio[@src='"
                        + kAudioSlash
                        + $"page2.mp3' and @clipBegin='0:00:00.000' and @clipEnd='{clipEnd1}']",
                    _ns
                );
                assertThatSmil.HasAtLeastOneMatchForXpath(
                    "smil:smil/smil:body/smil:seq/smil:par[@id='s2']/smil:audio[@src='"
                        + kAudioSlash
                        + $"page2.mp3' and @clipBegin='{clipEnd1}' and @clipEnd='{clipEnd2}']",
                    _ns
                );
            }

            var page2Data = GetPageNData(2);
            if (audioRecordingMode == TalkingBookApi.AudioRecordingMode.Sentence)
                AssertThatXmlIn
                    .String(page2Data)
                    .HasAtLeastOneMatchForXpath("//span[@id='a123' and not(@recordingmd5)]");
            else if (audioRecordingMode == TalkingBookApi.AudioRecordingMode.TextBox)
                AssertThatXmlIn
                    .String(page2Data)
                    .HasAtLeastOneMatchForXpath("//div[@id='a123' and not(@recordingmd5)]");

            VerifyEpubItemExists("content/" + EpubMaker.kAudioFolder + "/page2.mp3");
        }

        /// <summary>
        /// There's some special-case code for Ids that start with digits that we test here.
        /// This test has been extended to verify that we get media:duration metadata
        /// </summary>
        [TestCase(TalkingBookApi.AudioRecordingMode.Sentence, true)]
        [TestCase(TalkingBookApi.AudioRecordingMode.Sentence, false)]
        [TestCase(TalkingBookApi.AudioRecordingMode.TextBox, true)]
        [TestCase(TalkingBookApi.AudioRecordingMode.TextBox, false)]
        public void AudioWithParagraphsAndRealGuids_ProducesOverlay(
            TalkingBookApi.AudioRecordingMode audioRecordingMode,
            bool startWithWav
        )
        {
            BloomBook book;
            if (audioRecordingMode == TalkingBookApi.AudioRecordingMode.Sentence)
            {
                book = SetupBook(
                    "<p><span class='audio-sentence' id='e993d14a-0ec3-4316-840b-ac9143d59a2c'>This is some text.</span><span class='audio-sentence' id='i0d8e9910-dfa3-4376-9373-a869e109b763'>Another sentence</span></p>",
                    "xyz"
                );
            }
            else if (audioRecordingMode == TalkingBookApi.AudioRecordingMode.TextBox)
            {
                var extraEditDivClassesAndAttributes =
                    "audio-sentence' id='e993d14a-0ec3-4316-840b-ac9143d59a2c"; // Injecting other attributes into the "class" field as well in order to create the ID attribute simultaneously
                var extraContentOutsideTranslationGroup =
                    "<div class='bloom-translationGroup'><div lang='xyz' class='bloom-editable audio-sentence' id='i0d8e9910-dfa3-4376-9373-a869e109b763'><p>Text for editable 2</p></div></div>";
                book = SetupBookLong(
                    text: "<p>This is some text.</p>",
                    lang: "xyz",
                    extraEditDivClasses: extraEditDivClassesAndAttributes,
                    extraContentOutsideTranslationGroup: extraContentOutsideTranslationGroup
                );
            }
            else
            {
                book = null;
                Assert.Fail("Invalid test input");
            }

            MakeFakeAudio(
                book.FolderPath.CombineForPath("audio", "e993d14a-0ec3-4316-840b-ac9143d59a2c.mp3"),
                startWithWav
            );
            MakeFakeAudio(
                book.FolderPath.CombineForPath(
                    "audio",
                    "i0d8e9910-dfa3-4376-9373-a869e109b763.mp3"
                ),
                startWithWav
            );
            MakeEpub(
                "output",
                $"AudioWithParagraphsAndRealGuids_ProducesOverlay_{audioRecordingMode}_{startWithWav}",
                book
            );
            CheckBasicsInManifest();
            CheckBasicsInPage();
            CheckBasicsInGivenPage(2);
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data);
            CheckEpubTypeAttributes(page2Data, null);
            CheckNavPage();
            CheckFontStylesheet();

            var assertManifest = AssertThatXmlIn.String(
                FixContentForXPathValueSlash(_manifestContent)
            );
            assertManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='f2' and @href='2.xhtml' and @media-overlay='f2_overlay']"
            );
            assertManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='f2_overlay' and @href='2_overlay.smil' and @media-type='application^slash^smil+xml']"
            );

            double totalExpectedDuration = GetFakeAudioDurationSecs() * 2;
            // Make this duration culture invariant for testing.
            string expectedDurationFormatted =
                "00:00:0"
                + totalExpectedDuration.ToString("0.0000000", CultureInfo.InvariantCulture);
            assertManifest.HasAtLeastOneMatchForXpath(
                $"package/metadata/meta[@property='media:duration' and not(@refines) and text()='{expectedDurationFormatted}']"
            );
            assertManifest.HasAtLeastOneMatchForXpath(
                $"package/metadata/meta[@property='media:duration' and @refines='#f2_overlay' and text()='{expectedDurationFormatted}']"
            );

            var smilData = StripXmlHeader(
                ExportEpubTestsBaseClass.GetZipContent(_epub, "content/2_overlay.smil")
            );
            var assertSmil = AssertThatXmlIn.String(FixContentForXPathValueSlash(smilData));
            string smilSeqPrefix = "smil:smil/smil:body/smil:seq";
            assertSmil.HasAtLeastOneMatchForXpath(
                $"{smilSeqPrefix}[@epub:textref='2.xhtml' and @epub:type='bodymatter chapter']",
                _ns
            );
            assertSmil.HasAtLeastOneMatchForXpath(
                $"{smilSeqPrefix}/smil:par[@id='s1']/smil:text[@src='2.xhtml#e993d14a-0ec3-4316-840b-ac9143d59a2c']",
                _ns
            );
            assertSmil.HasAtLeastOneMatchForXpath(
                $"{smilSeqPrefix}/smil:par[@id='s2']/smil:text[@src='2.xhtml#i0d8e9910-dfa3-4376-9373-a869e109b763']",
                _ns
            );

            assertSmil.HasAtLeastOneMatchForXpath(
                $"{smilSeqPrefix}/smil:par[@id='s1']/smil:audio[@src='{kAudioSlash}e993d14a-0ec3-4316-840b-ac9143d59a2c.mp3']",
                _ns
            );
            assertSmil.HasAtLeastOneMatchForXpath(
                $"{smilSeqPrefix}/smil:par[@id='s2']/smil:audio[@src='{kAudioSlash}i0d8e9910-dfa3-4376-9373-a869e109b763.mp3']",
                _ns
            );
            VerifyEpubItemExists(
                "content/" + EpubMaker.kAudioFolder + "/e993d14a-0ec3-4316-840b-ac9143d59a2c.mp3"
            );
            VerifyEpubItemExists(
                "content/" + EpubMaker.kAudioFolder + "/i0d8e9910-dfa3-4376-9373-a869e109b763.mp3"
            );
        }

        /// <summary>
        /// Tests that the correct audio overlay is produced for each of the playback modes that do not include sub-element playback
        /// </summary>
        [TestCase("Sentence", true)]
        [TestCase("Sentence", false)]
        [TestCase("HardSplit", true)]
        [TestCase("HardSplit", false)]
        [TestCase("TextBox", true)]
        [TestCase("TextBox", false)]
        public void AddAudioOverlay_NoSubElementPlaybackModes_ProducesCorrectTimings(
            string audioRecordingMode,
            bool mergeAudio
        )
        {
            // Setup //
            var expectedIds = new string[] { "id1", "id2" };
            BloomBook book;

            if (audioRecordingMode == "Sentence")
            {
                book = SetupBook(
                    "<p><span class='audio-sentence' id='id1'>Sentence 1.</span><span class='audio-sentence' id='id2'>Sentence 2</span></p>",
                    "xyz"
                );
            }
            else if (audioRecordingMode == "HardSplit")
            {
                string divContents =
                    @"<p>
	<span id='id1' class='audio-sentence'>Sentence 1</span>
	<span id='id2' class='audio-sentence'>Sentence 2</span>
</p>";
                var extraEditDivClassesAndAttributes = "' id='textBoxId"; // Injecting other attributes into the "class" field as well in order to create the ID attribute simultaneously
                book = SetupBookLong(
                    text: divContents,
                    lang: "xyz",
                    extraEditDivClasses: extraEditDivClassesAndAttributes
                );
            }
            else if (audioRecordingMode == "TextBox")
            {
                string extraEditDivClassesAndAttributes = "audio-sentence' id='id1"; // Injecting other attributes into the "class" field as well in order to create the ID attribute simultaneously
                string extraContentOutsideTranslationGroup =
                    "<div class='bloom-translationGroup'><div lang='xyz' class='bloom-editable audio-sentence' id='id2'><p>Text for editable 2</p></div></div>";
                book = SetupBookLong(
                    text: "<p>Text for Editable 1</p>",
                    lang: "xyz",
                    extraEditDivClasses: extraEditDivClassesAndAttributes,
                    extraContentOutsideTranslationGroup: extraContentOutsideTranslationGroup
                );
            }
            else
            {
                book = null;
                Assert.Fail("Invalid test input");
            }

            foreach (string expectedId in expectedIds)
            {
                MakeFakeAudio(book.FolderPath.CombineForPath("audio", $"{expectedId}.mp3"));
            }

            // Cause the system under test to be executed.
            MakeEpub(
                "output",
                $"AddAudioOverlay_SentencePlaybackModes_ProducesCorrectOverlay_{audioRecordingMode}_{mergeAudio}",
                book,
                extraInit: maker => maker.OneAudioPerPage = mergeAudio
            );

            // Verification // note that the content page is page 2 of the epub: the title page is page 1.
            var smilData = StripXmlHeader(
                ExportEpubTestsBaseClass.GetZipContent(_epub, "content/2_overlay.smil")
            );
            var assertSmil = AssertThatXmlIn.String(FixContentForXPathValueSlash(smilData));

            string smilSeqPrefix = "smil:smil/smil:body/smil:seq";
            double expectedDurationPerClip = GetFakeAudioDurationSecs();

            for (int i = 0; i < expectedIds.Length; ++i)
            {
                string expectedId = expectedIds[i];
                string expectedFilename = mergeAudio ? "page2" : expectedId;
                double expectedClipBegin = mergeAudio ? (expectedDurationPerClip * i) : 0;
                double expectedClipEnd = expectedClipBegin + expectedDurationPerClip;
                // Make these clip timings culture invariant for testing.
                string expectedClipBeginFormatted =
                    "0:00:0" + expectedClipBegin.ToString("0.000", CultureInfo.InvariantCulture);
                string expectedClipEndFormatted =
                    "0:00:0" + expectedClipEnd.ToString("0.000", CultureInfo.InvariantCulture);

                assertSmil.HasAtLeastOneMatchForXpath(
                    $"{smilSeqPrefix}/smil:par[@id='s{i + 1}']/smil:text[@src='2.xhtml#{expectedId}']",
                    _ns
                );
                assertSmil.HasAtLeastOneMatchForXpath(
                    $"{smilSeqPrefix}/smil:par[@id='s{i + 1}']/smil:audio[@src='{kAudioSlash}{expectedFilename}.mp3'][@clipBegin='{expectedClipBeginFormatted}'][@clipEnd='{expectedClipEndFormatted}']",
                    _ns
                );
                VerifyEpubItemExists($"content/{EpubMaker.kAudioFolder}/{expectedFilename}.mp3");
            }
        }

        /// <summary>
        /// Tests that the correct audio overlay is produced for a text box that has been Soft Split
        /// </summary>
        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(false, true)]
        public void AddAudioOverlay_SubElementPlaybackModes_ProducesCorrectTimings(
            bool mergeAudio,
            bool switchCulture
        )
        {
            var originalCulture = CultureInfo.CurrentCulture;
            if (switchCulture)
            {
                // This culture -- Swedish (Sweden) -- is one of many which uses a comma for the decimal separator,
                // hence its significance in this case. See https://issues.bloomlibrary.org/youtrack/issue/BL-9079.
                CultureInfo.CurrentCulture = new CultureInfo("se-SE", false);
            }

            // Setup //
            double expectedDurationPerClip = GetFakeAudioDurationSecs();
            double expectedDurationPerSegment = expectedDurationPerClip / 2;

            var extraEditDivClassesAndAttributes = FormattableString.Invariant(
                $"audio-sentence' id='audio1' data-audiorecordingendtimes='{expectedDurationPerSegment} {expectedDurationPerClip}"
            ); // Injecting other attributes into the "class" field as well in order to create the ID attribute simultaneously
            string firstDivContents =
                @"<p>
	<span id='text1' class='bloom-highlightSegment'>Sentence 1</span>
	<span id='text2' class='bloom-highlightSegment'>Sentence 2</span>
</p>";

            string secondDivHtml = FormattableString.Invariant(
                $@"<div class='bloom-translationGroup'>
	<div lang='xyz' class='bloom-editable audio-sentence' id='audio2' data-audiorecordingendtimes='{expectedDurationPerSegment} {expectedDurationPerClip}'>
		<p>
			<span id='text3' class='bloom-highlightSegment'>Sentence 3.</span>
			<span id='text4' class='bloom-highlightSegment'>Sentence 4.</span>
		</p>
	</div>
</div>"
            );

            var book = SetupBookLong(
                text: firstDivContents,
                lang: "xyz",
                extraEditDivClasses: extraEditDivClassesAndAttributes,
                extraContentOutsideTranslationGroup: secondDivHtml
            );

            MakeFakeAudio(book.FolderPath.CombineForPath("audio", $"audio1.mp3"));
            MakeFakeAudio(book.FolderPath.CombineForPath("audio", $"audio2.mp3"));

            // Cause the system under test to be executed.
            MakeEpub(
                "output",
                $"AddAudioOverlay_SubElementPlaybackModes_ProducesCorrectTimings_{mergeAudio}_{switchCulture}",
                book,
                extraInit: maker => maker.OneAudioPerPage = mergeAudio
            );

            // Verification //
            var smilData = StripXmlHeader(
                ExportEpubTestsBaseClass.GetZipContent(_epub, "content/2_overlay.smil")
            );
            var assertSmil = AssertThatXmlIn.String(FixContentForXPathValueSlash(smilData));

            string smilSeqPrefix = "smil:smil/smil:body/smil:seq";

            for (int divIndex = 0; divIndex < 2; ++divIndex)
            {
                string expectedFilename = mergeAudio ? "page2" : $"audio{divIndex + 1}";
                double divStartTime = mergeAudio ? (divIndex * expectedDurationPerClip) : 0;

                for (int j = 0; j < 2; ++j)
                {
                    int expectedIndex = (divIndex) * 2 + j + 1;
                    double expectedClipBegin = divStartTime + expectedDurationPerSegment * j;
                    double expectedClipEnd = expectedClipBegin + expectedDurationPerSegment;
                    string expectedClipBeginFormatted =
                        "0:00:0"
                        + expectedClipBegin.ToString("0.000", CultureInfo.InvariantCulture);
                    string expectedClipEndFormatted =
                        "0:00:0" + expectedClipEnd.ToString("0.000", CultureInfo.InvariantCulture);

                    assertSmil.HasAtLeastOneMatchForXpath(
                        $"smil:smil/smil:body/smil:seq/smil:par[@id='s{expectedIndex}']/smil:text[@src='2.xhtml#text{expectedIndex}']",
                        _ns
                    );
                    assertSmil.HasAtLeastOneMatchForXpath(
                        $"{smilSeqPrefix}/smil:par[@id='s{expectedIndex}']/smil:audio[@src='{kAudioSlash}{expectedFilename}.mp3'][@clipBegin='{expectedClipBeginFormatted}'][@clipEnd='{expectedClipEndFormatted}']",
                        _ns
                    );
                }
                VerifyEpubItemExists($"content/{EpubMaker.kAudioFolder}/{expectedFilename}.mp3");
            }

            CultureInfo.CurrentCulture = originalCulture;
        }
        #endregion

        // Sometimes the tests were failing on TeamCity because the file was in use by another process
        // (I'm assuming that was another test since a few use the same path).
        private static readonly object s_thisLock = new object();

        protected void MakeFakeAudio(string path, bool alsoProduceWav = true)
        {
            lock (s_thisLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                // Bloom is going to try to figure its duration, so put a real audio file there.
                var src = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(
                    "src/BloomTests/Publish/sample_audio.mp3"
                );
                File.Copy(src, path);

                if (alsoProduceWav)
                {
                    var wavSrc = Path.ChangeExtension(src, ".wav");
                    File.Copy(wavSrc, Path.ChangeExtension(path, "wav"), true);
                }
            }
        }

        /// <summary>
        /// The duration, in seconds, of the audio produced by MakeFakeAudio()
        /// </summary>
        /// <returns></returns>
        protected static double GetFakeAudioDurationSecs()
        {
            // Formerly 1.672 on Windows, but after updating NAudio to 1.10.0 nuget package, now 1.646 seconds instead
            double expectedDurationPerClip = 1.646;
            if (Platform.IsLinux)
            {
                // Ffmpeg based audio time calculations on Linux don't quite match what NAudio produces on Windows.
                expectedDurationPerClip = 1.6;
            }

            return expectedDurationPerClip;
        }

        [TestCase("abc", ExpectedResult = "abc")]
        [TestCase("123", ExpectedResult = "f123")]
        [TestCase("123 abc", ExpectedResult = "f123abc")]
        [TestCase("x*y", ExpectedResult = "x_y")]
        [TestCase("x:y", ExpectedResult = "x:y")]
        [TestCase("*edf", ExpectedResult = "_edf")]
        [TestCase("a\u0300z", ExpectedResult = "a\u0300z")] // valid mid character
        [TestCase("\u0300z", ExpectedResult = "f\u0300z")] // invalid start character
        [TestCase("a\u037Ez", ExpectedResult = "a_z")] // high-range invalid
        [TestCase("-abc", ExpectedResult = "f-abc")]
        [Test]
        public string ToValidXmlId(string input)
        {
            return EpubMaker.ToValidXmlId(input);
        }

        [Test]
        public void IllegalIds_AreFixed()
        {
            var book = SetupBookLong("This is some text", "xyz", parentDivId: "12");
            MakeEpub("output", "IllegalIds_AreFixed", book);
            CheckBasicsInManifest();
            CheckBasicsInPage();
            CheckBasicsInGivenPage(2);
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data);
            CheckEpubTypeAttributes(page2Data, null);

            // This is possibly too strong; see comment where we remove them.
            AssertThatXmlIn.String(page2Data).HasAtLeastOneMatchForXpath("//div[@id='i12']");
        }

        [Test]
        public void ConflictingIds_AreNotConfused()
        {
            // Files called 12.png and f12.png are quite safe and distinct as files, but they will generate the same ID,
            // so one must be fixed. The second occurrence of f12, however, should not cause another file to be generated.
            var book = SetupBook("This is some text", "xyz", "12", "f12", "f12");
            MakeImageFiles(book, "12", "f12");
            MakeEpub("output", "ConflictingIds_AreNotConfused", book);
            CheckFolderStructure();
            CheckBasicsInManifest(); // don't check the file stuff here, we're looking at special cases
            CheckBasicsInPage();
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data);
            CheckEpubTypeAttributes(page2Data, null);
            CheckNavPage();
            CheckFontStylesheet();

            var assertThatManifest = AssertThatXmlIn.String(
                FixContentForXPathValueSlash(_manifestContent)
            );
            assertThatManifest.HasAtLeastOneMatchForXpath(
                "package/manifest/item[@id='f12' and @href='" + kImagesSlash + "12.png']"
            );
            assertThatManifest.HasSpecifiedNumberOfMatchesForXpath(
                "package/manifest/item[@id='f121' and @href='" + kImagesSlash + "f12.png']",
                1
            );
            assertThatManifest.HasNoMatchForXpath(
                "package/manifest/item[@href='" + kImagesSlash + "f121.png']"
            ); // What it would typically generate if it made another copy.

            AssertThatXmlIn
                .String(page2Data)
                .HasAtLeastOneMatchForXpath("//img[@src='" + kImagesSlash + "12.png']");
            AssertThatXmlIn
                .String(page2Data)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//img[@src='" + kImagesSlash + "f12.png']",
                    2
                );

            VerifyEpubItemExists("content/" + EpubMaker.kImagesFolder + "/12.png");
            VerifyEpubItemExists("content/" + EpubMaker.kImagesFolder + "/f12.png");
        }

        /// <summary>
        /// Also tests various odd characters that might be in file names. The &amp; is not technically
        /// valid in an href but it's what Bloom currently does for & in file name.
        /// </summary>
        [Test]
        public void FilesThatMapToSameSafeName_AreNotConfused()
        {
#if __MonoCS__
            // The first two image src values used here will both initially attempt to save as my_3dImage.png, so one must be renamed.
            // The latter two image src values both try to save as my_image.png, so one must be renamed.
            var book = SetupBook(
                "This is some text",
                "en",
                "my%3Dimage",
                "my_3dimage",
                "my%2bimage",
                "my&amp;image"
            );
            MakeImageFiles(book, "my=image", "my_3dimage", "my+image", "my&image");
#else
            // The two image src values used here will both initially attempt to save as my_3DImage.png, so one will have to be renamed
            var book = SetupBook(
                "This is some text",
                "en",
                "my%3Dimage",
                "my_3Dimage",
                "my%2bimage",
                "my&amp;image"
            );
            MakeImageFiles(book, "my=image", "my_3Dimage", "my+image", "my&image");
#endif
            MakeEpub("output", "FilesThatMapToSameSafeName_AreNotConfused", book);
            CheckFolderStructure();
            CheckBasicsInPage();
#if __MonoCS__
            CheckBasicsInManifest("my_3dimage", "my_3dimage1", "my_image", "my_image1");
            CheckBasicsInGivenPage(2, "my_3dimage", "my_3dimage1", "my_image", "my_image1");
#else
            CheckBasicsInManifest("my_3dimage", "my_3Dimage1", "my_image", "my_image1");
            CheckBasicsInGivenPage(2, "my_3dimage", "my_3Dimage1", "my_image", "my_image1");
#endif
            var page2Data = GetPageNData(2);
            CheckPageBreakMarker(page2Data);
            CheckEpubTypeAttributes(page2Data, null);
            CheckNavPage();
            CheckFontStylesheet();
        }

        [Test]
        public void AddFontFace_WithNullPath_AddsNothing()
        {
            var sb = new StringBuilder();
            Assert.DoesNotThrow(() => EpubMaker.AddFontFace(sb, "myFont", "bold", "italic", null));
            Assert.That(sb.Length, Is.EqualTo(0));
        }

        [Test]
        public void MultilingualTitlesOrderedCorrectly()
        {
            var book = CreateTestBook(
                @"
<div id='bloomDataDiv'>
    <div data-book='contentLanguage1' lang='*'>xyz</div>
    <div data-book='contentLanguage2' lang='*'>en</div>
    <div data-book='bookTitle' lang='xyz'><p>Livre de Test</p></div>
    <div data-book='bookTitle' lang='en'><p>Test Book</p></div>
</div>
<div class='bloom-page cover coverColor bloom-frontMatter frontCover outsideFrontCover side-right A5Portrait' data-page='required singleton' id='7cecf56f-7e97-443e-910f-ddc13a9b0dfa'>
	<div class='marginBox'>
		<div class='bloom-translationGroup bookTitle' data-default-languages='V,N1'>
			<label class='bubble'>Book title in {lang}</label>
			<div class='bloom-editable bloom-nodefaultstylerule Title-On-Cover-style' lang='z' contenteditable='true' data-book='bookTitle'></div>
			<div class='bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-content2 bloom-contentNational1 bloom-visibility-code-on' lang='en' contenteditable='true' data-book='bookTitle'>
				<p>Test Book</p>
			</div>
			<div class='bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-content1 bloom-visibility-code-on' lang='xyz' contenteditable='true' data-book='bookTitle'>
				<p>Libro de Pruebas</p>
			</div>
			<div class='bloom-editable bloom-nodefaultstylerule Title-On-Cover-style' lang='*' contenteditable='true' data-book='bookTitle'></div>
		</div>
	</div>
</div>",
                createPhysicalFile: true
            );
            MakeEpub("output", "MultilingualTitlesOrderedCorrectly", book);
            CheckBasicsInManifest();
            CheckBasicsInPage();
            CheckNavPage();
            CheckFontStylesheet();
            var dom = new XmlDocument();
            dom.LoadXml(_page1Data);
            var titleDiv =
                dom.DocumentElement.SelectSingleNode(
                    "//xhtml:div[contains(@class, 'bookTitle')]",
                    _ns
                ) as XmlElement;
            Assert.IsNotNull(titleDiv);
            var divs = titleDiv.SelectNodes("./xhtml:div", _ns);
            Assert.AreEqual(2, divs.Count);
            var class0 = divs[0].Attributes["class"];
            Assert.IsNotNull(class0);
            StringAssert.Contains("bloom-content1", class0.Value);
            var class1 = divs[1].Attributes["class"];
            Assert.IsNotNull(class1);
            StringAssert.Contains("bloom-content2", class1.Value);
        }

        [TestCase(TalkingBookApi.AudioRecordingMode.Sentence)]
        [TestCase(TalkingBookApi.AudioRecordingMode.TextBox)]
        public void HasFullAudioCoverage_ReturnsTrue(
            TalkingBookApi.AudioRecordingMode audioRecordingMode
        )
        {
            BloomBook book;
            if (audioRecordingMode == TalkingBookApi.AudioRecordingMode.Sentence)
            {
                book = SetupBook(
                    "\r\n  <p><span id='e993d14a-0ec3-4316-840b-ac9143d59a2d' class='audio-sentence'>This is some text.</span> <span id='i0d8e9910-dfa3-4376-9373-a869e109b764' class='audio-sentence'>Another sentence</span></p>  \r\n",
                    "xyz"
                );
            }
            else if (audioRecordingMode == TalkingBookApi.AudioRecordingMode.TextBox)
            {
                book = SetupBookLong(
                    text: "<p>This is some text.</p>",
                    lang: "xyz",
                    extraEditDivClasses: "audio-sentence' id='e993d14a-0ec3-4316-840b-ac9143d59a2d", // Injecting other attributes into the "class" field as well in order to create the ID attribute simultaneously
                    extraContentOutsideTranslationGroup: "<div class='bloom-translationGroup'><div lang='xyz' class='bloom-editable audio-sentence' id='i0d8e9910-dfa3-4376-9373-a869e109b764'><p>Another sentence</p></div></div>"
                );
            }
            else
            {
                book = null;
                Assert.Fail("Invalid test input");
            }
            MakeFakeAudio(
                book.FolderPath.CombineForPath("audio", "e993d14a-0ec3-4316-840b-ac9143d59a2d.mp3")
            );
            MakeFakeAudio(
                book.FolderPath.CombineForPath("audio", "i0d8e9910-dfa3-4376-9373-a869e109b764.mp3")
            );
            var hasFullAudio = book.HasFullAudioCoverage();
            Assert.IsTrue(hasFullAudio, "HasFullAudioCoverage should return true when appropriate");
            MakeEpub("output", $"HasFullAudioCoverage_ReturnsTrue_{audioRecordingMode}", book);
            CheckFolderStructure();
            CheckBasicsInManifest();
            CheckAccessibilityInManifest(true, false, false, _defaultSourceValue, hasFullAudio); // sound files, but no image files
        }

        [TestCase(TalkingBookApi.AudioRecordingMode.Sentence)]
        [TestCase(TalkingBookApi.AudioRecordingMode.TextBox)]
        public void HasFullAudioCoverage_ReturnsFalseForNoAudio(
            TalkingBookApi.AudioRecordingMode audioRecordingMode
        )
        {
            BloomBook book;
            if (audioRecordingMode == TalkingBookApi.AudioRecordingMode.Sentence)
            {
                book = SetupBook(
                    "\r\n  <p><span id='e993d14a-0ec3-4316-840b-ac9143d59a2f' class='audio-sentence'>This is some text.</span> Another sentence</p>  \r\n",
                    "xyz"
                );
            }
            else if (audioRecordingMode == TalkingBookApi.AudioRecordingMode.TextBox)
            {
                book = SetupBookLong(
                    text: "<p>This is some text.</p>",
                    lang: "xyz",
                    extraEditDivClasses: "audio-sentence' id='e993d14a-0ec3-4316-840b-ac9143d59a2f", // Injecting other attributes into the "class" field as well in order to create the ID attribute simultaneously
                    extraContentOutsideTranslationGroup: "<div class='bloom-translationGroup'><div lang='xyz' class='bloom-editable'><p>Another sentence</p></div></div>"
                );
            }
            else
            {
                book = null;
                Assert.Fail("Invalid test input");
            }
            MakeFakeAudio(
                book.FolderPath.CombineForPath("audio", "e993d14a-0ec3-4316-840b-ac9143d59a2f.mp3")
            );
            var hasFullAudio = book.HasFullAudioCoverage();
            Assert.IsFalse(
                hasFullAudio,
                "HasFullAudioCoverage should return false when not all text is covered by an audio span."
            );
            MakeEpub(
                "output",
                $"HasFullAudioCoverage_ReturnsFalseForNoAudio_{audioRecordingMode}",
                book
            );
            CheckFolderStructure();
            CheckBasicsInManifest();
            CheckAccessibilityInManifest(true, false, false, _defaultSourceValue, hasFullAudio); // sound files, but no image files
        }

        [TestCase(TalkingBookApi.AudioRecordingMode.Sentence)]
        [TestCase(TalkingBookApi.AudioRecordingMode.TextBox)]
        public void HasFullAudioCoverage_ReturnsFalseForMissingAudioFile(
            TalkingBookApi.AudioRecordingMode audioRecordingMode
        )
        {
            BloomBook book;
            if (audioRecordingMode == TalkingBookApi.AudioRecordingMode.Sentence)
            {
                book = SetupBook(
                    "\r\n  <p><span id='e993d14a-0ec3-4316-840b-ac9143d59a2e' class='audio-sentence'>This is some text.</span> <span id='i0d8e9910-dfa3-4376-9373-a869e109b765' class='audio-sentence'>Another sentence</span></p>  \r\n",
                    "xyz"
                );
            }
            else if (audioRecordingMode == TalkingBookApi.AudioRecordingMode.TextBox)
            {
                book = SetupBookLong(
                    text: "<p>This is some text.</p>",
                    lang: "xyz",
                    extraEditDivClasses: "audio-sentence' id='e993d14a-0ec3-4316-840b-ac9143d59a2e", // Injecting other attributes into the "class" field as well in order to create the ID attribute simultaneously
                    extraContentOutsideTranslationGroup: "<div class='bloom-translationGroup'><div lang='xyz' class='bloom-editable audio-sentence' id='i0d8e9910-dfa3-4376-9373-a869e109b765'><p>Another sentence</p></div></div>"
                );
            }
            else
            {
                book = null;
                Assert.Fail("Invalid test input");
            }
            MakeFakeAudio(
                book.FolderPath.CombineForPath("audio", "i0d8e9910-dfa3-4376-9373-a869e109b765.mp3")
            );
            var hasFullAudio = book.HasFullAudioCoverage();
            Assert.IsFalse(
                hasFullAudio,
                "HasFullAudioCoverage should return false when an audio file is missing."
            );
            MakeEpub(
                "output",
                $"HasFullAudioCoverage_ReturnsFalseForMissingAudioFile_{audioRecordingMode}",
                book
            );
            CheckFolderStructure();
            CheckBasicsInManifest();
            CheckAccessibilityInManifest(true, false, false, _defaultSourceValue, hasFullAudio); // sound files, but no image files
        }

        [TestCase(TalkingBookApi.AudioRecordingMode.Sentence)]
        [TestCase(TalkingBookApi.AudioRecordingMode.TextBox)]
        public void HasFullAudioCoverage_IgnoresOtherLanguage(
            TalkingBookApi.AudioRecordingMode audioRecordingMode
        )
        {
            string extraContentDe =
                @"
					<div class='bloom-editable' lang='de' contenteditable='true'>German should never display in this collection, and audio shouldn't be required either!</div>";

            BloomBook book;
            if (audioRecordingMode == TalkingBookApi.AudioRecordingMode.Sentence)
            {
                book = SetupBookLong(
                    "\r\n  <p><span id='e993d14a-0ec3-4316-840b-ac9143d59a2f' class='audio-sentence'>This is some text.</span> <span id='i0d8e9910-dfa3-4376-9373-a869e109b764' class='audio-sentence'>Another sentence</span></p>  \r\n",
                    "xyz",
                    extraContent: extraContentDe
                );
            }
            else if (audioRecordingMode == TalkingBookApi.AudioRecordingMode.TextBox)
            {
                book = SetupBookLong(
                    text: "<p>This is some text.</p>",
                    lang: "xyz",
                    extraContent: extraContentDe,
                    extraEditDivClasses: "audio-sentence' id='e993d14a-0ec3-4316-840b-ac9143d59a2f", // Injecting other attributes into the "class" field as well in order to create the ID attribute simultaneously
                    extraContentOutsideTranslationGroup: "<div class='bloom-translationGroup'><div lang='xyz' class='bloom-editable audio-sentence' id='i0d8e9910-dfa3-4376-9373-a869e109b764'><p>Another sentence</p></div></div>"
                );
            }
            else
            {
                book = null;
                Assert.Fail("Invalid test input");
            }
            MakeFakeAudio(
                book.FolderPath.CombineForPath("audio", "e993d14a-0ec3-4316-840b-ac9143d59a2f.mp3")
            );
            MakeFakeAudio(
                book.FolderPath.CombineForPath("audio", "i0d8e9910-dfa3-4376-9373-a869e109b764.mp3")
            );
            var hasFullAudio = book.HasFullAudioCoverage();
            Assert.IsTrue(
                hasFullAudio,
                "HasFullAudioCoverage should return true, ignoring text in other languages"
            );
            MakeEpub(
                "output",
                $"HasFullAudioCoverage_IgnoresOtherLanguage_{audioRecordingMode}",
                book
            );
            CheckFolderStructure();
            CheckBasicsInManifest();
            CheckAccessibilityInManifest(true, false, false, _defaultSourceValue, hasFullAudio); // sound files, but no image files
        }

        [TestCase("span")]
        [TestCase("div")]
        public void BogusCkeMaterial_Removed(string elementName)
        {
            var book = SetupBookLong(
                $@"<div class='split-pane-component-inner'>
  <div data-initialrect='0.0449438202247191 0.1414790996784566 0.6825842696629213 0.6816720257234726' style='' data-finalrect='0.3455056179775281 0.21221864951768488 0.49719101123595505 0.4983922829581994' class='bloom-imageContainer bloom-leadingElement'>
  <img data-license='cc-by-nc-nd' data-creator='Sue Newland' data-copyright='Copyright © 2018, Sue Newland' src='DSC08193.png' alt='An eagle taking off.' height='238' width='357' id='bookfig1' aria-describedby='figdesc1' />
  </div>
  <aside class='imageDescription' id='figdesc1'>
    <p><{elementName} data-duration='3.900227' id='i92ebf7a6-0786-4480-89b2-dcefb56d7782' class='audio-sentence'>An eagle taking off.</{elementName}></p>
    <div data-cke-hidden-sel='1' data-cke-temp='1' style='position:fixed;top:0;left:-1000px'>
      <br />
    </div>
  </aside>
</div>",
                "xyz"
            );
            MakeFakeAudio(
                book.FolderPath.CombineForPath("audio", "i92ebf7a6-0786-4480-89b2-dcefb56d7782.mp3")
            );
            MakeImageFiles(book, "DSC08193");
            var hasFullAudio = book.HasFullAudioCoverage();
            Assert.IsTrue(hasFullAudio);
            MakeEpub("output", $"BogusCkeMaterial_Removed_{elementName}", book);
            CheckFolderStructure();
            CheckBasicsInManifest();
            CheckAccessibilityInManifest(true, true, false, _defaultSourceValue, hasFullAudio);
            var page2Data = GetPageNData(2);
            AssertThatXmlIn.String(page2Data).HasNoMatchForXpath("//*[@data-cke-hidden-sel]");
            AssertThatXmlIn
                .String(page2Data)
                .HasSpecifiedNumberOfMatchesForXpath($"//aside/p/{elementName}", 1);
            AssertThatXmlIn.String(page2Data).HasNoMatchForXpath("//aside/div");
        }
    }

    class EpubMakerAdjusted : EpubMaker
    {
        // The minimal bookServer created by CreateBookServer fails to copy mock books because
        // GetPathHtmlFile() returns an empty string because we don't make a real book file with mocked books.
        // So all the tests which don't create an actual file (createPhysicalFile = false)
        // are currently passing null for the bookserver, which disables the device xmatter code.
        public EpubMakerAdjusted(BloomBook book, BookThumbNailer thumbNailer, BookServer bookServer)
            : base(thumbNailer, string.IsNullOrEmpty(book.GetPathHtmlFile()) ? null : bookServer)
        {
            Book = book;
        }

        internal override void CopyFile(
            string srcPath,
            string dstPath,
            ImagePublishSettings imagePublishSettings,
            bool reduceImageIfPossible = false,
            bool makeTransparentifAppropriate = false
        )
        {
            if (srcPath.Contains("notareallocation"))
            {
                File.WriteAllText(dstPath, "This is a test fake");
                return;
            }
            base.CopyFile(
                srcPath,
                dstPath,
                imagePublishSettings,
                reduceImageIfPossible,
                makeTransparentifAppropriate
            );
        }
    }
}
