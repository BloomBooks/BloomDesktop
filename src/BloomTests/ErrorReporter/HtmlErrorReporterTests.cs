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
            // This deliberately exercises the obsolete legacy overload.
#pragma warning disable 618
            reporter.NotifyUserOfProblem(
                new ShowAlwaysPolicy(),
                "Details",
                ErrorResult.Yes,
                "message"
            );
#pragma warning restore 618

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

        #region Non-interactive (console / e2e) suppression -- BL-16869

        /// <summary>
        /// In a command-line verb (e.g. the child Bloom that `bloom upload` starts) there is nobody
        /// to dismiss a dialog, so showing one blocks the process forever. The problem must be
        /// reported on stderr instead. See BL-16869.
        /// </summary>
        [TestCase(true, false, TestName = "NotifyUserOfProblem_RunningInConsoleMode_ShowsNoDialog")]
        [TestCase(false, true, TestName = "NotifyUserOfProblem_RunningE2eTests_ShowsNoDialog")]
        public void NotifyUserOfProblem_NonInteractive_ShowsNoDialogAndWritesToStandardError(
            bool consoleMode,
            bool e2eMode
        )
        {
            var mockFactory = GetDefaultMockReactDialogFactory();
            var reporter = new HtmlErrorReporterBuilder()
                .WithTestValues()
                .BrowserDialogFactory(mockFactory.Object)
                .Build();

            // Sanity check: with nothing suppressing it, this same call does show a dialog.
            reporter.NotifyUserOfProblem(new ShowAlwaysPolicy(), null, "a problem");
            mockFactory.Verify(
                x => x.CreateReactDialog(It.IsAny<string>(), It.IsAny<object>()),
                Times.Once,
                "Setup failed: an interactive Bloom should have shown the notify dialog, so this test could not tell suppression from a dialog that never happens."
            );
            mockFactory.Invocations.Clear();

            var originalConsoleMode = Bloom.Program.RunningInConsoleMode;
            var originalE2eMode = Bloom.Program.RunningE2eTests;
            var originalStandardError = Console.Error;
            var capturedStandardError = new System.IO.StringWriter();
            try
            {
                Bloom.Program.RunningInConsoleMode = consoleMode;
                Bloom.Program.RunningE2eTests = e2eMode;
                Console.SetError(capturedStandardError);

                // System Under Test
                reporter.NotifyUserOfProblem(
                    new ShowAlwaysPolicy(),
                    new ApplicationException("fake exception"),
                    "a problem"
                );
            }
            finally
            {
                Console.SetError(originalStandardError);
                Bloom.Program.RunningInConsoleMode = originalConsoleMode;
                Bloom.Program.RunningE2eTests = originalE2eMode;
            }

            mockFactory.Verify(
                x => x.CreateReactDialog(It.IsAny<string>(), It.IsAny<object>()),
                Times.Never,
                "A modal dialog here would block a non-interactive Bloom forever (BL-16869)."
            );
            var standardError = capturedStandardError.ToString();
            Assert.That(
                standardError,
                Does.Contain("a problem"),
                "The problem must still be reported on stderr, or it vanishes silently."
            );
            Assert.That(
                standardError,
                Does.Contain("fake exception"),
                "The exception details must reach stderr too."
            );
        }

        #endregion
    }
}
