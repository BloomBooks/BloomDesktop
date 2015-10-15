// Copyright (c) 2015 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System.Xml;
using Bloom;
using NUnit.Framework;

namespace BloomTests
{
	[TestFixture]
	public class ElementProxyTests
	{

		/* Note, the whole purpose of the ElementProxy class is to work around the difficulty of producing
			GeckoHtmlElements in unit tests. So we're not going to test those code paths here, either, sigh. */


		[Test]
		public void GetString_XmlElementHasAttribute()
		{
			Assert.AreEqual("foo", MakeElement("<div id='foo'/>").GetAttribute("id"));
		}
		[Test]
		public void GetString_XmlElementMissingAttribute_GivesEmptyString()
		{
			Assert.AreEqual("", MakeElement("<div id='foo'/>").GetAttribute("blah"));
		}

		[Test]
		public void SetString_CanReturnIt()
		{
			var e = MakeElement("<div id='foo'/>");
			e.SetAttribute("id", "blah");
            Assert.AreEqual("blah", e.GetAttribute("id"));

			MakeElement("<div/>");
			e.SetAttribute("id", "blah");
			Assert.AreEqual("blah", e.GetAttribute("id"));
		}

		[Test]
		public void Class_ReturnsSameCase()
		{
			Assert.AreEqual("Img", MakeElement("<Img id='foo'/>").Name,"should be same case because that's what XmlElement.Name does.");
		}

		private ElementProxy MakeElement(string xml)
		{
			var dom = new XmlDocument();
			dom.LoadXml(xml);
			return new ElementProxy(dom.DocumentElement);
		}
	}
}
