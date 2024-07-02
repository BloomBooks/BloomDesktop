using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Bloom.SafeXml;
using NUnit.Framework;

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
			doc.LoadXml("<root><child>0</child></root>");

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
			doc.LoadXml("<root><child>0</child></root>");

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
	}
}
