using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.web.controllers;
using Moq;
using NUnit.Framework;
using SIL.Text;

namespace BloomTests.web.controllers
{
	[TestFixture]
	class ProblemReportApiTests
	{
		private BloomServer _server;

		/// <summary>
		/// Sets up the Bloom server and registers the Problem Report API handler to it
		/// </summary>
		private void SetupApiHandler(BookSelection bookSelection)
		{
			_server = new BloomServer(bookSelection);
			var controller = new ProblemReportApi(bookSelection);
			controller.RegisterWithApiHandler(_server.ApiHandler);
		}

		private BookSelection GetDefaultBookSelection()
		{
			var bookSelection = new BookSelection();
			var mockBook = new Mock<Bloom.Book.Book>();
			mockBook.Setup(x => x.TitleBestForUserDisplay).Returns("Fake Book Title");

			bookSelection.SelectBook(mockBook.Object);

			return bookSelection;
		}


		[TearDown]
		public void TearDown()
		{
			if (_server != null)
			{
				_server.Dispose();
				_server = null;
			}
		}

		[Test]
		public void ReportHeadingHtml_GivenUnencodedHtmlInSummary_EncodesTheHtml()
		{
			bool isSummaryPreEncoded = false;
			SetupApiHandler(GetDefaultBookSelection());

			ProblemReportApi.GatherReportInfoExceptScreenshot(null, "Fake Details", "Fake Problem: See <a href=\"http://bloomlibrary.org\">Bloom Library</a> for help", isSummaryPreEncoded);
			var result = ApiTest.GetString(_server,"problemReport/reportHeadingHtml");

			Assert.That(result, Is.EqualTo("Fake Problem: See &lt;a href=&quot;http://bloomlibrary.org&quot;&gt;Bloom Library&lt;/a&gt; for help"));
		}

		[Test]
		public void ReportHeadingHtml_GivenPreEncodedHtmlInSummary_ReturnsRawHtmlDirectly()
		{
			bool isSummaryPreEncoded = true;
			SetupApiHandler(GetDefaultBookSelection());

			ProblemReportApi.GatherReportInfoExceptScreenshot(null, "Fake Details", "Fake Problem: See <a href=\"http://bloomlibrary.org\">Bloom Library</a> for help", isSummaryPreEncoded);
			var result = ApiTest.GetString(_server,"problemReport/reportHeadingHtml");

			Assert.That(result, Is.EqualTo("Fake Problem: See <a href=\"http://bloomlibrary.org\">Bloom Library</a> for help"));
		}
	}
}
