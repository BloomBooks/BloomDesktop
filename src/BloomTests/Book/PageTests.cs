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
		public void GetSourceTexts_()
		{
			XmlElement pageDiv = (XmlElement)GetDom().SelectSingleNodeHonoringDefaultNS("//div[@id='pageWithTokPisinAndEnglish']");

			var p = new Page(pageDiv, "caption", null, pg => pageDiv);
			var texts = p.GetSourceTexts("text2");
			Assert.AreEqual(2, texts.Count);
			Assert.AreEqual("2en", texts["en"]);
			Assert.AreEqual("2tpi", texts["tpi"]);
		}


		private XmlDocument GetDom()
		{
			var content = @"<?xml version='1.0' encoding='utf-8' ?>
				<html xmlns='http://www.w3.org/1999/xhtml'>
					<body class='a5Portrait'>
					<div class='-bloom-page' testid='pageWithJustTokPisin'>
						 <p>
							<textarea lang='tpi' id='text1' class='text'> Taim yu planim gaden yu save wokim banis.</textarea>
						</p>
					</div>
				<div class='-bloom-page' id='pageWithTokPisinAndEnglish'>
							<p>
								<textarea lang='en' id='text1' class='text'>1en</textarea>
								<textarea lang='tpi' id='text1' class='text'>1tpi</textarea>
							</p>
							<p>
								<textarea lang='en' id='text2' class='text'>2en</textarea>
								<textarea lang='tpi' id='text2' class='text'>2tpi</textarea>
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
