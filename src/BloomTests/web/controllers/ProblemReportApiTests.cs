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
                RetiredTestServers.Retire(_server);
                _server = null;
            }
        }

        [Test]
        public void ReportHeadingHtml_GivenUnencodedHtmlInSummary_EncodesTheHtml()
        {
            bool isSummaryPreEncoded = false;
            SetupApiHandler(GetDefaultBookSelection());

            ProblemReportApi.GatherReportInfoExceptScreenshot(
                null,
                "Fake Details",
                "Fake Problem: See <a href=\"http://bloomlibrary.org\">Bloom Library</a> for help",
                isSummaryPreEncoded
            );
            var result = ApiTest.GetString(_server, "problemReport/reportHeadingHtml");

            Assert.That(
                result,
                Is.EqualTo(
                    "Fake Problem: See &lt;a href=&quot;http://bloomlibrary.org&quot;&gt;Bloom Library&lt;/a&gt; for help"
                )
            );
        }

        [Test]
        public void ReportHeadingHtml_GivenPreEncodedHtmlInSummary_ReturnsRawHtmlDirectly()
        {
            bool isSummaryPreEncoded = true;
            SetupApiHandler(GetDefaultBookSelection());

            ProblemReportApi.GatherReportInfoExceptScreenshot(
                null,
                "Fake Details",
                "Fake Problem: See <a href=\"http://bloomlibrary.org\">Bloom Library</a> for help",
                isSummaryPreEncoded
            );
            var result = ApiTest.GetString(_server, "problemReport/reportHeadingHtml");

            Assert.That(
                result,
                Is.EqualTo(
                    "Fake Problem: See <a href=\"http://bloomlibrary.org\">Bloom Library</a> for help"
                )
            );
        }

        #region Non-interactive (console / e2e) suppression -- BL-16869

        /// <summary>
        /// ShowProblemDialog is reached from every level of problem report. In a Bloom nobody is
        /// watching -- a command-line verb, or an e2e run -- it must report on stderr and return
        /// rather than putting up a modal that will never be dismissed. See BL-16869.
        /// </summary>
        [TestCase(
            true,
            false,
            TestName = "ShowProblemDialog_RunningInConsoleMode_ReportsOnStandardError"
        )]
        [TestCase(
            false,
            true,
            TestName = "ShowProblemDialog_RunningE2eTests_ReportsOnStandardError"
        )]
        public void ShowProblemDialog_NonInteractive_ReportsOnStandardErrorAndReturns(
            bool consoleMode,
            bool e2eMode
        )
        {
            var originalConsoleMode = Program.RunningInConsoleMode;
            var originalE2eMode = Program.RunningE2eTests;
            var originalStandardError = Console.Error;
            var capturedStandardError = new System.IO.StringWriter();
            try
            {
                Program.RunningInConsoleMode = consoleMode;
                Program.RunningE2eTests = e2eMode;
                Console.SetError(capturedStandardError);

                // System Under Test. If the guard is missing this would try to build a dialog;
                // note that we deliberately pass no control and have no server running, so
                // returning quietly is only possible via the guard.
                ProblemReportApi.ShowProblemDialog(
                    null,
                    new ApplicationException("fake exception"),
                    "Fake Details",
                    "nonfatal",
                    "Fake Summary"
                );
            }
            finally
            {
                Console.SetError(originalStandardError);
                Program.RunningInConsoleMode = originalConsoleMode;
                Program.RunningE2eTests = originalE2eMode;
            }

            var standardError = capturedStandardError.ToString();
            Assert.That(
                standardError,
                Does.Contain("Fake Summary"),
                "The problem must be reported on stderr when we cannot show a dialog."
            );
            Assert.That(
                standardError,
                Does.Contain("Fake Details"),
                "The detailed message must reach stderr too."
            );
            Assert.That(
                standardError,
                Does.Contain("fake exception"),
                "The exception details must reach stderr too."
            );
            Assert.That(
                standardError,
                Does.Contain("nonfatal"),
                "The level of the problem should be evident in the stderr report."
            );
        }

        /// <summary>
        /// The guard must not change anything for a normal, interactive Bloom.
        /// </summary>
        [Test]
        public void ReportProblemWithoutUiIfNonInteractive_Interactive_DoesNothing()
        {
            var originalConsoleMode = Program.RunningInConsoleMode;
            var originalE2eMode = Program.RunningE2eTests;
            var originalStandardError = Console.Error;
            var capturedStandardError = new System.IO.StringWriter();
            bool handled;
            try
            {
                Program.RunningInConsoleMode = false;
                Program.RunningE2eTests = false;
                Console.SetError(capturedStandardError);

                handled = ProblemReportApi.ReportProblemWithoutUiIfNonInteractive(
                    "nonfatal",
                    new ApplicationException("fake exception"),
                    "Fake Summary"
                );
            }
            finally
            {
                Console.SetError(originalStandardError);
                Program.RunningInConsoleMode = originalConsoleMode;
                Program.RunningE2eTests = originalE2eMode;
            }

            Assert.That(
                handled,
                Is.False,
                "An interactive Bloom must go on to show its normal problem dialog."
            );
            Assert.That(
                capturedStandardError.ToString(),
                Is.Empty,
                "An interactive Bloom should not be writing problem reports to stderr."
            );
        }

        #endregion
    }
}
