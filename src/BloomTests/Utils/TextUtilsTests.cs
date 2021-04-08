using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Utils;
using NUnit.Framework;

namespace BloomTests.Utils
{
	[TestFixture]
	public class TextUtilsTests
	{
		[TestCase("John 3:16 (NIV)")]
		public void TrimEndNewlines_ValidInput_NoChangeInOutput(string input)
		{
			TrimEndNewlinesRunner(input, input);
		}

		[TestCase("John 3:16 (NIV)\n\n\n", "John 3:16 (NIV)")]
		[TestCase("John 3:16 (NIV)\r\r\r", "John 3:16 (NIV)")]
		[TestCase("John 3:16 (NIV)\r\n\r\n\r\n", "John 3:16 (NIV)")]
		public void TrimEndNewlines_InvalidInput_InvalidCharsRemoved(string input, string expected)
		{
			TrimEndNewlinesRunner(input, expected);
		}

		private void TrimEndNewlinesRunner(string input, string expected)
		{
			string observed = Bloom.Utils.TextUtils.TrimEndNewlines(input);
			Assert.That(observed.Equals(expected, StringComparison.Ordinal), $"{observed} does not equal {expected}");
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("a")]
		[TestCase("<b></b>")]
		[TestCase("http://www.example.org?q1=v1#id2")]
		[TestCase("%")]
		public void EscapeForWinForms_GivenNoAmpersandsInMnemonicText_ThenUnchanged(string input)
		{
			var result = TextUtils.EscapeForWinForms(input, useMnemonic: true);
			Assert.That(result, Is.EqualTo(input));
		}

		[Test]
		public void EscapeForWinForms_GivenAmpersandsInMnemonicText_ThenTheyBecomeDoubleAmp()
		{
			var result = TextUtils.EscapeForWinForms("A&B", useMnemonic: true);
			Assert.That(result, Is.EqualTo("A&&B"));
		}

		[TestCase("A&B")]
		[TestCase(null)]
		[TestCase("")]
		[TestCase("a")]
		[TestCase("<b></b>")]
		[TestCase("http://www.example.org?q1=v1#id2")]
		[TestCase("%")]
		public void EscapeForWinForms_GivenNonMnemonicText_ThenUnchanged(string input)
		{
			var result = TextUtils.EscapeForWinForms(input, useMnemonic: false);
			Assert.That(result, Is.EqualTo(input));
		}
	}
}
