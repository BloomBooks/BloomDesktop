// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Bloom.Book;
using Bloom.Collection;
using BloomTemp;
using L10NSharp;
using NUnit.Framework;
using Palaso.IO;
using Bloom;
using Bloom.ImageProcessing;
using Bloom.web;
using Palaso.Reporting;
using TemporaryFolder = Palaso.TestUtilities.TemporaryFolder;

namespace BloomTests.web
{
	[TestFixture]
	public class EnhancedImageServerTests
	{
		private TemporaryFolder _folder;
		private BloomFileLocator _fileLocator;
		private string _collectionPath;

		[SetUp]
		public void Setup()
		{
			Logger.Init();
			_folder = new TemporaryFolder("ImageServerTests");
			var localizationDirectory = FileLocator.GetDirectoryDistributedWithApplication("localization");
			LocalizationManager.Create("fr", "Bloom", "Bloom", "1.0.0", localizationDirectory, "SIL/Bloom", null, "", new string[] { });

			ErrorReport.IsOkToInteractWithUser = false;
			_collectionPath = Path.Combine(_folder.Path, "TestCollection");
			var cs = new CollectionSettings(Path.Combine(_folder.Path, "TestCollection.bloomCollection"));
			_fileLocator = new BloomFileLocator(cs, new XMatterPackFinder(new string[] { }), ProjectContext.GetFactoryFileLocations(),
				ProjectContext.GetFoundFileLocations(), ProjectContext.GetAfterXMatterFileLocations());
		}

		[TearDown]
		public void TearDown()
		{
			_folder.Dispose();
			Logger.ShutDown();
		}

		[Test]
		public void CanGetImage()
		{
			// Setup
			using (var server = CreateImageServer())
			using (var file = MakeTempImage())
			{
				var transaction = new PretendRequestInfo(ServerBase.PathEndingInSlash + file.Path);

				// Execute
				server.MakeReply(transaction);

				// Verify
				Assert.IsTrue(transaction.ReplyImagePath.Contains(".png"));
			}
		}

		[Test]
		public void CanGetPdf()
		{
			// Setup
			using (var server = CreateImageServer())
			using (var file = TempFile.WithExtension(".pdf"))
			{
				var transaction = new PretendRequestInfo(ServerBase.PathEndingInSlash + file.Path);

				// Execute
				server.MakeReply(transaction);

				// Verify
				Assert.IsTrue(transaction.ReplyImagePath.Contains(".pdf"));
			}
		}

		[Test]
		public void ReportsMissingFile()
		{
			// Setup
			using (var server = CreateImageServer())
			{
				var transaction = new PretendRequestInfo(ServerBase.PathEndingInSlash + "/non-existing-file.pdf");

				// Execute
				server.MakeReply(transaction);

				// Verify
				Assert.That(transaction.StatusCode, Is.EqualTo(404));
				Assert.That(Logger.LogText, Contains.Substring("**EnhancedImageServer: File Missing: /non-existing-file.pdf"));
			}
		}

		[Test]
		public void Topics_ReturnsFrenchFor_NoTopic_()
		{
			Assert.AreEqual("Sans thème", QueryServerForJson("topics").NoTopic.ToString());
		}

		[Test]
		public void Topics_ReturnsFrenchFor_Dictionary_()
		{
			Assert.AreEqual("Dictionnaire", QueryServerForJson("topics").Dictionary.ToString());
		}

		private dynamic QueryServerForJson(string query)
		{
			using (var server = CreateImageServer())
			{
				var transaction = new PretendRequestInfo(ServerBase.PathEndingInSlash + query);
				server.MakeReply(transaction);
				Debug.WriteLine(transaction.ReplyContents);
				return Newtonsoft.Json.JsonConvert.DeserializeObject(transaction.ReplyContents);
			}
		}

		private EnhancedImageServer CreateImageServer()
		{
			return new EnhancedImageServer(new RuntimeImageProcessor(new BookRenamedEvent()), _fileLocator);
		}

		private TempFile MakeTempImage()
		{
			var file = TempFile.WithExtension(".png");
			File.Delete(file.Path);
			using(var x = new Bitmap(100,100))
			{
				x.Save(file.Path, ImageFormat.Png);
			}
			return file;
		}

		[Test]
		public void CanRetrieveContentOfFakeTempFile_ButOnlyUntilDisposed()
		{
			using (var server = CreateImageServer())
			{
				var html = @"<html ><head></head><body>here it is</body></html>";
				var dom = new HtmlDom(html);
				dom.BaseForRelativePaths =_folder.Path.ToLocalhost();
				string url;
				using (var fakeTempFile = EnhancedImageServer.MakeSimulatedPageFileInBookFolder(dom))
				{
					url = fakeTempFile.Key;
					var transaction = new PretendRequestInfo(url);

					// Execute
					server.MakeReply(transaction);

					// Verify
					// Whitespace inserted by CreateHtml5StringFromXml seems to vary across versions and platforms.
					// I would rather verify the actual output, but don't want this test to be fragile, and the
					// main point is that we get a file with the DOM content.
					Assert.That(transaction.ReplyContents,
						Is.EqualTo(TempFileUtils.CreateHtml5StringFromXml(dom.RawDom)));
				}
				var transactionFail = new PretendRequestInfo(url);

				// Execute
				server.MakeReply(transactionFail);

				// Verify
				Assert.That(transactionFail.StatusCode, Is.EqualTo(404));
			}
		}

		private void SetupCssTests()
		{
			// create collection directory
			Directory.CreateDirectory(_collectionPath);

			// settingsCollectionStyles.css
			var cssFile = Path.Combine(_collectionPath, "settingsCollectionStyles.css");
			File.WriteAllText(cssFile, @".settingsCollectionStylesCssTest{}");

			// create book directory
			var bookPath = Path.Combine(_collectionPath, "TestBook");
			Directory.CreateDirectory(bookPath);

			// languageDisplay.css
			cssFile = Path.Combine(bookPath, "languageDisplay.css");
			File.WriteAllText(cssFile, @".languageDisplayCssTest{}");

			// Factory-XMatter.css
			cssFile = Path.Combine(bookPath, "Factory-XMatter.css");
			File.WriteAllText(cssFile, @".factoryXmatterCssTest{}");

			// miscStyles.css - a file name not distributed with or created by Bloom
			cssFile = Path.Combine(bookPath, "miscStyles.css");
			File.WriteAllText(cssFile, @".miscStylesCssTest{}");
		}

		[Test]
		public void GetCorrect_LanguageDisplayCss()
		{
			using (var server = CreateImageServer())
			{
				SetupCssTests();
				var cssFile = Path.Combine(_folder.Path, "TestCollection", "TestBook", "languageDisplay.css");

				var url = cssFile.ToLocalhost();
				var transaction = new PretendRequestInfo(url);

				server.MakeReply(transaction);

				Assert.That(transaction.ReplyContents, Is.EqualTo(".languageDisplayCssTest{}"));
			}
		}

		[Test]
		public void GetCorrect_SettingsCollectionStylesCss()
		{
			using (var server = CreateImageServer())
			{
				SetupCssTests();
				var cssFile = Path.Combine(_folder.Path, "TestCollection", "settingsCollectionStyles.css");

				var url = cssFile.ToLocalhost();
				var transaction = new PretendRequestInfo(url);

				server.MakeReply(transaction);

				Assert.That(transaction.ReplyContents, Is.EqualTo(".settingsCollectionStylesCssTest{}"));
			}
		}

		[Test]
		public void GetCorrect_XmatterStylesCss()
		{
			using (var server = CreateImageServer())
			{
				SetupCssTests();
				var cssFile = Path.Combine(_folder.Path, "TestCollection", "TestBook", "Factory-XMatter.css");

				var url = cssFile.ToLocalhost();
				var transaction = new PretendRequestInfo(url);

				server.MakeReply(transaction);

				Assert.AreNotEqual(transaction.ReplyContents, ".factoryXmatterCssTest{}");
			}
		}

		[Test]
		public void GetCorrect_MiscStylesCss()
		{
			using (var server = CreateImageServer())
			{
				SetupCssTests();
				var cssFile = Path.Combine(_folder.Path, "TestCollection", "TestBook", "miscStyles.css");

				var url = cssFile.ToLocalhost();
				var transaction = new PretendRequestInfo(url);

				server.MakeReply(transaction);

				Assert.That(transaction.ReplyContents, Is.EqualTo(".miscStylesCssTest{}"));
			}
		}
	}
}
