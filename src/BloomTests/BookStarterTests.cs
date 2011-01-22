using System.IO;
using System.Xml;
using Bloom;
using NUnit.Framework;
using Palaso.IO;
using Palaso.Reporting;
using Palaso.TestUtilities;


namespace BloomTests
{
	[TestFixture]
	public class BookStarterTests
	{
		private FileLocator _fileLocator;
		private BookStarter _starter;

		[SetUp]
		public void Setup()
		{
			ErrorReport.IsOkToInteractWithUser = false;
			_fileLocator = new FileLocator(new string[]
											{
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections"),
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections", "Templates", "A5Portrait")
											});
			_starter = new BookStarter(dir => new BookStorage(dir, _fileLocator));
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryA5_CreatesWithCoverAndTitle()
		{
			using (var dest = new TemporaryFolder("DestBookStorage"))
			{
				var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates", "A5Portrait");

				_starter.CreateBookOnDiskFromTemplate(source, dest.FolderPath);

				string path = dest.Combine("new", "new.htm");
				AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'cover')]", 1);
				AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'titlePage')]", 1);
			}
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryA5Portrait_CreatesWithCorrectStylesheets()
		{
			using (var dest = new TemporaryFolder("DestBookStorage"))
			{
				var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates", "A5Portrait");

				_starter.CreateBookOnDiskFromTemplate(source, dest.FolderPath);

				string path = dest.Combine("new", "new.htm");
				AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'A5Portrait')]", 1);
				AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'preview')]", 1);
				AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//link", 2);
			}
		}

//		[Test]
//		public void CopyToFolder_HasSubfolders_AllCopied()
//		{
//			using (var source = new TemporaryFolder("SourceBookStorage"))
//			using (var dest = new TemporaryFolder("DestBookStorage"))
//			{
//				File.WriteAllText(source.Combine("zero.txt"), "zero");
//				Directory.CreateDirectory(source.Combine("inner"));
//				File.WriteAllText(source.Combine("inner", "one.txt"), "one");
//				Directory.CreateDirectory(source.Combine("inner", "more inner"));
//				File.WriteAllText(source.Combine("inner", "more inner", "two.txt"), "two");
//
//				var storage = new BookStorage(source.FolderPath, null);
//				storage.CopyToFolder(dest.FolderPath);
//
//				Assert.That(Directory.Exists(dest.Combine("inner", "more inner")));
//				Assert.That(File.Exists(dest.Combine("zero.txt")));
//				Assert.That(File.Exists(dest.Combine("inner", "one.txt")));
//				Assert.That(File.Exists(dest.Combine("inner", "more inner", "two.txt")));
//			}
//		}
	}
}
