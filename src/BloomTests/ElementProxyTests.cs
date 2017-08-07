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

		[Test]
		public void EqualsNull_ReturnsFalse()
		{
			var elementProxy = MakeElement("<div id='foo'/>");
			Assert.That(elementProxy == null, Is.False);
			Assert.That(elementProxy.Equals(null), Is.False);

			ElementProxy proxy2 = null;
			// proxy2.Equals(elementProxy) has to fail.
			Assert.That(proxy2 == elementProxy, Is.False);
		}

		[Test]
		public void EqualsSelf_ReturnsTrue()
		{
			var elementProxy = MakeElement("<div id='foo'/>");
			Assert.That(elementProxy == elementProxy, Is.True);
			Assert.That(elementProxy.Equals(elementProxy), Is.True);
		}

		[Test]
		public void EqualsProxyForSameThing_ReturnsTrue()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<div id='foo'/>");
			var elementProxy = new ElementProxy(dom.DocumentElement);
			var proxy2 = new ElementProxy(dom.DocumentElement);
			Assert.That(elementProxy == proxy2, Is.True);
			Assert.That(elementProxy.Equals(proxy2), Is.True);
		}

		[Test]
		public void EqualsProxyForDifferentThing_ReturnsFalse()
		{
			var elementProxy = MakeElement("<div id='foo'/>");
			var proxy2 = MakeElement("<div id='foo'/>");
			Assert.That(elementProxy == proxy2, Is.False);
			Assert.That(elementProxy.Equals(proxy2), Is.False);
		}

		[Test]
		public void EqualsNonProxy_ReturnsFalse()
		{
			var elementProxy = MakeElement("<div id='foo'/>");
			Assert.That(elementProxy == new object(), Is.False); // won't exercise current == code, but I think still worth checking.
			Assert.That(elementProxy.Equals(new object()), Is.False);
		}

		[Test]
		public void SelfOrAncestorHasClass_MatchesSelf()
		{
			var elementProxy = MakeElement("<div class='blueberry pie'/>");
			Assert.That(elementProxy.SelfOrAncestorHasClass("blueberry"), Is.True);
			Assert.That(elementProxy.SelfOrAncestorHasClass("berry"), Is.False);
			Assert.That(elementProxy.SelfOrAncestorHasClass("pie"), Is.True);
			Assert.That(elementProxy.SelfOrAncestorHasClass("pieplate"), Is.False);
		}

		[Test]
		public void SelfOrAncestorHasClass_MatchesAncestor()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<div class='blueberry pie'><div id='foo' class='foolish'/></div>");
			var fooDiv = (XmlElement)dom.SelectSingleNode("//div[@id='foo']");
			var elementProxy = new ElementProxy(fooDiv);
			Assert.That(elementProxy.SelfOrAncestorHasClass("foolish"), Is.True);
			Assert.That(elementProxy.SelfOrAncestorHasClass("blueberry"), Is.True);
			Assert.That(elementProxy.SelfOrAncestorHasClass("berry"), Is.False);
			Assert.That(elementProxy.SelfOrAncestorHasClass("pie"), Is.True);
			Assert.That(elementProxy.SelfOrAncestorHasClass("pieplate"), Is.False);
		}
	}
}
