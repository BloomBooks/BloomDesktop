using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.web.controllers;
using NUnit.Framework;


namespace BloomTests.web.controllers
{
	public class OrthographyConverterTests
	{
		[Test]
		public void ApplyMappingsString_MultipleMatches_LongerLengthTakesPrecendence()
		{
			var mappings = new Dictionary<string, string>()
			{
				{ "ab", "a''b''" },
				{ "abc", "a'b'c'" },
				{ "a", "a'''" }
			};

			var converter = new OrthographyConverter(mappings);
			string result = converter.ApplyMappings("abchelloworld");

			Assert.That(result, Is.Not.EqualTo("a'''bc'helloworld"));  // This would be obtained if shortest mapping takes precedence.
			Assert.That(result, Is.Not.EqualTo("a''b''chelloworld"));  // This would be obtained if first mapping takes precedence.
			Assert.That(result, Is.EqualTo("a'b'c'helloworld"));
		}

		[Test]
		public void ApplyMappingsString_MatchWithinSubstring_DoesntDoubleReplace()
		{
			var mappings = new Dictionary<string, string>()
			{
				{ "abc", "a'b'c'" },
				{ "bc", "b''c''" }
			};

			var converter = new OrthographyConverter(mappings);
			string result = converter.ApplyMappings("abchelloworld");

			Assert.That(result, Is.Not.EqualTo("a'b''c''helloworld"));	// This would be obtained if you only advance it by 1 instead of by the length of the matched key.
			Assert.That(result, Is.EqualTo("a'b'c'helloworld"));
		}

		[TestCase("asdf", null, null)]
		[TestCase("convert_th_to_eo.txt", "th", "eo")]
		[TestCase("dir/convert_th_to_en-ipa.txt", "th", "en-ipa")]
		[TestCase("dir/dir/convert_en-ipa_to_en.txt", "en-ipa", "en")]
		public void ParseSourceAndTargetFromFilenameTest(string input, string expectedSource, string expectedTarget)
		{
			Tuple<string, string> result = OrthographyConverter.ParseSourceAndTargetFromFilename(input);

			if (expectedSource == null)
			{
				Assert.That(result, Is.Null);
			}
			else
			{
				Assert.That(result, Is.Not.Null, "ParseSourceAndTargetFromFilename returned null (Invalid) filename even though filename expected to be valid.");
				Assert.That(result.Item1, Is.EqualTo(expectedSource), "Source does not match");
				Assert.That(result.Item2, Is.EqualTo(expectedTarget), "Target does not match");
			}
		}
	}
}
