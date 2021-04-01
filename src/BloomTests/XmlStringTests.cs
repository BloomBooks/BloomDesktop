using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bloom;
using NUnit.Framework;


namespace BloomTests
{
	[TestFixture]
	class XmlStringTests
	{
		[TestCase("hello world")]
		[TestCase("AT&amp;T")]
		[TestCase("<b>Bold Text</b>")]
		[TestCase("")]
		[TestCase(null)]
		public void FromXml_GivenXmlInput_ThenXmlPropertyReturnsInputUnchanged(string xmlInput)
		{
			var result = XmlString.FromXml(xmlInput).Xml;

			Assert.That(result, Is.EqualTo(xmlInput));
		}

		[TestCase("a", "a")]
		[TestCase("AT&T", "AT&amp;T")]
		[TestCase("1 < 2", "1 &lt; 2")]
		public void FromUnencoded_GivenText_ThenEncodesText(string unencodedInput, string expectedXml)
		{
			var result = XmlString.FromUnencoded(unencodedInput).Xml;

			Assert.That(result, Is.EqualTo(expectedXml));
		}

		[Test]
		public void Empty_ReturnsEmptyString()
		{
			var result = XmlString.Empty;

			Assert.That(result.Xml, Is.EqualTo(""));
			Assert.That(result.Unencoded, Is.EqualTo(""));
		}

		[TestCase("Copyright John &amp; Jane Doe", "Copyright John & Jane Doe")]
		public void UnencodedProperty_GivenEncodedChars_DecodesXml(string xmlInput, string expectedUnencoded)
		{
			var result = XmlString.FromXml(xmlInput).Unencoded;

			Assert.That(result, Is.EqualTo(expectedUnencoded));
		}

		[TestCase("")]
		[TestCase(null)]
		public void IsNullOrEmpty_GivenNullOrEmpty_ReturnsTrue(string xmlInput)
		{
			var xmlStringObj = XmlString.FromXml(xmlInput);

			var result = XmlString.IsNullOrEmpty(xmlStringObj);

			Assert.That(result, Is.True);
		}

		[TestCase("a")]
		public void IsNullOrEmpty_GivenNonEmpty_ReturnsFalse(string xmlInput)
		{
			var xmlStringObj = XmlString.FromXml(xmlInput);

			var result = XmlString.IsNullOrEmpty(xmlStringObj);

			Assert.That(result, Is.False);
		}

		[Test]
		public void OperatorEquals_GivenNullWrapperandNullXml_AreEquivalent()
		{
			bool result = (null == XmlString.FromXml(null));

			Assert.That(result, Is.EqualTo(true));
		}

		[Test]
		public void OperatorEquals_GivenNullXmlAndNullWrapper_AreEquivalent()
		{
			// Need to test both orders to make sure it's actually commutative
			bool result = (XmlString.FromXml(null) == null);

			Assert.That(result, Is.EqualTo(true));
		}
	}
}
