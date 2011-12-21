using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Bloom.Book;
using NUnit.Framework;
using Palaso.Extensions;
using Palaso.IO;
using Palaso.TestUtilities;

namespace BloomTests.Book
{
	[TestFixture]
	public class XMatterHelperTests
	{
		private XmlDocument _dom;
		private DataSet _dataSet;

		[SetUp]
		public void Setup()
		{
			_dom = new XmlDocument();
			_dom.LoadXml("<html><head> <link href='file://blahblah\\a5portrait.css' type='text/css' /></head><body><div class='-bloom-dataDiv'></div><div id ='firstPage' class='-bloom-page'>1st page</div></body></html>");
			_dataSet = new DataSet();
			_dataSet.WritingSystemCodes.Add("V","xyz");
			_dataSet.WritingSystemCodes.Add("N1", "fr");
			_dataSet.WritingSystemCodes.Add("N2", "en");
		}
		private XMatterHelper CreateHelper()
		{
			var factoryCollections = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections");
			var factoryXMatter = FileLocator.GetDirectoryDistributedWithApplication("xMatter","Factory-XMatter");
			return new XMatterHelper(_dom, "Factory", new FileLocator(new string[] { factoryXMatter }));
		}

		[Test]
		public void PathToXMatterHtml_AllDefaults_Correct()
		{
			string pathToXMatterHtml = CreateHelper().PathToXMatterHtml;
			Assert.IsTrue(File.Exists(pathToXMatterHtml), pathToXMatterHtml);
		}

		[Test]
		public void GetStyleSheetFileName_AllDefaults_Correct()
		{
			Assert.AreEqual("A5-portrait-Factory-XMatter.css",CreateHelper().GetStyleSheetFileName());
		}

		[Test]
		public void InjectXMatter_AllDefaults_Inserts3PagesBetweenDataDivAndFirstPage()
		{
			CreateHelper().InjectXMatter(_dataSet);
			AssertThatXmlIn.Dom(_dom).HasSpecifiedNumberOfMatchesForXpath("//body/div[1][contains(@class,'-bloom-dataDiv')]", 1);
			AssertThatXmlIn.Dom(_dom).HasSpecifiedNumberOfMatchesForXpath("//body/div[2][contains(@class,'cover')]", 1);
			AssertThatXmlIn.Dom(_dom).HasSpecifiedNumberOfMatchesForXpath("//body/div[3][contains(@class,'titlePage')]", 1);
			AssertThatXmlIn.Dom(_dom).HasSpecifiedNumberOfMatchesForXpath("//body/div[4][contains(@class,'verso')]", 1);
			AssertThatXmlIn.Dom(_dom).HasSpecifiedNumberOfMatchesForXpath("//body/div[5][@id='firstPage']", 1);
		}

		//TODO: tests with a different paper size

		//TODO: tests with a custom pack, with images

		//TODO: test with custom pack and a paper size/orientation that we're missing a css for, should fall back to factory

		//TODO: test with defaults but a paper size/orientation that we're missing a css for, should warn and use a5portrait
	}
}
