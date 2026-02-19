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
            mockFactory
                .Setup(x => x.CreateReactDialog(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(mockBrowserDialog.Object);

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
            reporter.NotifyUserOfProblem(
                new ShowAlwaysPolicy(),
                null,
                "<b>Tags should not be encoded</b>"
            );

            mockFactory.Verify(x =>
                x.CreateReactDialog(
                    It.Is<string>(b => b == "problemReportBundle"),
                    It.Is<object>(props =>
                        (string)props.GetType().GetProperty("level").GetValue(props)
                            == ProblemLevel.kNotify
                        && (string)props.GetType().GetProperty("message").GetValue(props)
                            == "<b>Tags should not be encoded</b>"
                    )
                )
            );
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
            reporter.NotifyUserOfProblem(new ShowAlwaysPolicy(), null, messageText);

            // Verification
            mockFactory.Verify(x =>
                x.CreateReactDialog(
                    It.Is<string>(b => b == "problemReportBundle"),
                    It.Is<object>(props =>
                        (string)props.GetType().GetProperty("level").GetValue(props)
                            == ProblemLevel.kNotify
                        && (string)props.GetType().GetProperty("message").GetValue(props)
                            == messageText
                    )
                )
            );
        }

        /// <summary>
        /// Tests that when you use NotifyUserOfProblem with the default parameters and an exception is passed in,
        /// the correct report button label is generated (notably, not null or "").
        /// </summary>
        [Test]
        public void NotifyUserOfProblem_ExceptionProvided_UsesDefaultReportLabel()
        {
            var mockFactory = GetDefaultMockReactDialogFactory();
            var reporter = new HtmlErrorReporterBuilder()
                .WithTestValues()
                .BrowserDialogFactory(mockFactory.Object)
                .Build();

            // System Under Test
            reporter.NotifyUserOfProblem(
                new ShowAlwaysPolicy(),
                new ApplicationException("Fake exception"),
                "message"
            );

            mockFactory.Verify(x =>
                x.CreateReactDialog(
                    It.Is<string>(b => b == "problemReportBundle"),
                    It.Is<object>(props =>
                        (string)props.GetType().GetProperty("level").GetValue(props)
                            == ProblemLevel.kNotify
                        && (string)props.GetType().GetProperty("reportLabel").GetValue(props)
                            == "Report"
                    )
                )
            );
        }

        /// <summary>
        /// Tests that when you use NotifyUserOfProblem with the default parameters, when you don't pass in an exception,
        /// no report button is generated (notably, the label should be null or "").
        /// </summary>
        [Test]
        public void NotifyUserOfProblem_ExceptionNotProvided_EmptyReportLabel()
        {
            var mockFactory = GetDefaultMockReactDialogFactory();
            var reporter = new HtmlErrorReporterBuilder()
                .WithTestValues()
                .BrowserDialogFactory(mockFactory.Object)
                .Build();

            // System Under Test
            reporter.NotifyUserOfProblem(new ShowAlwaysPolicy(), null, "message");

            mockFactory.Verify(x =>
                x.CreateReactDialog(
                    It.Is<string>(b => b == "problemReportBundle"),
                    It.Is<object>(props =>
                        (string)props.GetType().GetProperty("level").GetValue(props)
                            == ProblemLevel.kNotify
                        && String.IsNullOrEmpty(
                            (string)props.GetType().GetProperty("reportLabel").GetValue(props)
                        )
                    )
                )
            );
        }
        #endregion

        #region Legacy NotifyUserOfProblem tests
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
            reporter.NotifyUserOfProblem(
                new ShowAlwaysPolicy(),
                "Details",
                ErrorResult.Yes,
                "message"
            );

            mockFactory.Verify(x =>
                x.CreateReactDialog(
                    It.Is<string>(b => b == "problemReportBundle"),
                    It.Is<object>(props =>
                        (string)props.GetType().GetProperty("level").GetValue(props)
                            == ProblemLevel.kNotify
                        && (string)props.GetType().GetProperty("reportLabel").GetValue(props)
                            == "Report"
                    )
                )
            );
        }
        #endregion


        #region custom NotifyUserOfProblem overloads tests
        [Test]
        public void NotifyUserOfProblem_ShouldHideReportSetToDefault_ReportButtonIfNonNullException()
        {
            var mockFactory = GetDefaultMockReactDialogFactory();
            var reporter = new HtmlErrorReporterBuilder()
                .WithTestValues()
                .BrowserDialogFactory(mockFactory.Object)
                .Build();

            // System Under Test
            reporter.NotifyUserOfProblem(
                "message",
                new ApplicationException("fake exception"),
                new NotifyUserOfProblemSettings(),
                new ShowAlwaysPolicy()
            );

            // Verification
            mockFactory.Verify(x =>
                x.CreateReactDialog(
                    It.Is<string>(b => b == "problemReportBundle"),
                    It.Is<object>(props =>
                        (string)props.GetType().GetProperty("level").GetValue(props)
                            == ProblemLevel.kNotify
                        && (string)props.GetType().GetProperty("reportLabel").GetValue(props)
                            == "Report"
                    )
                )
            );
        }

        [Test]
        public void NotifyUserOfProblem_ShouldHideReportSetToDefault_ReportButtonHiddenIfNullException()
        {
            var mockFactory = GetDefaultMockReactDialogFactory();
            var reporter = new HtmlErrorReporterBuilder()
                .WithTestValues()
                .BrowserDialogFactory(mockFactory.Object)
                .Build();

            // System Under Test
            reporter.NotifyUserOfProblem(
                "message",
                new NotifyUserOfProblemSettings(),
                new ShowAlwaysPolicy()
            );

            // Verification
            mockFactory.Verify(x =>
                x.CreateReactDialog(
                    It.Is<string>(b => b == "problemReportBundle"),
                    It.Is<object>(props =>
                        (string)props.GetType().GetProperty("level").GetValue(props)
                            == ProblemLevel.kNotify
                        && (string)props.GetType().GetProperty("reportLabel").GetValue(props) == ""
                    )
                )
            );
        }

        [TestCase(null)] // Tests that the report button label works even if exception is null
        [TestCase("fake exception")]
        public void NotifyUserOfProblem_ShouldShowReportSetToTrue_ReportButtonPresent(
            string exceptionMessage
        )
        {
            var mockFactory = GetDefaultMockReactDialogFactory();
            var reporter = new HtmlErrorReporterBuilder()
                .WithTestValues()
                .BrowserDialogFactory(mockFactory.Object)
                .Build();

            var exceptionOrNull =
                exceptionMessage != null ? new ApplicationException(exceptionMessage) : null;

            // System Under Test
            reporter.NotifyUserOfProblem(
                "message",
                exceptionOrNull,
                new NotifyUserOfProblemSettings(AllowSendReport.Allow),
                new ShowAlwaysPolicy()
            );

            // Verification
            mockFactory.Verify(x =>
                x.CreateReactDialog(
                    It.Is<string>(b => b == "problemReportBundle"),
                    It.Is<object>(props =>
                        (string)props.GetType().GetProperty("level").GetValue(props)
                            == ProblemLevel.kNotify
                        && (string)props.GetType().GetProperty("reportLabel").GetValue(props)
                            == "Report"
                    )
                )
            );
        }

        [TestCase(null)] // Tests that the report button label works even if exception is null)
        [TestCase("fake exception")]
        public void NotifyUserOfProblem_ShouldShowReportSetToFalse_ReportButtonDisabled(
            string exceptionMessage
        )
        {
            var mockFactory = GetDefaultMockReactDialogFactory();
            var reporter = new HtmlErrorReporterBuilder()
                .WithTestValues()
                .BrowserDialogFactory(mockFactory.Object)
                .Build();

            var exceptionOrNull =
                exceptionMessage != null ? new ApplicationException(exceptionMessage) : null;

            // System Under Test
            reporter.NotifyUserOfProblem(
                "message",
                exceptionOrNull,
                new NotifyUserOfProblemSettings(AllowSendReport.Disallow),
                new ShowAlwaysPolicy()
            );

            // Verification
            mockFactory.Verify(x =>
                x.CreateReactDialog(
                    It.Is<string>(b => b == "problemReportBundle"),
                    It.Is<object>(props =>
                        (string)props.GetType().GetProperty("level").GetValue(props)
                            == ProblemLevel.kNotify
                        && (string)props.GetType().GetProperty("reportLabel").GetValue(props) == ""
                    )
                )
            );
        }

        /// <summary>
        /// Tests that you can use this function to add a secondary action button with the desired text
        /// </summary>
        [Test]
        public void NotifyUserOfProblem_SecondaryActionButtonLabel()
        {
            var mockFactory = GetDefaultMockReactDialogFactory();
            var reporter = new HtmlErrorReporterBuilder()
                .WithTestValues()
                .BrowserDialogFactory(mockFactory.Object)
                .Build();

            // System Under Test
            reporter.NotifyUserOfProblem(
                "message",
                new NotifyUserOfProblemSettings("Retry", null),
                new ShowAlwaysPolicy()
            );

            // Verification
            mockFactory.Verify(x =>
                x.CreateReactDialog(
                    It.Is<string>(b => b == "problemReportBundle"),
                    It.Is<object>(props =>
                        (string)props.GetType().GetProperty("level").GetValue(props)
                            == ProblemLevel.kNotify
                        && (string)props.GetType().GetProperty("secondaryLabel").GetValue(props)
                            == "Retry"
                    )
                )
            );
        }

        [Test]
        public void NotifyUserOfProblem_SecondaryActionAutoInvoked()
        {
            // Simulate click on a button
            var mockFactory = new Mock<IReactDialogFactory>();
            var mockBrowserDialog = new Mock<IBrowserDialog>();
            mockBrowserDialog.SetupAllProperties(); // This is necessary for properties like CloseSource to set their values.
            mockBrowserDialog
                .Setup(x => x.ShowDialog())
                .Callback(
                    delegate
                    {
                        mockBrowserDialog.Object.CloseSource = "closedByAlternateButton";
                    }
                );
            mockFactory
                .Setup(x => x.CreateReactDialog(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(mockBrowserDialog.Object);

            var reporter = new HtmlErrorReporterBuilder()
                .WithTestValues()
                .BrowserDialogFactory(mockFactory.Object)
                .Build();

            _testValue = "";
            Action<string, Exception> action = delegate(string s, Exception e)
            {
                _testValue = "Retry was pressed";
            };

            try
            {
                // System Under Test
                reporter.NotifyUserOfProblem(
                    "message",
                    new NotifyUserOfProblemSettings("Retry", action),
                    new ShowAlwaysPolicy()
                );

                // Verification
                Assert.AreEqual("Retry was pressed", _testValue);
            }
            finally
            {
                // Cleanup
                _testValue = "";
            }
        }
        #endregion

        #region Integration Tests with BloomErrorReport
        [Test]
        public void IntegratationTestWithErrorReportUtils()
        {
            var mockFactory = GetDefaultMockReactDialogFactory();
            var reporter = new HtmlErrorReporterBuilder()
                .WithTestValues()
                .BrowserDialogFactory(mockFactory.Object)
                .Build();

            var originalErrorReporter = ErrorReport.GetErrorReporter();
            ErrorReport.SetErrorReporter(reporter);

            try
            {
                // System Under Test
                BloomErrorReport.NotifyUserOfProblem(
                    "message",
                    new ApplicationException("fake exception"),
                    new NotifyUserOfProblemSettings("Retry", ErrorReportUtils.TestAction),
                    new ShowAlwaysPolicy()
                );

                // Verification
                mockFactory.Verify(x =>
                    x.CreateReactDialog(
                        It.Is<string>(b => b == "problemReportBundle"),
                        It.Is<object>(props =>
                            (string)props.GetType().GetProperty("level").GetValue(props)
                                == ProblemLevel.kNotify
                            && (string)props.GetType().GetProperty("reportLabel").GetValue(props)
                                == "Report"
                            && (string)props.GetType().GetProperty("secondaryLabel").GetValue(props)
                                == "Retry"
                            && (string)props.GetType().GetProperty("message").GetValue(props)
                                == "message"
                        )
                    )
                );
            }
            finally
            {
                ErrorReport.SetErrorReporter(originalErrorReporter);
            }
        }
        #endregion
    }
}
