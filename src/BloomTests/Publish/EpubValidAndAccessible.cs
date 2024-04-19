using System;
using System.IO;
using System.Xml;
using Bloom.Book;
using Bloom.Publish.Epub;
using NUnit.Framework;
using SIL.Extensions;

namespace BloomTests.Publish
{
    [TestFixture]
    // This class implements what is conceptually one or two tests in the ExportEpubTests group.
    // But as there are many different outcomes to verify, and a much more complicated book to
    // create, it's cleaner to make a distinct class, and do the complete book creation setup in
    // OneTimeSetup.
    public class EpubValidAndAccessible : ExportEpubTestsBaseClass
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
		<p>Image on page 1 by International Illustrations; The Art Of Reading 3.0, © 2009 SIL International. CC-BY-SA 4.0.</p>
	</div>
	<div data-book='copyright' lang='*'>Copyright © 2020, Dr. Steve Himself</div>
	<div data-book='licenseUrl' lang='*'>http://creativecommons.org/licenses/by/4.0/</div>
	<div data-book='licenseDescription' lang='xyz'>
		http://creativecommons.org/licenses/by/4.0/<br />You are free to make commercial use of this work. You may adapt and add to this work. You must keep the copyright and credits for authors, illustrators, etc.
	</div>
	<div data-book='versionAcknowledgments' lang='en'>Dr. Stephen R. McConnel, Ph.D.</div>
	<div data-book='originalLicenseUrl' lang='*'>http://creativecommons.org/licenses/by/4.0/</div>
	<div data-book='originalCopyright' lang='*'>Copyright © 2017, Dr. Timothy I. McConnel, Ph.D.</div>
	<div data-book='originalAcknowledgments' lang='en'>Dr. Timothy I. McConnel, Ph.D.</div>
	<div data-book='funding' lang='xyz'><p>We gratefully acknowledge those who help fund the author in his work.  They know who they are.</p></div>
	<div data-book='outside-back-cover-branding-top-html'></div>
	<div data-book='outside-back-cover-branding-bottom-html' lang='*'>
		<p>This book was created with Bloom Enterprise features freely enabled in order to support projects funded by the local community.</p><img class='branding' src='Bloom%20Against%20Light%20HD.png' alt=''></img>
	</div>
</div>
",
                extraContentOutsideTranslationGroup: @"
<div class=""bloom-imageContainer"">
	<img data-book=""coverImage"" src=""DevilsSlide.png"" data-copyright=""Copyright © 2015, Stephen McConnel"" data-creator=""Stephen McConnel"" data-license=""cc-by-sa""></img>
	<div class=""bloom-translationGroup bloom-imageDescription bloom-trailingElement normal-style"" data-default-languages=""auto"" data-book=""coverImageDescription"">
		<div data-languagetipcontent=""English"" aria-label=""false"" role=""textbox"" spellcheck=""true"" tabindex=""0"" style=""min-height: 64.01px;"" class=""bloom-editable cke_editable cke_editable_inline cke_contents_ltr thisOverflowingParent bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""xyz"" contenteditable=""true"">
			<p>Photograph of a rock formation with two parallel spines coming down a hill</p>
		</div>
	</div>
</div>
",
                extraPages: @"
<div class='bloom-page numberedPage' data-page-number='1'>
	<div class=""marginBox"">
		<div style=""min-height: 42px;"" class=""split-pane horizontal-percent"">
			<div class=""split-pane-component position-top"" style=""bottom: 50%;"">
				<div class=""split-pane-component-inner"">
					<div title=""SteveAtMalad.png 91.92 KB 1024 x 768 489 DPI (should be 300-600) Bit Depth: 24"" class=""bloom-imageContainer bloom-leadingElement"">
						<img style="""" data-license=""cc-by-sa"" data-creator=""Stephen McConnel"" data-copyright=""Copyright © 2012, Stephen McConnel"" src=""SteveAtMalad.png"" alt=""This picture, SteveAtMalad.png, is missing or was loading too slowly."" height=""481"" width=""642""></img>
						<div class=""bloom-translationGroup bloom-imageDescription bloom-trailingElement normal-style"">
							<div aria-label=""false"" role=""textbox"" spellcheck=""true"" tabindex=""0"" class=""bloom-editable cke_editable cke_editable_inline cke_contents_ltr cke_focus normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""xyz"" contenteditable=""true"">
								<p>photograph of a man in a cowboy hat standing on a bridge over a narrow river gorge with a highway bridge behind him</p>
							</div>
						</div>
					</div>
				</div>
			</div>
			<div class=""split-pane-divider horizontal-divider"" style=""bottom: 50%;""></div>
			<div class=""split-pane-component position-bottom"" style=""height: 50%;"">
				<div class=""split-pane-component-inner"">
					<div class=""bloom-translationGroup bloom-trailingElement normal-style"" data-default-languages=""auto"">
						<div aria-describedby=""qtip-1"" data-languagetipcontent=""English"" data-hasqtip=""true"" aria-label=""false"" role=""textbox"" spellcheck=""true"" tabindex=""0"" style=""min-height: 64px;"" class=""bloom-editable cke_editable cke_editable_inline cke_contents_ltr normal-style cke_focus overflow bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""xyz"" contenteditable=""true"">
							<p>This is a <i><b>me</b></i> at Malad Gorge State Park in Idaho.</p>
							<p>Notice how deep this river gorge is beneath the bridge!</p>
						</div>
					</div>
				</div>
			</div>
		</div>
	</div>
</div>
<div class='bloom-page numberedPage' data-page-number='2' data-page=""required singleton"">
	<div class=""marginBox"">
		<div class=""bloom-translationGroup"" data-default-languages=""N1"" id=""originalContributions"">
			<div class=""bloom-editable credits bloom-copyFromOtherLanguageIfNecessary Content-On-Title-Page-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"">
				<p>This element would normally be on the title page but put here where contentinfo should be added</p>
			</div>
		</div>
		<div class=""copyright Credits-Page-style"" data-derived=""copyright"" lang=""*"">
			Copyright © 2018, Stephen R. McConnel...this would not normally be on a content page
		</div>	
	</div>
</div>
<div class=""bloom-page titlePage bloom-backMatter A5Portrait layout-style-Default side-left"" data-page=""required singleton"" id=""60ae9f18-b7b8-405b-8dd0-2cb5926eacde"" data-page-number=""10"">
	<div class=""marginBox"">
		<div class=""bloom-translationGroup"" data-default-languages=""V,N1"" id=""titlePageTitleBlock"">
			<div class=""bloom-editable bloom-nodefaultstylerule Title-On-Title-Page-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""xyz"" contenteditable=""true"" data-book=""bookTitle"">
				<p>New Book</p>
			</div>
		</div>
		<div class=""bloom-translationGroup"" data-default-languages=""N1"" id=""originalContributions"">
			<div class=""bloom-editable credits bloom-copyFromOtherLanguageIfNecessary Content-On-Title-Page-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" data-book=""originalContributions"">
				<p>words <em>carefully</em> chosen by Stephen McConnel</p>
				<p>Image on page Front Cover by Stephen McConnel, © 2012 Stephen McConnel. CC-BY-SA 4.0.</p>
				<p>Image on page 1 by International Illustrations; The Art Of Reading 3.0, © 2009 SIL International. CC-BY-SA 4.0.</p>
			</div>
		</div>
		<div class=""bloom-translationGroup"" data-default-languages=""N1"" id=""funding"">
			<div class=""bloom-editable funding Content-On-Title-Page-style bloom-copyFromOtherLanguageIfNecessary bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" data-book=""funding"">
				<p>We gratefully acknowledge those who help fund the author in his work.  They know who they are.</p>
			</div>
		</div>
	</div>
	<div class=""marginBox""><img class=""branding"" type=""image/svg"" onerror=""this.style.display='none'""></img></div>
</div>
<div class=""bloom-page bloom-backMatter credits A5Portrait layout-style-Default side-right"" data-page=""required singleton"" id=""e56cb792-855a-4be9-8362-8a8d8bea3468"" data-page-number=""1"">
	<div class=""marginBox"">
		<div class=""bloom-metaData licenseAndCopyrightBlock"" data-functiononhintclick=""bookMetadataEditor"" data-hint=""Click to Edit Copyright &amp; License"" lang=""xyz"">
			<div class=""copyright Credits-Page-style"" data-derived=""copyright"" lang=""*"">
				Copyright © 2018, Stephen R. McConnel
			</div>
			<div class=""licenseBlock"">
				<div class=""licenseDescription Credits-Page-style"" data-derived=""licenseDescription"" lang=""xyz"">
					http://creativecommons.org/licenses/by/4.0/<br/>You are free to make commercial use of this work. You may adapt and add to this work. You must keep the copyright and credits for authors, illustrators, etc.
				</div>
			</div>
		</div>
		<div class=""bloom-translationGroup versionAcknowledgments"" data-default-languages=""N1"">
			<div class=""bloom-editable versionAcknowledgments Credits-Page-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" data-book=""versionAcknowledgments"">
				Dr. Stephen R. McConnel, Ph.D.
			</div>
		</div>
		<div class=""copyright Credits-Page-style"" data-derived=""originalCopyrightAndLicense"">
			<div class=""copyright Credits-Page-style"" data-derived=""copyright"" lang=""*"">
				Copyright © 2017, Timothy I. McConnel
			</div>
			<div class=""licenseBlock"">
				<div class=""licenseUrl"" data-derived=""licenseUrl"" lang=""*"">
					http://creativecommons.org/licenses/by/4.0/
				</div>
			</div>
		</div>
		<div class=""bloom-translationGroup originalAcknowledgments"" data-default-languages=""N1"">
			<div class=""bloom-editable bloom-copyFromOtherLanguageIfNecessary Credits-Page-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" data-book=""originalAcknowledgments"">
				<div class=""bloom-editable originalAcknowledgments Credits-Page-style bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" data-book=""originalAcknowledgments"">
					Dr. Timothy I. McConnel, Ph.D.
				</div>
			</div>
		</div>
	</div>
	<div class=""marginBox""><img class=""branding"" src="""" type=""image/svg"" onerror=""this.style.display='none'""></img></div>
</div>
<div class=""bloom-page bloom-backMatter theEndPage A5Portrait layout-style-Default side-right"" data-page=""required singleton"" id=""2f5d5775-a8c4-49fd-8a92-601b64b4846f"" data-page-number=""10"">
	<div class=""pageLabel"" lang=""xyz"" data-i18n=""TemplateBooks.PageLabel.The End"">
		The End
	</div>
	<div class=""pageDescription"" lang=""xyz""></div>
	<div class=""marginBox""><img class=""branding"" src=""back-cover-outside.svg"" type=""image/svg"" onerror=""this.style.display='none'""></img></div>
</div>
"
            );
            MakeImageFiles(book, "DevilsSlide", "SteveAtMalad", "Bloom Against Light HD.png");
            // The end page is rebuilt using the xmatter and branding settings, so the xhtml referencing back-cover-outside.svg is thrown away.
            // However, the title page with the given branding references BloomLocal.svg so we'll create that and test it.
            MakeSampleSvgImage(book.FolderPath.CombineForPath("BloomLocal.svg"));
            // Add some accessibility stuff from the ePUB metadata dialog
            var metadata = book.BookInfo.MetaData;
            metadata.Hazards = "flashingHazard,noMotionSimulationHazard";
            metadata.A11yFeatures = "signLanguage";
            // Without a branding, Bloom Enterprise-only features are removed
            string branding = "Local-Community";
            // Currently, only in OnPage mode does the image description turn into an aside that can be linked to the image.
            // May need to try more than once on Linux to make the epub without an exception for failing to complete loading the document.
            MakeEpubWithRetries(
                kMakeEpubTrials,
                "output",
                "ExportEpubWithSvgTests",
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
        }

        [Test]
        public void CheckFrontCoverAccessibility()
        {
            // Verify the ARIA role and labels for the front cover page.
            AssertThatXmlIn
                .String(_page1Data)
                .HasSpecifiedNumberOfMatchesForXpath("//xhtml:div[@role='contentinfo']", _ns, 1);
            AssertThatXmlIn
                .String(_page1Data)
                .HasSpecifiedNumberOfMatchesForXpath("//xhtml:*[@aria-label]", _ns, 2);
            AssertThatXmlIn
                .String(_page1Data)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:div[@aria-label='Front Cover']",
                    _ns,
                    1
                );
            AssertThatXmlIn
                .String(_page1Data)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:div[@role='contentinfo' and @aria-label='Front Cover']",
                    _ns,
                    1
                );

            // Check the page break references.
            AssertThatXmlIn
                .String(_page1Data)
                .HasSpecifiedNumberOfMatchesForXpath("//xhtml:span[@role='doc-pagebreak']", _ns, 1);
            AssertThatXmlIn
                .String(_page1Data)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:span[@role='doc-pagebreak' and @aria-label='Front Cover']",
                    _ns,
                    1
                );

            XmatterPageHasContentInfoNotMain(_page1Data);
            ImageDescriptionIsMarkedForAccessibility(_page1Data, "1");
        }

        [Test]
        public void CheckContentPageAccessibility()
        {
            var pageData = GetPageNData(2);
            // Verify the ARIA role and labels for the content page.
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath("//xhtml:*[@role='main']", _ns, 1);
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath("//xhtml:*[@aria-label]", _ns, 2);
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:div[@role='main' and @aria-label='Main Content']",
                    _ns,
                    1
                );

            // Check the page break references.
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath("//xhtml:span[@role='doc-pagebreak']", _ns, 1);
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:span[@role='doc-pagebreak' and @aria-label='1']",
                    _ns,
                    1
                );

            // Verify that the content page does not have a "contentinfo" role.
            AssertThatXmlIn
                .String(pageData)
                .HasNoMatchForXpath("//xhtml:*[@role='contentinfo']", _ns);

            ImageDescriptionIsMarkedForAccessibility(pageData, "2");
        }

        [Test]
        public void CheckTitlePageAccessibility()
        {
            var pageData = GetPageNData(4);
            // Verify the ARIA roles and labels for the Bloom title page.
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath("//xhtml:*[@role='contentinfo']", _ns, 1);
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath("//xhtml:*[@aria-label]", _ns, 4);
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:div[@role='contentinfo' and @aria-label='Title Page']",
                    _ns,
                    1
                );

            // Check our standard subsections of the title page as well.
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:div[@aria-label='Original Contributions']",
                    _ns,
                    1
                );
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath("//xhtml:div[ @aria-label='Funding']", _ns, 1);

            // Check the page break references.
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath("//xhtml:span[@role='doc-pagebreak']", _ns, 1);
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:span[@role='doc-pagebreak' and @aria-label='Title Page']",
                    _ns,
                    1
                );

            XmatterPageHasContentInfoNotMain(pageData);
            EpubBackmatterPageHasNoDescribableImage(pageData);
        }

        [Test]
        public void CheckContentInfoOnChildElementsOutsideContentInfoPages()
        {
            // These elements would not normally be on a numbered page, and a numbered page would not normally be data-page="requiredSingleton".
            // I set up some unusual data to test that these elements, in the right circumstances, get given their own contentinfo and label.
            // The contentinfo is NOT added when, as usual and as tested elsewhere, they are on a page like title page that is ALL contentinfo.
            var pageData = GetPageNData(3);
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:div[@role='contentinfo' and @aria-label='Original Contributions']",
                    _ns,
                    1
                );
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:div[@role='contentinfo' and @aria-label='Copyright']",
                    _ns,
                    1
                );
        }

        [Test]
        public void CheckCreditsPageAccessibility()
        {
            var pageData = GetPageNData(5);
            // Verify the ARIA roles and labels for the Bloom credits page.
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath("//xhtml:*[@role='contentinfo']", _ns, 1);
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath("//xhtml:*[@aria-label]", _ns, 6);
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:div[@role='contentinfo' and @aria-label='Credits Page']",
                    _ns,
                    1
                );

            // Check our standard subsections of the credits page as well.
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:div[@aria-label='Copyright']",
                    _ns,
                    1
                );
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:div[@aria-label='Version Acknowledgments']",
                    _ns,
                    1
                );
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:div[@aria-label='Original Copyright']",
                    _ns,
                    1
                );
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:div[@aria-label='Original Acknowledgments']",
                    _ns,
                    1
                );

            // Check the page break references.
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath("//xhtml:span[@role='doc-pagebreak']", _ns, 1);
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:span[@role='doc-pagebreak' and @aria-label='Credits Page']",
                    _ns,
                    1
                );

            XmatterPageHasContentInfoNotMain(pageData);
            EpubBackmatterPageHasNoDescribableImage(pageData);
        }

        [Test]
        public void CheckEndPageAccessibility()
        {
            var pageData = GetPageNData(6);
            // Verify the ARIA roles and labels for the End Page.
            // currently one thing gets role attr, a doc-pageBreak.
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath("//xhtml:*[@role]", _ns, 1);
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath("//xhtml:*[@aria-label]", _ns, 1);

            // Check the page break references.
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath("//xhtml:span[@role='doc-pagebreak']", _ns, 1);
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:span[@role='doc-pagebreak' and @aria-label='Outside Back Cover']",
                    _ns,
                    1
                );
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:span[@role='doc-pagebreak' and @aria-label='The End']",
                    _ns,
                    0
                );

            EpubBackmatterPageHasNoDescribableImage(pageData);
        }

        [Test]
        public void CheckEpubSvgValidity()
        {
            var svgData = GetFileData(EpubMaker.kImagesFolder + "/BloomLocal.svg");
            var ns = new XmlNamespaceManager(new NameTable());
            ns.AddNamespace("rdf", "http://www.w3.org/1999/02/22-rdf-syntax-ns#");
            ns.AddNamespace("sodipodi", "http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd");
            ns.AddNamespace("inkscape", "http://www.inkscape.org/namespaces/inkscape");

            AssertThatXmlIn
                .String(_originalSvgContent)
                .HasAtLeastOneMatchForXpath("//@inkscape:version", ns);
            AssertThatXmlIn.String(svgData).HasNoMatchForXpath("//@inkscape:version", ns);

            AssertThatXmlIn
                .String(_originalSvgContent)
                .HasAtLeastOneMatchForXpath("//sodipodi:namedview", ns);
            AssertThatXmlIn.String(svgData).HasNoMatchForXpath("//sodipodi:namedview", ns);

            AssertThatXmlIn.String(_originalSvgContent).HasAtLeastOneMatchForXpath("//rdf:RDF", ns);
            AssertThatXmlIn.String(svgData).HasNoMatchForXpath("//rdf:RDF", ns);

            AssertThatXmlIn.String(_originalSvgContent).HasAtLeastOneMatchForXpath("//flowRoot");
            AssertThatXmlIn.String(svgData).HasNoMatchForXpath("//flowRoot");

            Assert.AreEqual(
                -1,
                svgData.IndexOf("inkscape:", StringComparison.InvariantCulture),
                "No remnants of inkscape elements or attributes should remain in the svg file"
            );
            Assert.AreEqual(
                -1,
                svgData.IndexOf("rdf:", StringComparison.InvariantCulture),
                "No remnants of rdf elements or attributes should remain in the svg file"
            );
            Assert.AreEqual(
                -1,
                svgData.IndexOf("sodipodi:", StringComparison.InvariantCulture),
                "No remnants of sodipodi elements or attributes should remain in the svg file"
            );
        }

        [Test]
        public void CheckEpubManifestPropertiesValidity()
        {
            AssertThatXmlIn
                .String(_manifestContent)
                .HasAtLeastOneMatchForXpath(
                    "package/manifest/item[@id='f4' and @properties='svg']"
                );
            AssertThatXmlIn
                .String(_manifestContent)
                .HasSpecifiedNumberOfMatchesForXpath("package/manifest/item[@properties='svg']", 1);

            AssertThatXmlIn
                .String(_manifestContent)
                .HasAtLeastOneMatchForXpath(
                    "package/manifest/item[@id='epub-thumbnail' and @properties='cover-image']"
                );
            AssertThatXmlIn
                .String(_manifestContent)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "package/manifest/item[@properties='cover-image']",
                    1
                );

            AssertThatXmlIn
                .String(_manifestContent)
                .HasAtLeastOneMatchForXpath(
                    "package/manifest/item[@id='nav' and @properties='nav']"
                );
            AssertThatXmlIn
                .String(_manifestContent)
                .HasSpecifiedNumberOfMatchesForXpath("package/manifest/item[@properties='nav']", 1);
        }

        [Test]
        public void CheckEpubPagesValidity()
        {
            CheckPageForEpubValidity(1);
            CheckPageForEpubValidity(2);
            CheckPageForEpubValidity(3);
            CheckPageForEpubValidity(4);
            CheckPageForEpubValidity(5);
        }

        private void CheckPageForEpubValidity(int n)
        {
            var pageData = GetPageNData(n);
            AssertThatXmlIn.String(pageData).HasNoMatchForXpath("//img[@type]");
            AssertThatXmlIn.String(pageData).HasNoMatchForXpath("//img[@src='']");
            AssertThatXmlIn.String(pageData).HasNoMatchForXpath("//img[not(@src)]");
            AssertThatXmlIn
                .String(pageData)
                .HasNoMatchForXpath(
                    "//div[contains(@class, 'split-pane-component-inner') and @min-height]"
                );
            AssertThatXmlIn
                .String(pageData)
                .HasNoMatchForXpath(
                    "//div[contains(@class, 'split-pane-component-inner') and @min-width]"
                );
        }

        /// <summary>
        /// Verify that an image has a description linked to it properly.
        /// If this method needs to handle multiple descriptions for the same image someday, it'll need reworking.
        /// </summary>
        private void ImageDescriptionIsMarkedForAccessibility(string pageData, string figureNumber)
        {
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath("//xhtml:img[@aria-describedby]", _ns, 1);
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:img[@aria-describedby='figdesc"
                        + figureNumber
                        + ".0' and @id='bookfig"
                        + figureNumber
                        + "']",
                    _ns,
                    1
                );
            AssertThatXmlIn
                .String(pageData)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//xhtml:aside[@id='figdesc" + figureNumber + ".0']",
                    _ns,
                    1
                );
        }

        /// <summary>
        /// Verify that an ePUB backmatter page does not have an image description set up.
        /// </summary>
        private void EpubBackmatterPageHasNoDescribableImage(string pageData)
        {
            AssertThatXmlIn
                .String(pageData)
                .HasNoMatchForXpath("//xhtml:img[@aria-describedby]", _ns);
        }

        /// <summary>
        /// Verify that an xmatter page has a "contentinfo" role, but no "main" role.
        /// </summary>
        private void XmatterPageHasContentInfoNotMain(string pageData)
        {
            AssertThatXmlIn.String(pageData).HasNoMatchForXpath("//xhtml:*[@role='main']", _ns);
            AssertThatXmlIn
                .String(pageData)
                .HasAtLeastOneMatchForXpath("//xhtml:div[@role='contentinfo']", _ns);
        }

        const string _originalSvgContent =
            @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""no""?>
<!-- Created with Inkscape (http://www.inkscape.org/) -->

<svg
   xmlns:dc=""http://purl.org/dc/elements/1.1/""
   xmlns:cc=""http://creativecommons.org/ns#""
   xmlns:rdf=""http://www.w3.org/1999/02/22-rdf-syntax-ns#""
   xmlns:svg=""http://www.w3.org/2000/svg""
   xmlns=""http://www.w3.org/2000/svg""
   xmlns:sodipodi=""http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd""
   xmlns:inkscape=""http://www.inkscape.org/namespaces/inkscape""
   version=""1.1""
   width=""0.9144159cm""
   height=""1.2380072cm""
   id=""svg2""
   xml:space=""preserve""
   inkscape:version=""0.92.1 r15371""
   sodipodi:docname=""title-page.svg""
   inkscape:export-filename=""X:\dev\bloom2\src\BloomExe\Resources\SIL_Logo_2014Small1.png""
   inkscape:export-xdpi=""19.940001""
   inkscape:export-ydpi=""19.940001""><sodipodi:namedview
     pagecolor=""#ffffff""
     bordercolor=""#666666""
     borderopacity=""1""
     objecttolerance=""10""
     gridtolerance=""10""
     guidetolerance=""10""
     inkscape:pageopacity=""0""
     inkscape:pageshadow=""2""
     inkscape:window-width=""1920""
     inkscape:window-height=""1147""
     id=""namedview22""
     showgrid=""false""
     inkscape:zoom=""2.6224042""
     inkscape:cx=""-138.99445""
     inkscape:cy=""-10.254716""
     inkscape:window-x=""-8""
     inkscape:window-y=""-8""
     inkscape:window-maximized=""1""
     inkscape:current-layer=""g24""
     units=""cm""
     fit-margin-top=""0""
     fit-margin-left=""0""
     fit-margin-right=""0""
     fit-margin-bottom=""0"" /><metadata
     id=""metadata8""><rdf:RDF><cc:Work
         rdf:about=""""><dc:format>image/svg+xml</dc:format><dc:type
           rdf:resource=""http://purl.org/dc/dcmitype/StillImage"" /><dc:title></dc:title></cc:Work></rdf:RDF></metadata><defs
     id=""defs6""><clipPath
       id=""clipPath18""><path
         d=""M 0,2879.78 V 0 h 2563.7 v 2879.78 z""
         inkscape:connector-curvature=""0""
         id=""path20"" /></clipPath><clipPath
       id=""clipPath28""><path
         d=""M 0.00416667,3071.7653 H 2734.6133 V 0 H 0.00416667 Z""
         inkscape:connector-curvature=""0""
         id=""path30""
         style=""stroke-width:1.06666672"" /></clipPath></defs><g
     transform=""matrix(1.25,0,0,-1.25,-120.67921,52.788781)""
     id=""g10""><g
       id=""g24""
       transform=""scale(0.1)""><path
         id=""path4578""
         inkscape:connector-curvature=""0""
         inkscape:export-filename=""X:\dev\b36\src\BloomExe\Resources\LogoForSplashScreen.png""
         inkscape:export-xdpi=""108.54""
         inkscape:export-ydpi=""108.54""
         sodipodi:nodetypes=""cccsccc""
         class=""st1""
         d=""m 1121.4986,242.03009 c 1.6386,91.77992 -31.6859,113.0854 -51.3525,127.8351 -17.4818,12.5647 -52.4455,26.2235 -77.57543,31.1396 -18.9981,-105.6795 7.1329,-235.39503 112.66143,-230.53921 121.2455,5.57768 119.696,97.63239 112.4156,227.80721 -66.1023,-6.0079 -86.3161,-25.6756 -86.3161,-25.6756 l -3.8239,-2.732""
         style=""opacity:1;fill:none;stroke:#d65649;stroke-width:42.61167145;stroke-linejoin:round;stroke-opacity:1"" /><flowRoot
         xml:space=""preserve""
         id=""flowRoot4510""
         style=""font-style:normal;font-weight:normal;font-size:11.25px;line-height:125%;font-family:Sans;letter-spacing:0px;word-spacing:0px;fill:#000000;fill-opacity:1;stroke:none;stroke-width:1px;stroke-linecap:butt;stroke-linejoin:miter;stroke-opacity:1""
         transform=""matrix(8,0,0,-8,911.12736,467.60113)""><flowRegion
           id=""flowRegion4512""><rect
             id=""rect4514""
             width=""52.623466""
             height=""11.821214""
             x=""8.7705774""
             y=""45.620865"" /></flowRegion><flowPara
           id=""flowPara4516"">Locd</flowPara></flowRoot><text
         xml:space=""preserve""
         style=""font-style:italic;font-variant:normal;font-weight:normal;font-stretch:normal;font-size:115.67372894px;line-height:125%;font-family:'Andika New Basic';-inkscape-font-specification:'Andika New Basic, Italic';font-variant-ligatures:normal;font-variant-caps:normal;font-variant-numeric:normal;font-feature-settings:normal;text-align:start;letter-spacing:0px;word-spacing:0px;writing-mode:lr-tb;text-anchor:start;fill:#000000;fill-opacity:1;stroke:none;stroke-width:10.28210926px;stroke-linecap:butt;stroke-linejoin:miter;stroke-opacity:1""
         x=""960.58069""
         y=""-49.395706""
         id=""text4520""
         transform=""scale(1,-1)""><tspan
           sodipodi:role=""line""
           id=""tspan4518""
           x=""960.58069""
           y=""-49.395706""
           style=""font-style:italic;font-variant:normal;font-weight:normal;font-stretch:normal;font-size:115.67372894px;font-family:'Andika New Basic';-inkscape-font-specification:'Andika New Basic, Italic';font-variant-ligatures:normal;font-variant-caps:normal;font-variant-numeric:normal;font-feature-settings:normal;text-align:start;writing-mode:lr-tb;text-anchor:start;stroke-width:10.28210926px"">Local</tspan></text>
</g></g></svg>";

        protected void MakeSampleSvgImage(string path)
        {
            File.WriteAllText(path, _originalSvgContent);
        }

        [Test]
        public void CheckEpubFolderStructure()
        {
            CheckFolderStructure();
        }
    }
}
