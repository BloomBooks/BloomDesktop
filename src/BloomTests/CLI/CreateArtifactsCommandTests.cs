using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.CLI;
using NUnit.Framework;

namespace BloomTests.CLI
{
	[TestFixture]
	public class CreateArtifactsCommandTests
	{
		[Test]
		public void CreateArtifactsExitCode_GetErrorsFromExitCode_ExitCode0_ReturnsSuccess()
		{
			int exitCode = 0;
			var errors = CreateArtifactsCommand.GetErrorsFromExitCode(exitCode);

			Assert.That(errors.Count, Is.EqualTo(0));
		}

		[Test]
		public void CreateArtifactsExitCode_GetErrorsFromExitCode_UnhandledException_Returns1Error()
		{
			int exitCode = 1;
			var errors = CreateArtifactsCommand.GetErrorsFromExitCode(exitCode);

			CollectionAssert.AreEquivalent(new string[] { "UnhandledException" }, errors);
		}

		[Test]
		public void CreateArtifactsExitCode_GetErrorsFromExitCode_BookHtmlNotFound_Returns1Error()
		{
			int exitCode = 2;
			var errors = CreateArtifactsCommand.GetErrorsFromExitCode(exitCode);

			CollectionAssert.AreEquivalent(new string[] { "BookHtmlNotFound" }, errors);
		}

		[Test]
		public void CreateArtifactsExitCode_GetErrorsFromExitCode_EpubError_Returns1Error()
		{
			int exitCode = 4;
			var errors = CreateArtifactsCommand.GetErrorsFromExitCode(exitCode);

			CollectionAssert.AreEquivalent(new string[] { "EpubException" }, errors);
		}

		[Test]
		public void CreateArtifactsExitCode_GetErrorsFromExitCode_MultipleFlags_ReturnsBoth()
		{
			int exitCode = 0;

			// bitwise arithmetic to set the first few flags
			int numFlags = 2;
			for (int i = 0; i < numFlags; ++i)
			{
				exitCode |= 1 << i;
			}

			var errors = CreateArtifactsCommand.GetErrorsFromExitCode(exitCode);

			CollectionAssert.AreEquivalent(new string[] { "UnhandledException", "BookHtmlNotFound" }, errors);
		}

		[Test]
		public void CreateArtifactsExitCode_GetErrorsFromExitCode_UnknownFlag_ReturnsUnknown()
		{
			int exitCode = 1 << 20;
			var errors = CreateArtifactsCommand.GetErrorsFromExitCode(exitCode);

			CollectionAssert.AreEqual(new string[] { "Unknown" }, errors);
		}

		[Test]
		public void CreateArtifactsExitCode_GetErrorsFromExitCode_BigNumber_AddsUnknown()
		{
			int exitCode = -532462766;
			var errors = CreateArtifactsCommand.GetErrorsFromExitCode(exitCode);

			Assert.That(errors.Contains("Unknown"), Is.True);
		}
	}
}
