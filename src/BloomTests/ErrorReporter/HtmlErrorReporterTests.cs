using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Bloom.ErrorReporter;
using Bloom.MiscUI;
using Bloom.web.controllers;
using BloomTests.DataBuilders;
using Moq;
using NUnit.Framework;
using SIL.Reporting;

namespace BloomTests.ErrorReporter
{
	[TestFixture]
	public class HtmlErrorReporterTests
	{
		private string _testValue = "";

		private Mock<IBrowserDialogFactory> GetDefaultMockBrowserDialogFactory()
		{
			var mockFactory = new Mock<IBrowserDialogFactory>();
			var mockBrowserDialog = new Mock<IBrowserDialog>();
			mockFactory.Setup(x => x.CreateBrowserDialog(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<Action>())).Returns(mockBrowserDialog.Object);

			return mockFactory;
		}

		#region GetMessage tests
		[Test]
		public void GetMessage_OnlyText()
		{
			var result = HtmlErrorReporter.GetMessage("message text", null);
			Assert.AreEqual("message text", result.NotEncoded);
		}

		[Test]
		public void GetMessage_OnlyException()
		{
			var exception = new ApplicationException("fake exception");
			var result = HtmlErrorReporter.GetMessage(null, exception);
			Assert.AreEqual("fake exception", result.NotEncoded);
		}

		[Test]
		public void GetMessage_TextAndException_ReturnsTextOnly()
		{
			var exception = new ApplicationException("fake exception");
			var result = HtmlErrorReporter.GetMessage("message text", exception);
			Assert.AreEqual("message text", result.NotEncoded);
		}
		#endregion


		#region NotifyUserOfProblem tests
		[Test]
		public void NotifyUserOfProblem_UnsafeMessage()
		{
			var mockFactory = GetDefaultMockBrowserDialogFactory();
			var reporter = new HtmlErrorReporterBuilder()
				.WithTestValues()
				.BrowserDialogFactory(mockFactory.Object)
				.Build();

			// System Under Test
			reporter.NotifyUserOfProblem(new ShowAlwaysPolicy(), "", ErrorResult.Yes, "<b>Tags should be encoded</b>");

			// FYI: The URL uses a substring specific to the actual directory, so use .Contains() instead of an equality check
			mockFactory.Verify(x => x.CreateBrowserDialog(
				It.Is<string>(url => url.Contains("/browser/problemDialog/loader.html?level=notify&msg=%3cb%3eTags%20should%20be%20encoded%3c%2fb%3e")),
				It.IsAny<bool>(), It.IsAny<Action>()));
		}

		[Test]
		public void NotifyUserOfProblem_LongMessage()
		{
			var messageTextBuilder = new StringBuilder();
			for (int i = 0; i < 3000; ++i)
			{
				messageTextBuilder.Append('a');
			}
			var messageText = messageTextBuilder.ToString();

			var mockFactory = GetDefaultMockBrowserDialogFactory();
			var reporter = new HtmlErrorReporterBuilder()
				.WithTestValues()
				.BrowserDialogFactory(mockFactory.Object)
				.Build();

			// System Under Test
			reporter.NotifyUserOfProblem(new ShowAlwaysPolicy(), "", ErrorResult.Yes, messageText);

			// Verification
			mockFactory.Verify(x => x.CreateBrowserDialog(
				It.Is<string>(url => url.Contains("/browser/problemDialog/loader.html?level=notify")),
				It.IsAny<bool>(), It.IsAny<Action>()));

			Assert.AreEqual(messageText, ProblemReportApi.NotifyMessage);
		}

		[TestCase("Report")]
		[TestCase("CustomReport")]
		public void NotifyUserOfProblem_ReportButton(string reportLabel)
		{
			var mockFactory = GetDefaultMockBrowserDialogFactory();
			var reporter = new HtmlErrorReporterBuilder()
				.WithTestValues()
				.BrowserDialogFactory(mockFactory.Object)
				.Build();

			// System Under Test
			reporter.NotifyUserOfProblem(new ShowAlwaysPolicy(), reportLabel, ErrorResult.Yes, "message");

			mockFactory.Verify(x => x.CreateBrowserDialog(
				It.Is<string>(url => url.Contains($"/browser/problemDialog/loader.html?level=notify&reportLabel={reportLabel}&msg=message")),
				It.IsAny<bool>(), It.IsAny<Action>()));
		}

		/// <summary>
		/// We want to automatically convert the hard-coded "Details" parameter that ErrorReport.cs passes in
		/// to the new default
		/// </summary>
		[Test]
		public void NotifyUserOfProblem_IfParamIsDetailsThenConvertedToReport()
		{
			var mockFactory = GetDefaultMockBrowserDialogFactory();
			var reporter = new HtmlErrorReporterBuilder()
				.WithTestValues()
				.BrowserDialogFactory(mockFactory.Object)
				.Build();

			// System Under Test
			reporter.NotifyUserOfProblem(new ShowAlwaysPolicy(), "Details", ErrorResult.Yes, "message");

			mockFactory.Verify(x => x.CreateBrowserDialog(
				It.Is<string>(url => url.Contains("/browser/problemDialog/loader.html?level=notify&reportLabel=Report&msg=message")),
				It.IsAny<bool>(), It.IsAny<Action>()));
		}
		#endregion


		#region CustomNotifyUserAuto tests
		/// <summary>
		/// Test the workaround for if you truly want it to say "Details"
		/// </summary>
		[Test]
		public void CustomNotifyUserAuto_IfInstanceVarIsDetailsThenStaysDetails()
		{
			var mockFactory = GetDefaultMockBrowserDialogFactory();
			var reporter = new HtmlErrorReporterBuilder()
				.WithTestValues()
				.BrowserDialogFactory(mockFactory.Object)
				.Build();

			// CustomNotifyUserAuto calls ErrorReport, so we should set it up
			ErrorReport.SetErrorReporter(reporter);

			// System Under Test
			reporter.CustomNotifyUserAuto("Details", null, null, null, "message");

			mockFactory.Verify(x => x.CreateBrowserDialog(
				It.Is<string>(url => url.Contains("/browser/problemDialog/loader.html?level=notify&reportLabel=Details&msg=message")),
				It.IsAny<bool>(), It.IsAny<Action>()));
		}

		/// <summary>
		/// Tests that you can use this function to add a secondary action button with the desired text
		/// </summary>
		[Test]
		public void CustomNotifyUserAuto_SecondaryActionButtonLabel()
		{
			var mockFactory = GetDefaultMockBrowserDialogFactory();
			var reporter = new HtmlErrorReporterBuilder()
				.WithTestValues()
				.BrowserDialogFactory(mockFactory.Object)
				.Build();

			// CustomNotifyUserAuto calls ErrorReport, so we should set it up
			ErrorReport.SetErrorReporter(reporter);

			// System Under Test
			reporter.CustomNotifyUserAuto("", "Retry", null, null, "message");

			// Verification
			mockFactory.Verify(x => x.CreateBrowserDialog(
				It.Is<string>(url => url.Contains("/browser/problemDialog/loader.html?level=notify&secondaryLabel=Retry&msg=message")),
				It.IsAny<bool>(), It.IsAny<Action>()));
		}

		[Test]
		public void CustomNotifyUserAuto_SecondaryActionAutoInvoked()
		{
			// Simulate click on a button
			var mockFactory = new Mock<IBrowserDialogFactory>();
			var mockBrowserDialog = new Mock<IBrowserDialog>();
			mockBrowserDialog.Setup(x => x.ShowDialog()).Callback(delegate {
				BrowserDialogApi.LastCloseSource = "alternate";
			});
			mockFactory.Setup(x => x.CreateBrowserDialog(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<Action>())).Returns(mockBrowserDialog.Object);

			var reporter = new HtmlErrorReporterBuilder()
				.WithTestValues()
				.BrowserDialogFactory(mockFactory.Object)
				.Build();

			// CustomNotifyUserAuto calls ErrorReport, so we should set it up
			ErrorReport.SetErrorReporter(reporter);

			_testValue = "";
			Action<Exception, string> action = delegate (Exception e, string s)
			{
				_testValue = "Retry was pressed";
			};

			// System Under Test
			reporter.CustomNotifyUserAuto("", "Retry", action, null, "message");

			// Verification
			Assert.AreEqual("Retry was pressed", _testValue);

			// Cleanup
			BrowserDialogApi.LastCloseSource = null;
			_testValue = "";
		}
		#endregion

		#region CustomNotifyUserManual tests
		[Test]
		public void CustomNotifyUserManual_WhenSecondaryActionButtonClicked_ThenSecondaryActionResultReturned()
		{
			// Simulate click on a button
			var mockFactory = new Mock<IBrowserDialogFactory>();
			var mockBrowserDialog = new Mock<IBrowserDialog>();
			mockBrowserDialog.Setup(x => x.ShowDialog()).Callback(delegate {
				BrowserDialogApi.LastCloseSource = "alternate";
			});
			mockFactory.Setup(x => x.CreateBrowserDialog(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<Action>())).Returns(mockBrowserDialog.Object);

			var reporter = new HtmlErrorReporterBuilder()
				.WithTestValues()
				.BrowserDialogFactory(mockFactory.Object)
				.Build();

			// CustomNotifyUserManual calls ErrorReport, so we should set it up
			ErrorReport.SetErrorReporter(reporter);

			// System Under Test
			var result = reporter.CustomNotifyUserManual(new ShowAlwaysPolicy(),
				"Report", ErrorResult.Yes,
				"Retry", ErrorResult.Retry,
				"message");

			// Verification
			Assert.AreEqual(ErrorResult.Retry, result);

			// Cleanup
			BrowserDialogApi.LastCloseSource = null;
		}

		[Test]
		public void CustomNotifyUserManual_WhenReportButtonClicked_ThenReportResultReturned()
		{
			// Simulate click on a button
			var mockFactory = new Mock<IBrowserDialogFactory>();
			var mockBrowserDialog = new Mock<IBrowserDialog>();
			mockBrowserDialog.Setup(x => x.ShowDialog()).Callback(delegate {
				BrowserDialogApi.LastCloseSource = "report";
			});
			mockFactory.Setup(x => x.CreateBrowserDialog(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<Action>())).Returns(mockBrowserDialog.Object);

			var reporter = new HtmlErrorReporterBuilder()
				.WithTestValues()
				.BrowserDialogFactory(mockFactory.Object)
				.Build();

			// CustomNotifyUserManual calls ErrorReport, so we should set it up
			ErrorReport.SetErrorReporter(reporter);

			// System Under Test
			var result = reporter.CustomNotifyUserManual(new ShowAlwaysPolicy(),
				"Report", ErrorResult.Yes,
				"Retry", ErrorResult.Retry,
				"message");

			// Verification
			Assert.AreEqual(ErrorResult.Yes, result);

			// Cleanup
			BrowserDialogApi.LastCloseSource = null;
		}
		
		[Test]
		public void CustomNotifyUserManual_WhenCloseButtonClicked_ThenOKReturned()
		{
			// Simulate click on a button
			var mockFactory = new Mock<IBrowserDialogFactory>();
			var mockBrowserDialog = new Mock<IBrowserDialog>();
			mockBrowserDialog.Setup(x => x.ShowDialog()).Callback(delegate {
				BrowserDialogApi.LastCloseSource = null;
			});
			mockFactory.Setup(x => x.CreateBrowserDialog(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<Action>())).Returns(mockBrowserDialog.Object);

			var reporter = new HtmlErrorReporterBuilder()
				.WithTestValues()
				.BrowserDialogFactory(mockFactory.Object)
				.Build();

			// CustomNotifyUserManual calls ErrorReport, so we should set it up
			ErrorReport.SetErrorReporter(reporter);

			BrowserDialogApi.LastCloseSource = null;

			// System Under Test
			var result = reporter.CustomNotifyUserManual(new ShowAlwaysPolicy(),
				"Report", ErrorResult.Yes,
				"Retry", ErrorResult.Retry,
				"message");

			// Verification
			Assert.AreEqual(ErrorResult.OK, result);

			// Cleanup
			BrowserDialogApi.LastCloseSource = null;
		}

		/// <summary>
		/// Tests a bug that assumed LastCloseSource would get reset automatically, but it wouldn't if the user clicked the WinForms X button to close.
		/// </summary>
		[Test]
		public void CustomNotifyUserManual_GivenPreviousNotificationWasReported_WhenUserClosesNextNotification_ThenActuallyClose()
		{
			var reporter = new HtmlErrorReporterBuilder()
				.WithTestValues()
				.BrowserDialogFactory(GetDefaultMockBrowserDialogFactory().Object)
				.Build();

			// CustomNotifyUserManual calls ErrorReport, so we should set it up
			ErrorReport.SetErrorReporter(reporter);

			// Simulate that on the prior notification, the user clicked "Report"
			BrowserDialogApi.LastCloseSource = "report";

			// System Under Test
			var result = reporter.CustomNotifyUserManual(new ShowAlwaysPolicy(),
				"Report", ErrorResult.Yes,
				"Retry", ErrorResult.Retry,
				"message");

			Assert.AreEqual(ErrorResult.OK, result);

			// Cleanup
			BrowserDialogApi.LastCloseSource = null;
		}
		#endregion
	}
}
