using System.Xml;
using Bloom.ToPalaso;
using NUnit.Framework;
using SIL.Xml;

namespace BloomTests.ToPalaso
{
	/// <summary>
	/// Unit tests for the XmlExtensions class.
	/// </summary>
	[TestFixture]
	public class XmlExtensionsTests
	{
		[Test]
		public void ParentWithClass_FindsDirectParentWithExactClass()
		{
			var doc = new XmlDocument();
			var parent = doc.CreateElement("parent");
			parent.SetAttribute("class", "target");
			var child = doc.CreateElement("child");
			parent.AppendChild(child);
			Assert.That(child.ParentWithClass("target"), Is.EqualTo(parent));
		}

		[TestCase("target other", "targetOther")]
		[TestCase("other target", "othertarget")]
		[TestCase("something target other", "somethingtargetOther")]
		public void ParentWithClass_FindsInDirectParentWithFollowingClasses_SkippingCombinedName(string parentClass, string intermediateClass)
		{
			var doc = new XmlDocument();
			var parent = doc.CreateElement("parent");
			parent.SetAttribute("class", parentClass);
			var intermediate = doc.CreateElement("intermediate");
			parent.AppendChild(intermediate);
			intermediate.SetAttribute("class", intermediateClass);
			var child = doc.CreateElement("child");
			intermediate.AppendChild(child);
			Assert.That(child.ParentWithClass("target"), Is.EqualTo(parent));
		}

		[Test]
		public void ParentWithClass_DoesNotFindSelf()
		{
			var doc = new XmlDocument();
			var child = doc.CreateElement("child");
			child.SetAttribute("class", "target");
			Assert.That(child.ParentWithClass("target"), Is.Null);
		}

		[Test]
		public void ParentWithClass_HandlesMissingClassAttr()
		{
			var doc = new XmlDocument();
			var parent = doc.CreateElement("parent");
			parent.SetAttribute("class", "target");
			var intermediate = doc.CreateElement("intermediate");
			parent.AppendChild(intermediate);
			var child = doc.CreateElement("child");
			intermediate.AppendChild(child);
			Assert.That(child.ParentWithClass("target"), Is.EqualTo(parent));
		}

		[Test]
		public void UnwrapElement_AnchorInDiv_Unwraps()
		{
			var doc = new XmlDocument();
			doc.LoadXml(@"<html><body><div>This is <a href='somewhere'>a <i>nice</i> link</a> that goes nowhere</div></body></html>");

			var anchor =doc.SafeSelectNodes("//a")[0] as XmlElement;
			var div = anchor.ParentNode as XmlElement;

			anchor.UnwrapElement();

			Assert.That(div.InnerText, Is.EqualTo("This is a nice link that goes nowhere"));
			Assert.That(doc.SafeSelectNodes("//a"), Has.Count.EqualTo(0));

			var italics = doc.SafeSelectNodes("//i")[0] as XmlElement;
			Assert.That(italics.InnerText, Is.EqualTo("nice"));
		}
	}
}
