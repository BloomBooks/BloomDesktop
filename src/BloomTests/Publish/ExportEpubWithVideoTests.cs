using System;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom.Book;
using NUnit.Framework;
using SIL.Xml;

namespace BloomTests.Publish
{
	/// <summary>
	/// This part of the class implements several tests in the ExportEpubTests group on a single,
	/// more complicated book that tests having video.  The code tries to minimize how often the
	/// book gets created.
	/// </summary>
	/// <remarks>
	/// This started as a separate subclass, which explains the base class.  The CreateEpubWithVideo
	/// method was essentially the subclass's override of the OneTimeSetup method.  However, this
	/// implementation was subject to sporadic (actually more than sporadic) failures on Linux
	/// with the message
	/// System.ApplicationException: Failure to completely load visibility document in RemoveUnwantedContent
	/// in the MakeEpub method call.  Presumably this was due to parallel calls into the browser
	/// due to the architecture of the NUnit test runner.  Making all this into a single class
	/// reduced the number of times this error happened in 10 runs of the three classes (or three
	/// parts of the same class) from 7 to 0.  Trying a lock around the content of the OneTimeSetup
	/// method reduced the count to 3 failures in 10 tries.  Trying a lock around the content of the
	/// MakeEpub method resulted in 5 failures in 10 tries.
	/// </remarks>
	[TestFixture]
	public partial class ExportEpubTests : ExportEpubTestsBaseClass
	{
		bool _epubWithVideoExists;

		public void CreateEpubWithVideo()
		{
			if (_epubWithVideoExists)
				return;
			var book = SetupBookLong("This is some text", "xyz", " bloom-frontMatter frontCover' data-page='required singleton",
				extraContentOutsideTranslationGroup: @"
<div class='bloom-imageContainer'>
	<img data-book='coverImage' src='DevilsSlide.png' data-copyright='Copyright © 2015, Stephen McConnel' data-creator='Stephen McConnel' data-license='cc-by-sa'></img>
	<div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement normal-style' data-default-languages='auto' data-book='coverImageDescription'>
		<div data-languagetipcontent='Xyzzy' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 64.01px;' class='bloom-editable cke_editable cke_editable_inline cke_contents_ltr thisOverflowingParent bloom-content1 bloom-contentNational1 bloom-visibility-code-on' lang='xyz' contenteditable='true'>
			<p>Photograph of a rock formation with two parallel spines coming down a hill</p>
		</div>
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
");
			MakeImageFiles(book, "DevilsSlide");
			MakeVideoFiles(book, "a0c5c8dd-d84b-4bf6-9f53-c4bb5caf38d0", "importedvideo");
			// Without a branding, Bloom Enterprise-only features are removed
			var branding = "Test";
			MakeEpub("output", "ExportEpubWithVideo", book, BookInfo.HowToPublishImageDescriptions.OnPage, branding);
			_ns = GetNamespaceManager();
			_epubWithVideoExists = true;
		}

		[Test]
		public void CheckVideoEpubBasics()
		{
			CreateEpubWithVideo();
			CheckBasicsInPage("DevilsSlide");
			CheckBasicsInManifest();
			CheckAccessibilityInManifest(false, true, false, _defaultSourceValue, false); // no sound files, but some image files
			CheckFolderStructure();
		}

		[Test]
		public void CheckPageWithVideoAndText()
		{
			CreateEpubWithVideo();
			var pageData = GetPageNData(2);
			var doc = new XmlDocument();
			doc.LoadXml(pageData);
			var list = doc.SafeSelectNodes("//div[contains(@class,'bloom-videoContainer')]/video/source").Cast<XmlElement>();
			Assert.AreEqual(1, list.Count());
			Assert.AreEqual(kVideoSlash+"a0c5c8dd-d84b-4bf6-9f53-c4bb5caf38d0.mp4", list.First().GetAttribute("src"));
			list = doc.SafeSelectNodes("//div[contains(@class,'bloom-trailingElement')]").Cast<XmlElement>();
			Assert.AreEqual(1, list.Count());
			Assert.AreEqual("This video shows me counting to five on my fingers.", list.First().InnerText.Trim());
			CheckAccessibilityInManifest(false, true, true, _defaultSourceValue, false); // no sound files, but some image and video files
		}

		[Test]
		public void CheckPageWithTextButNoVideo()
		{
			CreateEpubWithVideo();
			var pageData = GetPageNData(3);
			var doc = new XmlDocument();
			doc.LoadXml(pageData);
			var list = doc.SafeSelectNodes("//div[contains(@class,'bloom-videoContainer')]/video/source").Cast<XmlElement>();
			Assert.AreEqual(0, list.Count());
			list = doc.SafeSelectNodes("//div[contains(@class,'bloom-videoContainer')]").Cast<XmlElement>();
			Assert.AreEqual(1, list.Count());
			Assert.Contains("bloom-noVideoSelected", list.First().GetAttribute("class").Split(' '));
			list = doc.SafeSelectNodes("//div[contains(@class,'bloom-trailingElement')]").Cast<XmlElement>();
			Assert.AreEqual(1, list.Count());
			Assert.AreEqual("This page has an empty spot for a video.  None has been selected yet.", list.First().InnerText.Trim());
		}

		[Test]
		public void CheckPageWithVideoButNoText()
		{
			CreateEpubWithVideo();
			var pageData = GetPageNData(4);
			var doc = new XmlDocument();
			doc.LoadXml(pageData);
			var list = doc.SafeSelectNodes("//div[contains(@class,'bloom-videoContainer')]/video/source").Cast<XmlElement>();
			Assert.AreEqual(1, list.Count());
			Assert.AreEqual(kVideoSlash+"importedvideo.mp4", list.First().GetAttribute("src"));
			list = doc.SafeSelectNodes("//div[contains(@class,'bloom-trailingElement')]").Cast<XmlElement>();
			Assert.AreEqual(1, list.Count());
			Assert.AreEqual("", list.First().InnerText.Trim());
			CheckAccessibilityInManifest(false, true, true, _defaultSourceValue, false); // no sound files, but some image and video files
		}

		[Test]
		public void CheckPageWithTextAndDeletedVideo()
		{
			CreateEpubWithVideo();
			var pageData = GetPageNData(5);
			var doc = new XmlDocument();
			doc.LoadXml(pageData);
			var list = doc.SafeSelectNodes("//div[contains(@class,'bloom-videoContainer')]/video/source").Cast<XmlElement>();
			Assert.AreEqual(1, list.Count());
			Assert.AreEqual(kVideoSlash+"deletedvideo.mp4", list.First().GetAttribute("src"));
			list = doc.SafeSelectNodes("//div[contains(@class,'bloom-trailingElement')]").Cast<XmlElement>();
			Assert.AreEqual(1, list.Count());
			Assert.AreEqual("This page had a video selected, but it is now missing.", list.First().InnerText.Trim());
		}

		[Test]
		public void CheckPageWithNeitherVideoNorText()
		{
			CreateEpubWithVideo();
			// This page should not exist.
			VerifyThatPageNDoesNotExist(6);
		}
	}
}
