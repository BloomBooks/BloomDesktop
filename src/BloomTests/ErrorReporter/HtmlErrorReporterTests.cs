using System;
using System.Text;
using Bloom.Api;
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

		[NUnit.Framework.SetUp]
		public void Setup()
		{
			// doesn't make it true, but allows the methods we're testing to use the
			// error reporting paths designed for when it is.
			BloomServer.ServerIsListening = true;
		}

		[NUnit.Framework.TearDown]
		public void TearDown()
		{
			BloomServer.ServerIsListening = false;
		}

		private Mock<IReactDialogFactory> GetDefaultMockReactDialogFactory()
		{
			var mockFactory = new Mock<IReactDialogFactory>();
			var mockBrowserDialog = new Mock<IBrowserDialog>();
			mockFactory.Setup(x => x.CreateReactDialog(It.IsAny<string>(), It.IsAny<object>())).Returns(mockBrowserDialog.Object);

			return mockFactory;
		}

		#region GetMessage tests
		[Test]
		public void GetMessage_OnlyText()
		{
			var result = HtmlErrorReporter.GetMessage("message text", null);
			Assert.AreEqual("message text", result);
		}

		[Test]
		public void GetMessage_OnlyException()
		{
			var exception = new ApplicationException("fake exception");
			var result = HtmlErrorReporter.GetMessage(null, exception);
			Assert.AreEqual("fake exception", result);
		}

		[Test]
		public void GetMessage_TextAndException_ReturnsTextOnly()
		{
			var exception = new ApplicationException("fake exception");
			var result = HtmlErrorReporter.GetMessage("message text", exception);
			Assert.AreEqual("message text", result);
		}
		#endregion


		#region NotifyUserOfProblem tests
		[Test]
		public void NotifyUserOfProblem_UnsafeMessage()
		{
			var mockFactory = GetDefaultMockReactDialogFactory();
			var reporter = new HtmlErrorReporterBuilder()
				.WithTestValues()
				.BrowserDialogFactory(mockFactory.Object)
				.Build();

			// System Under Test
			reporter.NotifyUserOfProblem(new ShowAlwaysPolicy(), "", ErrorResult.Yes, "<b>Tags should not be encoded</b>");

			mockFactory.Verify(x => x.CreateReactDialog(
				It.Is<string>(b=> b == "problemReportBundle"),
				It.Is<object>(props => (string)props.GetType().GetProperty("level").GetValue(props) == ProblemLevel.kNotify &&
					(string)props.GetType().GetProperty("message").GetValue(props) == "<b>Tags should not be encoded</b>")
			));
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

			var mockFactory = GetDefaultMockReactDialogFactory();
			var reporter = new HtmlErrorReporterBuilder()
				.WithTestValues()
				.BrowserDialogFactory(mockFactory.Object)
				.Build();

			// System Under Test
			reporter.NotifyUserOfProblem(new ShowAlwaysPolicy(), "", ErrorResult.Yes, messageText);

			// Verification
			mockFactory.Verify(x => x.CreateReactDialog(
			 It.Is<string>(b => b == "problemReportBundle"),
				It.Is<object>(props => (string)props.GetType().GetProperty("level").GetValue(props) == ProblemLevel.kNotify &&
						(string)props.GetType().GetProperty("message").GetValue(props) == messageText)
				));
		}

		[TestCase("Report")]
		[TestCase("CustomReport")]
		public void NotifyUserOfProblem_ReportButton(string reportLabel)
		{
			var mockFactory = GetDefaultMockReactDialogFactory();
			var reporter = new HtmlErrorReporterBuilder()
				.WithTestValues()
				.BrowserDialogFactory(mockFactory.Object)
				.Build();

			// System Under Test
			reporter.NotifyUserOfProblem(new ShowAlwaysPolicy(), reportLabel, ErrorResult.Yes, "message");

			mockFactory.Verify(x =>
				x.CreateReactDialog(
				It.Is<string>(b => b == "problemReportBundle"),
					It.Is<object>(props => (string)props.GetType().GetProperty("level").GetValue(props) == ProblemLevel.kNotify &&
						(string)props.GetType().GetProperty("reportLabel").GetValue(props) == reportLabel &&
						(string)props.GetType().GetProperty("message").GetValue(props) == "message")
				)
			);
		}

		/// <summary>
		/// We want to automatically convert the hard-coded "Details" parameter that ErrorReport.cs passes in
		/// to the new default
		/// </summary>
		[Test]
		public void NotifyUserOfProblem_IfParamIsDetailsThenConvertedToReport()
		{
			var mockFactory = GetDefaultMockReactDialogFactory();
			var reporter = new HtmlErrorReporterBuilder()
				.WithTestValues()
				.BrowserDialogFactory(mockFactory.Object)
				.Build();

			// System Under Test
			reporter.NotifyUserOfProblem(new ShowAlwaysPolicy(), "Details", ErrorResult.Yes, "message");

			mockFactory.Verify(x => x.
				CreateReactDialog(
					It.Is<string>(b => b == "problemReportBundle"),
					It.Is<object>(props => (string)props.GetType().GetProperty("level").GetValue(props) == ProblemLevel.kNotify &&
						(string)props.GetType().GetProperty("reportLabel").GetValue(props) == "Report" &&
						(string)props.GetType().GetProperty("message").GetValue(props) == "message")
				)
			);
		}
		#endregion


		#region CustomNotifyUserAuto tests
		/// <summary>
		/// Test the workaround for if you truly want it to say "Details"
		/// </summary>
		[Test]
		public void CustomNotifyUserAuto_IfInstanceVarIsDetailsThenStaysDetails()
		{
			var mockFactory = GetDefaultMockReactDialogFactory();
			var reporter = new HtmlErrorReporterBuilder()
				.WithTestValues()
				.BrowserDialogFactory(mockFactory.Object)
				.Build();

			// CustomNotifyUserAuto calls ErrorReport, so we should set it up
			ErrorReport.SetErrorReporter(reporter);

			// System Under Test
			reporter.CustomNotifyUserAuto("Details", null, null, null, "message");

			mockFactory.Verify(x =>
				x.CreateReactDialog(
					It.Is<string>(b => b == "problemReportBundle"),
					It.Is<object>(props => (string)props.GetType().GetProperty("level").GetValue(props) == ProblemLevel.kNotify &&
						(string)props.GetType().GetProperty("reportLabel").GetValue(props) == "Details" &&
						(string)props.GetType().GetProperty("message").GetValue(props) == "message")
				)
			);
		}

		/// <summary>
		/// Tests that you can use this function to add a secondary action button with the desired text
		/// </summary>
		[Test]
		public void CustomNotifyUserAuto_SecondaryActionButtonLabel()
		{
			var mockFactory = GetDefaultMockReactDialogFactory();
			var reporter = new HtmlErrorReporterBuilder()
				.WithTestValues()
				.BrowserDialogFactory(mockFactory.Object)
				.Build();

			// CustomNotifyUserAuto calls ErrorReport, so we should set it up
			ErrorReport.SetErrorReporter(reporter);

			// System Under Test
			reporter.CustomNotifyUserAuto("", "Retry", null, null, "message");

			// Verification
			mockFactory.Verify(x => x.CreateReactDialog(
				It.Is<string>(b => b == "problemReportBundle"),
				It.Is<object>(props => (string)props.GetType().GetProperty("level").GetValue(props) == ProblemLevel.kNotify &&
					(string)props.GetType().GetProperty("secondaryLabel").GetValue(props) == "Retry" &&
					(string)props.GetType().GetProperty("message").GetValue(props) == "message")
			));
		}

		[Test]
		public void CustomNotifyUserAuto_SecondaryActionAutoInvoked()
		{
			// Simulate click on a button
			var mockFactory = new Mock<IReactDialogFactory>();
			var mockBrowserDialog = new Mock<IBrowserDialog>();
			mockBrowserDialog.SetupAllProperties();	// This is necessary for properties like CloseSource to set their values.
			mockBrowserDialog.Setup(x => x.ShowDialog()).Callback(delegate {
				mockBrowserDialog.Object.CloseSource = "closedByAlternateButton";
			});
			mockFactory.Setup(x => x.CreateReactDialog(It.IsAny<string>(), It.IsAny<object>()))
				.Returns(mockBrowserDialog.Object);

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
			_testValue = "";
		}
		#endregion

		#region CustomNotifyUserManual tests
		[Test]
		public void CustomNotifyUserManual_WhenSecondaryActionButtonClicked_ThenSecondaryActionResultReturned()
		{
			// Simulate click on a button
			var mockFactory = new Mock<IReactDialogFactory>();
			var mockBrowserDialog = new Mock<IBrowserDialog>();
			mockBrowserDialog.SetupAllProperties(); // This is necessary for properties like CloseSource to set their values.
			mockBrowserDialog.Setup(x => x.ShowDialog()).Callback(delegate
			{
				mockBrowserDialog.Object.CloseSource = "closedByAlternateButton";
			});
			mockFactory.Setup(x => x.CreateReactDialog( It.IsAny<string>(),It.IsAny<object>())).Returns(mockBrowserDialog.Object);

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
		}

		[Test]
		public void CustomNotifyUserManual_WhenReportButtonClicked_ThenReportResultReturned()
		{
			// Simulate click on a button
			var mockFactory = new Mock<IReactDialogFactory>();
			var mockBrowserDialog = new Mock<IBrowserDialog>();
			mockBrowserDialog.SetupAllProperties(); // This is necessary for properties like CloseSource to set their values.
			mockBrowserDialog.Setup(x => x.ShowDialog()).Callback(delegate
			{
				mockBrowserDialog.Object.CloseSource = "closedByReportButton";
			});
			mockFactory.Setup(x => x.CreateReactDialog( It.IsAny<string>(), It.IsAny<object>()))
				.Returns(mockBrowserDialog.Object);

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
		}

		[Test]
		public void CustomNotifyUserManual_WhenCloseButtonClicked_ThenOKReturned()
		{
			// Simulate click on a button
			var mockFactory = new Mock<IReactDialogFactory>();
			var mockBrowserDialog = new Mock<IBrowserDialog>();
			mockBrowserDialog.Setup(x => x.ShowDialog()).Callback(delegate
			{
				mockBrowserDialog.Object.CloseSource = null;
			});
			mockFactory.Setup(x => x.CreateReactDialog(It.IsAny<string>(), It.IsAny<object>()))
				.Returns(mockBrowserDialog.Object);

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
			Assert.AreEqual(ErrorResult.OK, result);
		}
		#endregion
	}
}
