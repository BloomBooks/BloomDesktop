using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Bloom;
using Bloom.Edit;
using Moq;
using NUnit.Framework;
using Palaso.IO;
using Palaso.Reporting;
using Palaso.TestUtilities;


namespace BloomTests
{
	[TestFixture]
	public class BookStorageTests
	{
		private FileLocator _fileLocator;
		private TemporaryFolder _folder;
		private string _bookPath;

		[SetUp]
		public void Setup()
		{
			ErrorReport.IsOkToInteractWithUser = false;
			_fileLocator = new FileLocator(new string[]
											{
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections"),
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections", "Templates", "A5Portrait")
											});
			_folder = new TemporaryFolder("BloomBookStorageTest");
			_bookPath = _folder.Combine("BloomBookStorageTest.htm");
		}
		[Test]
		public void Save_BookHadOnlyPaperSizeStyleSheet_StillHasIt()
		{
			File.WriteAllText(_bookPath, "<html xmlns='http://www.w3.org/1999/xhtml'><head><link rel='stylesheet' href='A5Portrait.css' type='text/css' /></head><body/></html>");
			var storage = new BookStorage(_folder.FolderPath, _fileLocator);
			storage.Save();
			//AssertThatXmlIn.File(temp.Path).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'A5Portrait')]", 1);
			AssertThatXmlIn.File(_bookPath).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'A5Portrait')]", 1);
			AssertThatXmlIn.File(_bookPath).HasSpecifiedNumberOfMatchesForXpath("//link", 1);
		}

		[Test]
		public void Save_BookHadEditStyleSheet_NowHasNone()
		{
			File.WriteAllText(_bookPath, "<html xmlns='http://www.w3.org/1999/xhtml'><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body/></html>");
			var storage = new BookStorage(_folder.FolderPath, _fileLocator);
			storage.Save();
			//AssertThatXmlIn.File(temp.Path).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'A5Portrait')]", 1);
			//AssertThatXmlIn.File(_bookPath).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'preview')]", 1);
			AssertThatXmlIn.File(_bookPath).HasSpecifiedNumberOfMatchesForXpath("//link", 0);
		}

	}
}
