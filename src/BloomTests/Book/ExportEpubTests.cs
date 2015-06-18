using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using BloomTemp;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;
using Palaso.Extensions;

namespace BloomTests.Book
{
	[TestFixture]
	public class ExportEpubTests : BookTests
	{
		[Test]
		public void SaveEpub()
		{
			SetDom(@"<div class='bloom-page'>
						<div id='somewrapper'>
							<div id='test' class='bloom-translationGroup bloom-requiresParagraphs' lang=''>
								<div aria-describedby='qtip-1' class='bloom-editable' lang='en'>
									This is some text
								</div>
								<div lang = '*'>more text</div>
							</div>
							<div><img src='myImage.png'></img></div>
							<div><img src='my image.png'></img></div>
						</div>
					</div>");
			var book = CreateBook();
			// These two names are especially interesting because they differ by case and also white space.
			// The case difference is not important to the Windows file system.
			// The white space must be removed to make an XML ID.
			MakeSamplePngImageWithMetadata(book.FolderPath.CombineForPath("myImage.png"));
			MakeSamplePngImageWithMetadata(book.FolderPath.CombineForPath("my image.png"));
			var epubFolder = new TemporaryFolder();
			var epubName = "output.epub";
			var epubPath = Path.Combine(epubFolder.FolderPath, epubName);
			book.SaveEpub(epubPath);
			Assert.That(File.Exists(epubPath));
			var zip = new ZipFile(epubPath);

			// Every epub must have a mimetype at the root
			GetZipContent(zip, "mimetype");

			// Every epub must have a "META-INF/container.xml." (case matters). Most things we could check about its content
			// would be redundant with the code that produces it, but we can at least verify that it is valid
			// XML and points us at the rootfile (open package format) file.
			var containerData = GetZipContent(zip, "META-INF/container.xml");
			var doc = XDocument.Parse(containerData);
			XNamespace ns = doc.Root.Attribute("xmlns").Value;
			var packageFile = doc.Root.Element(ns + "rootfiles").Element(ns + "rootfile").Attribute("full-path").Value;

			// That gives us a path to the main package file, typically content.opf
			var packageData = StripXmlHeader(GetZipContent(zip, packageFile));
			var toCheck = AssertThatXmlIn.String(packageData);
			var mgr = new XmlNamespaceManager(toCheck.NameTable);
			mgr.AddNamespace("dc", "http://purl.org/dc/elements/1.1/");
			mgr.AddNamespace("opf", "http://www.idpf.org/2007/opf");
			toCheck.HasAtLeastOneMatchForXpath("package[@version='3.0']");
			toCheck.HasAtLeastOneMatchForXpath("package[@unique-identifier]");
			toCheck.HasAtLeastOneMatchForXpath("opf:package/opf:metadata/dc:title", mgr);
			toCheck.HasAtLeastOneMatchForXpath("opf:package/opf:metadata/dc:language", mgr);
			toCheck.HasAtLeastOneMatchForXpath("opf:package/opf:metadata/dc:identifier", mgr);
			toCheck.HasAtLeastOneMatchForXpath("package/metadata/meta[@property='dcterms:modified']");

			toCheck.HasAtLeastOneMatchForXpath("package/manifest/item[@id='f1' and @href='1.xhtml']");
			toCheck.HasAtLeastOneMatchForXpath("package/manifest/item[@id='fmyImage' and @href='myImage.png']");
			toCheck.HasAtLeastOneMatchForXpath("package/manifest/item[@id='fmyimage1' and @href='my image.png']");
			toCheck.HasAtLeastOneMatchForXpath("package/spine/itemref[@idref='f1']");
			toCheck.HasAtLeastOneMatchForXpath("package/manifest/item[@properties='nav']");

			var packageDoc = XDocument.Parse(packageData);
			XNamespace opf = "http://www.idpf.org/2007/opf";
			// Some attempt at validating that we actually included the images in the zip.
			// Enhance: This undesirably depends on the exact order of items in the manifest.
			var image1 = packageDoc.Root.Element(opf + "manifest").Elements(opf + "item").ToArray()[0].Attribute("href").Value;
			GetZipEntry(zip, Path.GetDirectoryName(packageFile) + "/" + image1);
			var image2 = packageDoc.Root.Element(opf + "manifest").Elements(opf + "item").ToArray()[1].Attribute("href").Value;
			GetZipEntry(zip, Path.GetDirectoryName(packageFile) + "/" + image2);

			var page1 = packageDoc.Root.Element(opf + "manifest").Elements(opf + "item").ToArray()[2].Attribute("href").Value;
			// Names in package file are relative to its folder.
			var page1Data = GetZipContent(zip, Path.GetDirectoryName(packageFile) + "/" + page1);
			// This is possibly too strong; see comment where we remove them.
			AssertThatXmlIn.String(page1Data).HasNoMatchForXpath("//*[@aria-describedby]");
			// Not sure why we sometimes have these, but validator doesn't like them.
			AssertThatXmlIn.String(page1Data).HasNoMatchForXpath("//*[@lang='']");
			XNamespace xhtml = "http://www.w3.org/1999/xhtml";
			var mgr2 = new XmlNamespaceManager(new NameTable());
			mgr2.AddNamespace("xhtml", "http://www.w3.org/1999/xhtml");

			AssertThatXmlIn.String(page1Data).HasNoMatchForXpath("//xhtml:script", mgr2);
			AssertThatXmlIn.String(page1Data).HasNoMatchForXpath("//*[@lang='*']");

			mgr2.AddNamespace("epub", "http://www.idpf.org/2007/ops");
			var navPage = packageDoc.Root.Element(opf + "manifest").Elements(opf + "item").Last().Attribute("href").Value;
			var navPageData = StripXmlHeader(GetZipContent(zip, Path.GetDirectoryName(packageFile) + "/" + navPage));
			AssertThatXmlIn.String(navPageData)
				.HasAtLeastOneMatchForXpath(
					"xhtml:html/xhtml:body/xhtml:nav[@epub:type='toc' and @id='toc']/xhtml:ol/xhtml:li/xhtml:a[@href='1.xhtml']", mgr2);


			// Todo: check more about result...
		}

		private string GetZipContent(ZipFile zip, string path)
		{
			var entry = GetZipEntry(zip, path);
			var buffer = new byte[entry.Size];
			var stream = zip.GetInputStream(entry);
			stream.Read(buffer, 0, (int) entry.Size);
			return Encoding.UTF8.GetString(buffer);
		}

		private static ZipEntry GetZipEntry(ZipFile zip, string path)
		{
			var entry = zip.GetEntry(path);
			Assert.That(entry, Is.Not.Null, "Should have found entry at " + path);
			Assert.That(entry.Name, Is.EqualTo(path), "Expected entry has wrong case");
			return entry;
		}

		private string StripXmlHeader(string data)
		{
			var index = data.IndexOf("?>");
			if (index > 0)
				return data.Substring(index + 2);
			return data;
		}
	}
}
