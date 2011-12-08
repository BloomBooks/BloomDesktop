using System.Xml;
using Bloom.Book;
using NUnit.Framework;
using Palaso.Xml;

namespace BloomTests.Book
{
	[TestFixture]
	public sealed class PageTests
	{
		[Test]
		public void GetSourceTexts_WrappedByParagraphWithId_GetsCorrectTexts()
		{
			XmlElement pageDiv = (XmlElement)GetDom().SelectSingleNodeHonoringDefaultNS("//div[@id='pageWithTokPisinAndEnglish']");

			var p = new Page(pageDiv, "caption", null, pg => pageDiv);
			var texts = p.GetSourceTexts("2", "xyz");
			Assert.AreEqual(2, texts.Count);
			Assert.AreEqual("2en", texts["en"]);
			Assert.AreEqual("2tpi", texts["tpi"]);
		}

		[Test]
		public void GetSourceTexts_HasVerncularToo_GetsOnlyNonVernacular()
		{
			XmlElement pageDiv = (XmlElement)GetDom().SelectSingleNodeHonoringDefaultNS("//div[@id='pageWithTokPisinAndEnglish']");

			var p = new Page(pageDiv, "caption", null, pg => pageDiv);
			var texts = p.GetSourceTexts("1", "xyz");
			Assert.AreEqual(2, texts.Count);
			Assert.AreEqual("1en", texts["en"]);
			Assert.AreEqual("1tpi", texts["tpi"]);
		}

		private XmlDocument GetDom()
		{
			var content = @"<?xml version='1.0' encoding='utf-8' ?>
				<html>
					<body class='a5Portrait'>
					<div class='-bloom-page' testid='pageWithJustTokPisin'>
						 <p id='0'>
							<textarea lang='tpi'> Taim yu planim gaden yu save wokim banis.</textarea>
						</p>
					</div>
				<div class='-bloom-page' id='pageWithTokPisinAndEnglish'>
							<p id='1'>
								<textarea lang='en' >1en</textarea>
								<textarea lang='tpi'>1tpi</textarea>
								<textarea lang='xyz'>1tpi</textarea>
							</p>
							<p id='2'>
								<textarea lang='en'>2en</textarea>
								<textarea lang='tpi'>2tpi</textarea>
							 </p>
						</div>
				</body>
				</html>
		";
			var dom = new XmlDocument();
			dom.LoadXml(content);
			return dom;
		}
	}

}
