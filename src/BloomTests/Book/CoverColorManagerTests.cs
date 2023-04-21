using System.Xml;
using Bloom.Book;
using NUnit.Framework;
using SIL.Xml;
using System;
using Castle.Components.DictionaryAdapter.Xml;

namespace BloomTests.Book
{
	[TestFixture]
	public class CoverColorManagerTests
	{
		[Test]
		public void WriteCoverColorToDom_RemovesOldRules()
		{
			var newValue = "#777777";
			// I don't know why this test has these empty <style> things, nor what the TEXTAREA thing is about or if it's needed anymore.
			var dom = MakeDom("",
				@"<style type='text/css'>
					div.coverColor  textarea {
						background-color: #B2CC7D !important;
					}
					div.bloom-page.coverColor {
						background-color: #B2CC7D !important;
					}
				</style>
				<style type='text/css'>
					DIV.coverColor  TEXTAREA {
						background-color: #B2CC7D !important;
					}
					DIV.bloom-page.coverColor {
						background-color: #B2CC7D !important;
					}
				</style>
				<style type='text/css'>		DIV.bloom-page {
						/* don't remove me, I'm not trying to set the color */
					}</style>");
			CoverColorManager.WriteCoverColorToDom(dom, newValue);
			Assert.That(dom.SafeSelectNodes("//style").Count, Is.EqualTo(2));
			Assert.That(dom.SelectSingleNode("//style[1]").InnerText.Contains("don't remove me"));
			Assert.That(dom.SelectSingleNode("//style[2]").InnerText.Contains(newValue));
		}

		//	[Test]
		//	public void CreateBook_AlreadyHasCoverColorAndUserStyles_InWrongOrder_GetsStyleElementsReversed()
		//	{
		//		var coverStyle = @"<style type='text/css'>
		//DIV.coverColor  TEXTAREA  { background-color: #98D0B9 !important; }
		//DIV.bloom-page.coverColor { background-color: #98D0B9 !important; }
		//		</style>";
		//		var userStyle = @"<style type='text/css' title='userModifiedStyles'>
		//.normal-style[lang='fr'] { font-size: 9pt ! important; }
		//.normal-style { font-size: 9pt !important; }
		//		</style>";
		//		var dom = MakeDom("<div class='bloom-page' id='1'></div>", coverStyle + userStyle);


		//		var styleNodes = dom.Head.SafeSelectNodes("./style");
		//		Assert.AreEqual(2, styleNodes.Count);
		//		Assert.AreEqual("userModifiedStyles", styleNodes[0].Attributes["title"].Value);
		//		Assert.IsTrue(styleNodes[0].InnerText.Contains(".normal-style[lang='fr'] { font-size: 9pt ! important; }"));
		//		Assert.IsTrue(styleNodes[1].InnerText.Contains("coverColor"));
		//	}

		[Test]
		public void GetCoverColorFromDom_RegularHexCode_Works()
		{
			const string xml = @"<html><head>
				<style type='text/css'>
				    DIV.bloom-page.coverColor       {               background-color: #abcdef !important;   }
				</style>
			</head><body></body></html>";
			var document = new XmlDocument();
			document.LoadXml(xml);

			// SUT
			var result = CoverColorManager.GetCoverColorFromDom(new HtmlDom(document));

			Assert.AreEqual("#abcdef", result);
		}

		[Test]
		public void GetCoverColorFromDom_ColorWordWithComment_Works()
		{
			// This is from the Digital Comic Book template. (lowercase 'div' and intervening comment)
			const string xml = @"<html><head>
				<meta name='preserveCoverColor' content='true'></meta>
			    <style type='text/css'>
				    div.bloom-page.coverColor {
				        /* note that above, we have a meta ""preserveCoverColor"" tag to preserve this*/
				        background-color: black !important;
				    }
			    </style>
			</head><body></body></html>";
			var document = new XmlDocument();
			document.LoadXml(xml);

			// SUT
			var result = CoverColorManager.GetCoverColorFromDom(new HtmlDom(document));

			Assert.AreEqual("black", result);
		}

		[Test]
		public void GetCoverColorFromDom_MoonAndCapVersion_Works()
		{
			// This is from the Moon and Cap example book. (lowercase 'div' and extraneous textarea rule)
			const string xml = @"<html><head>
			    <style type='text/css'>
				    div.coverColor textarea {
				    background-color: #ffd4d4 !important;
				    }
				    div.bloom-page.coverColor {
				    background-color: #ffd4d4 !important;
				    }
			    </style>
			</head><body></body></html>";
			var document = new XmlDocument();
			document.LoadXml(xml);

			// SUT
			var result = CoverColorManager.GetCoverColorFromDom(new HtmlDom(document));

			Assert.AreEqual("#ffd4d4", result);
		}

		public static HtmlDom MakeDom(string bodyContents, string headContents = "")
		{
			return new HtmlDom(MakeBookHtml(bodyContents, headContents));
		}

		protected static string MakeBookHtml(string bodyContents, string headContents)
		{
			return @"<html ><head>" + headContents + "</head><body>" + bodyContents + "</body></html>";
		}
	}
}
