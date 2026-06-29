using System.Collections.Generic;
using Bloom.Publish.Rab;
using NUnit.Framework;

namespace BloomTests.Publish.Rab
{
    /// <summary>
    /// Tests for the message shown when Reading App Builder runs but produces no APK. Instead of a
    /// generic "no APK was found" message, Bloom surfaces RAB's own diagnostics (BL-16467).
    /// </summary>
    [TestFixture]
    public class RabBuildFailureMessageTests
    {
        // The RAB output captured for the book in BL-16467: a missing font plus RAB's pre-build
        // requirement checklist, with no APK produced.
        private static readonly string[] kRealWorldRabOutput =
        {
            "READING APP BUILDER",
            "Version 14.0",
            "Load App Project:",
            "Font:          Charis SIL Compact",
            "Font not found: Charis SIL Compact",
            "Build App:",
            "Message_Before_Building",
            " - Message_Build_Add_Font",
            " - Message_Build_Enable_Interface_Language",
            " - Message_Build_Valid_App_Builder_Folder",
        };

        [Test]
        public void DescribeMissingApkFailure_SurfacesRabsOwnDiagnostics()
        {
            var message = RabProjectService.DescribeMissingApkFailure(kRealWorldRabOutput);

            // It should name the real problems RAB reported...
            Assert.That(message, Does.Contain("Font not found: Charis SIL Compact"));
            Assert.That(message, Does.Contain("Message_Build_Add_Font"));
            Assert.That(message, Does.Contain("Message_Build_Enable_Interface_Language"));
            // ...and not the old, useless "searched roots / candidates" dump.
            Assert.That(message, Does.Not.Contain("Searched roots"));
            Assert.That(message, Does.Not.Contain("APK candidates"));
            // Sanity: ordinary progress lines that are not problems are left out.
            Assert.That(message, Does.Not.Contain("READING APP BUILDER"));
        }

        [Test]
        public void DescribeMissingApkFailure_IncludesGradleFailureLines()
        {
            var output = new List<string>
            {
                "> Task :processReleaseResources",
                "FAILURE: Build failed with an exception.",
                "error: resource not found",
                "BUILD FAILED in 12s",
            };

            var message = RabProjectService.DescribeMissingApkFailure(output);

            Assert.That(message, Does.Contain("FAILURE: Build failed with an exception."));
            Assert.That(message, Does.Contain("error: resource not found"));
            Assert.That(message, Does.Contain("BUILD FAILED in 12s"));
        }

        [Test]
        public void DescribeMissingApkFailure_NoRecognizableProblem_FallsBackToTail()
        {
            // None of these lines match a known problem marker, so the tail should be shown rather
            // than nothing.
            var output = new List<string> { "Step one done", "Step two done", "Step three done" };

            var message = RabProjectService.DescribeMissingApkFailure(output);

            Assert.That(message, Does.Contain("Step three done"));
            Assert.That(
                message,
                Does.Not.Contain("did not report a reason"),
                "Falling back to the tail still gives the user lines to look at."
            );
        }

        [Test]
        public void DescribeMissingApkFailure_NoOutput_StillGivesAClearMessage()
        {
            var message = RabProjectService.DescribeMissingApkFailure(new List<string>());

            Assert.That(message, Does.Contain("without producing an Android app"));
            Assert.That(message, Does.Contain("did not report a reason"));
        }

        [Test]
        public void DescribeFailedRabBuild_KeepsExitSummaryAndSurfacesRabsOwnDiagnostics()
        {
            // When RAB exits non-zero, the captured output usually explains the failure; both the
            // exit-code summary and RAB's diagnostics should appear (BL-16467).
            const string exitSummary = "cmd.exe exited with code 1.";

            var message = RabProjectService.DescribeFailedRabBuild(
                exitSummary,
                kRealWorldRabOutput
            );

            // The exit-code summary is preserved...
            Assert.That(message, Does.Contain(exitSummary));
            // ...and RAB's own diagnostics are surfaced alongside it.
            Assert.That(message, Does.Contain("Font not found: Charis SIL Compact"));
            Assert.That(message, Does.Contain("Message_Build_Add_Font"));
            // Sanity: ordinary progress lines that are not problems are still left out.
            Assert.That(message, Does.Not.Contain("READING APP BUILDER"));
        }

        [Test]
        public void DescribeFailedRabBuild_NoNotableOutput_ReturnsJustTheExitSummary()
        {
            // With nothing actionable in the output, the bare exit-code summary is the best we can
            // do; we should not pad it with a "did not report a reason" sentence (that phrasing is
            // for the ran-cleanly-but-no-APK case).
            const string exitSummary = "cmd.exe exited with code 1.";

            var message = RabProjectService.DescribeFailedRabBuild(exitSummary, new List<string>());

            Assert.That(message, Is.EqualTo(exitSummary));
        }
    }
}
