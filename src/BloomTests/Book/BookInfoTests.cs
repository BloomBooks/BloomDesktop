using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Book;
using NUnit.Framework;
using Palaso.IO;
using Palaso.Reporting;
using Palaso.TestUtilities;

namespace BloomTests.Book
{
	public class BookInfoTests
	{
		private TemporaryFolder _fixtureFolder;
		private TemporaryFolder _folder;
		[SetUp]
		public void Setup()
		{
			_fixtureFolder = new TemporaryFolder("BloomBookStorageTest");
			_folder = new TemporaryFolder(_fixtureFolder, "theBook");
		}

		[TearDown]
		public void TearDown()
		{
			_fixtureFolder.Dispose();
		}

		[Test]
		public void Constructor_LoadsMetaDataFromJson()
		{
			var jsonPath = Path.Combine(_folder.Path, BookInfo.MetaDataFileName);
			File.WriteAllText(jsonPath, @"{'folio':'true','experimental':'true','suitableForMakingShells':'true'}");
			var bi = new BookInfo(_folder.Path, true);
			Assert.That(bi.IsExperimental);
			Assert.That(bi.IsFolio);
			Assert.That(bi.IsSuitableForMakingShells);
		}

		[Test]
		public void Constructor_FallsBackToTags()
		{
			var tagsPath = Path.Combine(_folder.Path, "tags.txt");
			File.WriteAllText(tagsPath, @"folio\nexperimental\nsuitableForMakingShells\n");
			var bi = new BookInfo(_folder.Path, true);
			Assert.That(bi.IsExperimental);
			Assert.That(bi.IsFolio);
			Assert.That(bi.IsSuitableForMakingShells);

			// Check that json takes precedence
			var jsonPath = Path.Combine(_folder.Path, BookInfo.MetaDataFileName);
			File.WriteAllText(jsonPath, @"{'folio':'false','experimental':'true','suitableForMakingShells':'false'}");
			bi = new BookInfo(_folder.Path, true);
			Assert.That(bi.IsExperimental);
			Assert.That(bi.IsFolio, Is.False);
			Assert.That(bi.IsSuitableForMakingShells, Is.False);
		}

		[Test]
		public void TitleSetter_FixesTitleWithXml()
		{
			var jsonPath = Path.Combine(_folder.Path, BookInfo.MetaDataFileName);
			File.WriteAllText(jsonPath,
				"{'suitableForMakingShells':true,'experimental':false,'title':'<span class=\"sentence-too-long\" data-segment=\"sentence\">Book on &lt;span&gt;s\r\n</span>'}");
			var bi = new BookInfo(_folder.Path, true); // loads metadata, but doesn't use Title setter
			// SUT
			bi.Title = bi.Title; // exercises setter
			Assert.That(bi.IsExperimental, Is.False);
			Assert.AreEqual("Book on <span>s\r\n", bi.Title);
		}
	}
}
