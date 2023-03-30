using Bloom;
using Bloom.Api;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BloomTests
{
	[TestFixture]
	class ExtensionsTests
	{
		[Test]
		public static void ToLocalhost_GivenALocalhostUrl_ReturnsUnchanged()
		{
			// Setup
			string input = $"http://localhost:{BloomServer.portForHttp}/bloom/C%3A/Directory/filename.txt";

			// System under test
			string result = input.ToLocalhost();

			// Verification
			Assert.That(result, Is.EqualTo(input));
		}

		[Test]
		public static void ToLocalhost_GivenSimpleFilename_ConvertsToUrl()
		{
			// Setup
			string fileName = $@"C:\Directory\Book Title\Book Title.htm";

			// System under test
			string result = fileName.ToLocalhost();

			// Verification
			Assert.That(result, Is.EqualTo($@"http://localhost:{BloomServer.portForHttp}/bloom/C%3A/Directory/Book%20Title/Book%20Title.htm"));
		}

		[TestCase("Guitar #14", "Guitar%20%2314")]	// Test for BL-8652.
		[TestCase("a %+", "a%20%25%2B")]	// A test with multiple punctuation chars that gets past some earlier implementations
		// Test every punctuation on a standard US QWERTY keyboard that is allowed in filenames.
		[TestCase("a`", "a%60")]
		[TestCase("a~", "a~")]
		[TestCase("C#", "C%23")]
		[TestCase("Yahoo!", "Yahoo%21")]
		[TestCase("@user", "%40user")]
		[TestCase("$100", "%24100")]
		[TestCase("100%", "100%25")]
		[TestCase("a^", "a%5E")]
		[TestCase("AT&T", "AT%26T")]
		[TestCase("(parens)", "%28parens%29")]
		[TestCase("A-Z", "A-Z")]
		[TestCase("_underscore", "_underscore")]
		[TestCase("1+1=2", "1%2B1%3D2")]
		[TestCase("[brackets]", "%5Bbrackets%5D")]
		[TestCase("{braces}", "%7Bbraces%7D")]
		[TestCase("A;", "A%3B")]
		[TestCase("A'", "A%27")]
		[TestCase("A,", "A%2C")]
		[TestCase("A.", "A.")]
		public static void ToLocalhost_BloomPubPreviewBookTitleWithPunc_GeneratesWellFormedUrl(string bookTitle, string expectedEscapedTitle)
		{
			// Setup
			string fileName = $@"C:\PathToTemp\PlaceForStagingBook\{bookTitle}\meta.json";

			// System under test
			string result = fileName.ToLocalhost();

			// Verification
			string expectedResult = $@"http://localhost:{BloomServer.portForHttp}/bloom/C%3A/PathToTemp/PlaceForStagingBook/{expectedEscapedTitle}/meta.json";
			Assert.That(result, Is.EqualTo(expectedResult));
		}

		[Test]
		public static void MapUnevenLists_HandlesEmptyList()
		{
			var input1 = new List<string>(new[] { "A1", "A2" });
			var input2 = new List<string>();
			var input3 = new List<string>(new[] { "C1" });
			var mainInput = new[] {input1, input2, input3};
			var result = Extensions.MapUnevenLists(mainInput).ToArray();
			Assert.That(result, Has.Length.EqualTo(2));
			var r1 = result[0]; // First element from each input
			Assert.That(r1[0], Is.EqualTo("A1"));
			Assert.That(r1[1], Is.Null);
			Assert.That(r1[2], Is.EqualTo("C1"));

			var r2 = result[1]; // second element from each input
			Assert.That(r2[0], Is.EqualTo("A2"));
			Assert.That(r2[1], Is.Null);
			Assert.That(r2[2], Is.Null);
		}
	}
}
