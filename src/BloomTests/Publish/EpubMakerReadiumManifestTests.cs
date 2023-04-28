using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom;
using Bloom.Book;
using Bloom.Publish.Epub;
using BloomTemp;
using Gtk;
using Newtonsoft.Json;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Publish
{
	public class EpubMakerReadiumManifestTests
	{
		private const string opfData = @"<?xml version=""1.0"" encoding=""utf-8""?>
<package version=""3.0"" unique-identifier=""I7cd3b13a-402f-4d58-b57d-7ffc41008222"" prefix=""a11y: http://www.idpf.org/epub/vocab/package/a11y/# epub32: https://w3c.github.io/publ-epub-revision/epub32/spec/epub-packages.html# http://www.idpf.org/vocab/rendition/#"" xmlns=""http://www.idpf.org/2007/opf"">
  <opf:metadata xmlns:dc=""http://purl.org/dc/elements/1.1/"" xmlns:opf=""http://www.idpf.org/2007/opf"">
    <dc:title>comic 2</dc:title>
    <dc:language>akl</dc:language>
    <dc:identifier id=""I7cd3b13a-402f-4d58-b57d-7ffc41008222"">bloomlibrary.org.7cd3b13a-402f-4d58-b57d-7ffc41008222</dc:identifier>
    <dc:source>created from Bloom book on 2023-03-31 with page size A5 Portrait</dc:source>
    <dc:rights>http://creativecommons.org/licenses/by/4.0/</dc:rights>
    <opf:meta property=""dcterms:modified"">2023-03-31T13:28:39Z</opf:meta>
    <opf:meta property=""dcterms:dateCopyrighted"">2023</opf:meta>
    <opf:meta property=""dcterms:rightsHolder"">JohnT</opf:meta>
    <opf:meta property=""schema:numberOfPages"">4</opf:meta>
    <opf:meta property=""schema:accessMode"">textual</opf:meta>
    <opf:meta property=""schema:accessMode"">visual</opf:meta>
    <opf:meta property=""schema:accessMode"">auditory</opf:meta>
    <opf:meta property=""schema:accessModeSufficient"">textual</opf:meta>
    <opf:meta property=""schema:accessModeSufficient"">textual,visual</opf:meta>
    <opf:meta property=""schema:accessModeSufficient"">textual,auditory</opf:meta>
    <opf:meta property=""schema:accessModeSufficient"">textual,visual,auditory</opf:meta>
    <opf:meta property=""schema:accessibilityFeature"">synchronizedAudioText</opf:meta>
    <opf:meta property=""schema:accessibilityFeature"">displayTransformability</opf:meta>
    <opf:meta property=""schema:accessibilityFeature"">printPageNumbers</opf:meta>
    <opf:meta property=""schema:accessibilityFeature"">unlocked</opf:meta>
    <opf:meta property=""schema:accessibilityFeature"">readingOrder</opf:meta>
    <opf:meta property=""schema:accessibilityFeature"">tableOfContents</opf:meta>
    <opf:meta property=""schema:accessibilityHazard"">unknown</opf:meta>
    <opf:meta property=""schema:accessibilitySummary"">How well the accessibility features work is up to the individual author.</opf:meta>
    <meta name=""cover"" content=""epub-thumbnail"" />
    <meta property=""media:active-class"">ui-audioCurrent</meta>
    <meta property=""rendition:layout"">pre-paginated</meta>
    <opf:meta property=""media:duration"" refines=""#f2_overlay"">00:00:07.5230000</opf:meta>
    <opf:meta property=""media:duration"" refines=""#f4_overlay"">00:00:02.4030000</opf:meta>
    <opf:meta property=""media:duration"">00:00:09.9260000</opf:meta>
    <opf:meta property=""media:active-class"">-epub-media-overlay-active</opf:meta>
  </opf:metadata>
  <manifest>
    <item id=""f1"" href=""1.xhtml"" media-type=""application/xhtml+xml"" />
    <item id=""basePage"" href=""css/basePage.css"" media-type=""text/css"" />
    <item id=""origami"" href=""css/origami.css"" media-type=""text/css"" />
    <item id=""branding"" href=""css/branding.css"" media-type=""text/css"" />
    <item id=""Basic_Book"" href=""css/Basic_Book.css"" media-type=""text/css"" />
    <item id=""Device-XMatter"" href=""css/Device-XMatter.css"" media-type=""text/css"" />
    <item id=""langVisibility"" href=""css/langVisibility.css"" media-type=""text/css"" />
    <item id=""defaultLangStyles"" href=""css/defaultLangStyles.css"" media-type=""text/css"" />
    <item id=""customCollectionStyles"" href=""css/customCollectionStyles.css"" media-type=""text/css"" />
    <item id=""aor_adb15"" href=""images/aor_adb15.png"" media-type=""image/png"" />
    <item id=""f2"" href=""2.xhtml"" media-type=""application/xhtml+xml"" media-overlay=""f2_overlay"" />
    <item id=""f2_overlay"" href=""2_overlay.smil"" media-type=""application/smil+xml"" />
    <item id=""page2"" href=""audio/page2.mp3"" media-type=""audio/mpeg"" />
    <item id=""aor_CMB600"" href=""images/aor_CMB600.png"" media-type=""image/png"" />
    <item id=""f3"" href=""3.xhtml"" media-type=""application/xhtml+xml"" />
    <item id=""f4"" href=""4.xhtml"" media-type=""application/xhtml+xml"" media-overlay=""f4_overlay"" />
    <item id=""f4_overlay"" href=""4_overlay.smil"" media-type=""application/smil+xml"" />
    <item id=""ffcc72a7-9ee5-4ff3-91dd-5fa4834368bc"" href=""audio/ffcc72a7-9ee5-4ff3-91dd-5fa4834368bc.mp3"" media-type=""audio/mpeg"" />
    <item id=""aor_adb16"" href=""images/aor_adb16.png"" media-type=""image/png"" />
    <item id=""f5"" href=""5.xhtml"" media-type=""application/xhtml+xml"" />
    <item id=""BloomLocal"" href=""images/BloomLocal.svg"" media-type=""image/svg+xml"" />
    <item id=""f6"" href=""6.xhtml"" media-type=""application/xhtml+xml"" properties=""svg"" />
    <item id=""license"" href=""images/license.png"" media-type=""image/png"" />
    <item id=""f7"" href=""7.xhtml"" media-type=""application/xhtml+xml"" />
    <item id=""Bloom_Against_Light_HD"" href=""images/Bloom_Against_Light_HD.png"" media-type=""image/png"" />
    <item id=""f8"" href=""8.xhtml"" media-type=""application/xhtml+xml"" />
    <item id=""epub-thumbnail"" href=""images/epub-thumbnail.png"" media-type=""image/png"" properties=""cover-image"" />
    <item id=""AndikaNewBasic-R"" href=""fonts/AndikaNewBasic-R.ttf"" media-type=""application/vnd.ms-opentype"" />
    <item id=""fonts"" href=""css/fonts.css"" media-type=""text/css"" />
    <item id=""nav"" href=""nav.xhtml"" media-type=""application/xhtml+xml"" properties=""nav"" />
  </manifest>
  <spine>
    <itemref idref=""f1"" />
    <itemref idref=""f2"" />
    <itemref idref=""f3"" />
    <itemref idref=""f4"" />
    <itemref idref=""f5"" />
    <itemref idref=""f6"" />
    <itemref idref=""f7"" />
    <itemref idref=""f8"" />
  </spine>
</package>";

		private const string page2Smil = @"<?xml version=""1.0"" encoding=""utf-8""?>
<smil xmlns=""http://www.w3.org/ns/SMIL"" xmlns:epub=""http://www.idpf.org/2007/ops"" version=""3.0"">
  <body>
    <seq id=""id1"" epub:textref=""2.xhtml"" epub:type=""bodymatter chapter"">
      <par id=""s1"">
        <text src=""2.xhtml#f8b18f34-1da2-4c86-9a4b-1c76eabe61bd""/>
        <audio src=""audio/page2.mp3"" clipBegin=""0:00:00.000"" clipEnd=""0:00:02.011""/>
      </par>
      <par id=""s2"">
        <text src=""2.xhtml#abb8d0d7-34c3-4099-a3c7-f1ed1b379b13""/>
        <audio src=""audio/special.mp3"" clipBegin=""0:00:02.011"" clipEnd=""0:00:05.120""/>
      </par>
      <par id=""s3"">
        <text src=""2.xhtml#fe5483f1-28e3-4cc5-97c1-db2e312c85e8""/>
        <audio src=""audio/page2.mp3"" clipBegin=""0:00:05.120"" clipEnd=""1:02:07.523""/>
      </par>
    </seq>
  </body>
</smil>";

		private const string page4Smil = @"<?xml version=""1.0"" encoding=""utf-8""?>
<smil xmlns=""http://www.w3.org/ns/SMIL"" xmlns:epub=""http://www.idpf.org/2007/ops"" version=""3.0"">
  <body>
    <seq id=""id1"" epub:textref=""4.xhtml"" epub:type=""bodymatter chapter"">
      <par id=""s1"">
        <text src=""4.xhtml#ffcc72a7-9ee5-4ff3-91dd-5fa4834368bc""/>
        <audio src=""audio/ffcc72a7-9ee5-4ff3-91dd-5fa4834368bc.mp3"" clipBegin=""0:00:00.000"" clipEnd=""0:00:02.403""/>
      </par>
    </seq>
  </body>
</smil>";

		private TemporaryFolder _folder;
		ReadiumManifestRoot _manifest;
		private ReadiumMediaOverlay[] _overlays = new ReadiumMediaOverlay[2];


		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			_folder = new TemporaryFolder("EpubMakerReadiumManifestTests");
			var contentFolder = Path.Combine(_folder.FolderPath, "content");
			Directory.CreateDirectory(contentFolder);
			var opfPath = Path.Combine(contentFolder, "content.opf");
			RobustFile.WriteAllText(opfPath, opfData);
			var smil2Path = Path.Combine(contentFolder, "2_overlay.smil");
			RobustFile.WriteAllText(smil2Path, page2Smil);
			var smil4Path = Path.Combine(contentFolder, "4_overlay.smil");
			RobustFile.WriteAllText(smil4Path, page4Smil);

			var outputPath = ReadiumManifest.MakeReadiumManifest(_folder.FolderPath);

			_manifest = JsonConvert.DeserializeObject<ReadiumManifestRoot>(RobustFile.ReadAllText(outputPath));
			var overlay2Path = Path.Combine(_folder.FolderPath, "2-media-overlay.json");
			_overlays[0] = JsonConvert.DeserializeObject<ReadiumMediaOverlay>(RobustFile.ReadAllText(overlay2Path));
			var overlay4Path = Path.Combine(_folder.FolderPath, "4-media-overlay.json");
			_overlays[1] = JsonConvert.DeserializeObject<ReadiumMediaOverlay>(RobustFile.ReadAllText(overlay4Path));
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{
			_folder.Dispose();
		}

		[Test]
		public void HasExpectedType()
		{
			Assert.That(_manifest.type, Is.EqualTo("application/webpub+json"));
		}

		[Test]
		public void HasExpectedTitle()
		{
			Assert.That(_manifest.title, Is.EqualTo("comic 2"));
		}

		[Test]
		public void HasFixedLayout()
		{
			Assert.That(_manifest.metadata.rendition.layout, Is.EqualTo("fixed"));
		}

		[Test]
		public void HasMediaLayout()
		{
			Assert.That(_manifest.metadata.MediaOverlay.ActiveClass, Is.EqualTo("ui-audioCurrent"));
		}


		[Test]
		public void HasSelfLink()
		{
			var selfLink = _manifest.links[0];
			Assert.That(selfLink.type, Is.EqualTo("application/webpub+json"));
			Assert.That(selfLink.rel, Is.EqualTo("self"));
			var manifestPath = Path.Combine(_folder.FolderPath, "manifest.json");
			Assert.That(selfLink.href, Is.EqualTo(manifestPath.ToLocalhost()));
		}

		[TestCase(0, "content/1.xhtml")]
		[TestCase(1, "content/2.xhtml")]
		[TestCase(2, "content/3.xhtml")]
		public void HasSimpleReadingOrderEntry(int pageNum, string href)
		{
			var roItem = _manifest.readingOrder[pageNum];
			Assert.That(roItem.type, Is.EqualTo("application/xhtml+xml"));
			Assert.That(roItem.href, Is.EqualTo(href));
		}

		[TestCase(1, "3727.523", "2-media-overlay.json")]
		[TestCase(3, "2.403", "4-media-overlay.json")]
		public void HasOverlayReadingOrderEntry(int pageNum, string duration, string overlay)
		{
			var roItem = _manifest.readingOrder[pageNum];
			Assert.That(roItem.type, Is.EqualTo("application/xhtml+xml"));
			Assert.That(roItem.duration, Is.EqualTo(duration));
			Assert.That(roItem.properties.MediaOverlay, Is.EqualTo(overlay));
		}

		[TestCase(0, "content/2.xhtml")]
		[TestCase(1, "content/4.xhtml")]
		public void HasTextAndRoles(int index, string text)
		{
			Assert.That(_overlays[index].role, Is.EqualTo("section"));
			Assert.That(_overlays[index].narration[0].role, Is.EqualTo(new[] {"section", "bodymatter", "chapter"}));
			Assert.That(_overlays[index].narration[0].text, Is.EqualTo(text));
		}

		[TestCase(0, 0, "content/2.xhtml#f8b18f34-1da2-4c86-9a4b-1c76eabe61bd", "content/audio/page2.mp3#t=0.000,2.011")]
		[TestCase(0, 1, "content/2.xhtml#abb8d0d7-34c3-4099-a3c7-f1ed1b379b13", "content/audio/special.mp3#t=2.011,5.120")]
		[TestCase(0, 2, "content/2.xhtml#fe5483f1-28e3-4cc5-97c1-db2e312c85e8", "content/audio/page2.mp3#t=5.120,3727.523")]
		[TestCase(1, 0, "content/4.xhtml#ffcc72a7-9ee5-4ff3-91dd-5fa4834368bc", "content/audio/ffcc72a7-9ee5-4ff3-91dd-5fa4834368bc.mp3#t=0.000,2.403")]
		public void HasTextAndAudio(int index, int item, string text, string audio)
		{
			var innerNarration = _overlays[index].narration[0].narration[item];
			Assert.That(innerNarration.text, Is.EqualTo(text));
			Assert.That(innerNarration.audio, Is.EqualTo(audio));
		}
	}
}
