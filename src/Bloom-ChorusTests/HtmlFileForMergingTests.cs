using System.IO;
using Bloom_ChorusPlugin;
using NUnit.Framework;
using Palaso.IO;
using Palaso.TestUtilities;

namespace Bloom_ChorusTests
{
	[TestFixture]
	public class HtmlFileForMergingTests
	{
		[Test]
		public void GetPathToXHtml_IsXml()
		{
			const string html = @"<html><head></head><body><p></body></html>";
			using(var htmlFile = new TempFile(html))
			{
				var xmlPath = new HtmlFileForMerging(htmlFile.Path).GetPathToXHtml();
				Assert.IsTrue(File.Exists(xmlPath));
				AssertThatXmlIn.File(xmlPath).HasSpecifiedNumberOfMatchesForXpath("//p",1);
			}
		}
		[Test]
		public void SaveHtml_IsHtml()
		{
			const string html = @"<html><head></head><body><p>one</body></html>";
			using (var htmlFile = new TempFile(html))
			{
				var htmlFileForMerging = new HtmlFileForMerging(htmlFile.Path);
				var xmlPath = htmlFileForMerging.GetPathToXHtml();
				var xml = File.ReadAllText(xmlPath);
				xml = xml.Replace("one","one<br/>two");
				File.WriteAllText(xmlPath, xml);
				htmlFileForMerging.SaveHtml();
				var newHtml = File.ReadAllText(htmlFile.Path);
				Assert.IsTrue(newHtml.StartsWith("<!DOCTYPE html>"));
				Assert.IsTrue(newHtml.Contains("two"));
			}
		}
	}
}