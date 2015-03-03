// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Bloom.Book;
using BloomTemp;
using L10NSharp;
using NUnit.Framework;
using Palaso.IO;
using Bloom;
using Bloom.ImageProcessing;
using Bloom.web;
using Palaso.Reporting;
using RestSharp;
using TemporaryFolder = Palaso.TestUtilities.TemporaryFolder;

namespace BloomTests.web
{
	[TestFixture]
	public class EnhancedImageServerTests
	{
		private TemporaryFolder _folder;

		[SetUp]
		public void Setup()
		{
			Logger.Init();
			_folder = new TemporaryFolder("ImageServerTests");
			var localizationDirectory = FileLocator.GetDirectoryDistributedWithApplication("localization");
			LocalizationManager.Create("fr", "Bloom", "Bloom", "1.0.0", localizationDirectory, "SIL/Bloom", null, "", new string[] { });
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
		private static dynamic QueryServerForJson(string query)
		{
			using (var server = CreateImageServer())
			{
				var transaction = new PretendRequestInfo(ServerBase.PathEndingInSlash + query);
				server.MakeReply(transaction);
				Debug.WriteLine(transaction.ReplyContents);
				return Newtonsoft.Json.JsonConvert.DeserializeObject(transaction.ReplyContents);
			}
		}

		private static EnhancedImageServer CreateImageServer()
		{
			return new EnhancedImageServer(new RuntimeImageProcessor(new BookRenamedEvent()));
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
	}
}
