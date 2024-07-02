using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Bloom.SafeXml;
using NUnit.Framework;
using SIL.Xml;

namespace BloomTests.SafeXml
{
	[TestFixture]
	public class SafeXmlTests
	{
		[Test]
		public void Xml_DoesNotProvide_ThreadSafety()
		{
			var tasks = new List<Task>();
			var doc = new XmlDocument();
			doc.LoadXml("<root i=\"0\"><child>0</child></root>");

			Assert.Throws<AggregateException>(() =>
			{
				tasks.Add(Task.Run(() =>
				{
					for (var i = 1; i <= 20; ++i)
					{
						doc.FirstChild.InnerXml = $"<child>{i}</child>";
						(doc.FirstChild as XmlElement).SetAttribute("i", i.ToString());
						Thread.Sleep(5);
					}
				}));
				tasks.Add(Task.Run(() =>
				{
					for (var i = 1; i <= 20; ++i)
					{
						doc.FirstChild.InnerXml = $"<child>{i}</child>";
						(doc.FirstChild as XmlElement).SetAttribute("i", i.ToString());
						Thread.Sleep(4);
					}
				}));
				tasks.Add(Task.Run(() =>
				{
					for (var i = 1; i <= 20; ++i)
					{
						var inner = doc.FirstChild.InnerXml;
						var attr = (doc.FirstChild as XmlElement).GetAttribute("i");
						Thread.Sleep(5);
					}
				}));
				Task.WaitAll(tasks.ToArray());
			});
		}

		[Test]
		public void SafeXml_Provides_ThreadSafety()
		{
			var tasks = new List<Task>();
			var doc = SafeXmlDocument.Create();
			doc.LoadXml("<root i=\"0\"><child>0</child></root>");

			tasks.Add(Task.Run(() =>
			{
				for (var i = 1; i <= 20; ++i)
				{
					doc.FirstChild.InnerXml = $"<child>{i}</child>";
					doc.FirstChild.SetAttribute("i", i.ToString());
					Thread.Sleep(5);
				}
			}));
			tasks.Add(Task.Run(() =>
			{
				for (var i = 1; i <= 20; ++i)
				{
					doc.FirstChild.InnerXml = $"<child>{i}</child>";
					doc.FirstChild.SetAttribute("i", i.ToString());
					Thread.Sleep(4);
				}
			}));
			tasks.Add(Task.Run(() =>
			{
				for (var i = 1; i <= 20; ++i)
				{
					var inner = doc.FirstChild.InnerXml;
					var attr = doc.FirstChild.GetAttribute("i");
					Assert.That(inner, Does.Match("<child>[0-9]+</child>"));
					Assert.That(attr, Does.Match("[0-9]+"));
					Thread.Sleep(5);
				}
			}));
			Task.WaitAll(tasks.ToArray());
			Assert.That(doc.FirstChild.InnerXml, Is.EqualTo("<child>20</child>"));
			Assert.That(doc.FirstChild.GetAttribute("i"), Is.EqualTo("20"));
		}

		// The following tests do not check for thread safety, but rather for the correctness of the methods.
		// The tested methods were derived from an earlier implementation of XML extensions that were added
		// to the SafeXmlElement class.

		[Test]
		public void ParentWithClass_FindsDirectParentWithExactClass()
		{
			var doc = SafeXmlDocument.Create();
			var parent = doc.CreateElement("parent");
			parent.SetAttribute("class", "target");
			var child = doc.CreateElement("child");
			parent.AppendChild(child);
			Assert.That(child.ParentWithClass("target"), Is.EqualTo(parent));
		}

		[TestCase("target other", "targetOther")]
		[TestCase("other target", "othertarget")]
		[TestCase("something target other", "somethingtargetOther")]
		public void ParentWithClass_FindsInDirectParentWithFollowingClasses_SkippingCombinedName(
			string parentClass,
			string intermediateClass
		)
		{
			var doc = SafeXmlDocument.Create();
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
			var doc = SafeXmlDocument.Create();
			var parent = doc.CreateElement("parent");
			var child = doc.CreateElement("child");
			child.SetAttribute("class", "target");
			parent.AppendChild(child);
			Assert.That(child.ParentWithClass("target"), Is.Null);
		}

		[Test]
		public void ParentWithClass_HandlesMissingClassAttr()
		{
			var doc = SafeXmlDocument.Create();
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
			var doc = SafeXmlDocument.Create();
			doc.LoadXml(
				@"<html><body><div>This is <a href='somewhere'>a <i>nice</i> link</a> that goes nowhere</div></body></html>"
			);

			var anchor = doc.SafeSelectNodes("//a")[0] as SafeXmlElement;
			var div = anchor.ParentNode as SafeXmlElement;

			anchor.UnwrapElement();

			Assert.That(div.InnerText, Is.EqualTo("This is a nice link that goes nowhere"));
			Assert.That(doc.SafeSelectNodes("//a"), Has.Length.EqualTo(0));

			var italics = doc.SafeSelectNodes("//i")[0] as SafeXmlElement;
			Assert.That(italics.InnerText, Is.EqualTo("nice"));
		}
	}
}
