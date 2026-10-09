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
        private static SafeXmlElement ParseElement(string xml)
        {
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(xml);
            return doc.DocumentElement;
        }

        [Test]
        public void GetXmlIgnoringAttributeOrder_SameAttributesInAnotherOrder_Equal()
        {
            var a = ParseElement(
                "<div class='x' lang='en'><p data-a='1' data-b='2'>text &amp; more</p></div>"
            );
            var b = ParseElement(
                "<div lang='en' class='x'><p data-b='2' data-a='1'>text &amp; more</p></div>"
            );
            Assert.That(a.OuterXml, Is.Not.EqualTo(b.OuterXml), "test setup: order differs");

            Assert.That(
                a.GetXmlIgnoringAttributeOrder(),
                Is.EqualTo(b.GetXmlIgnoringAttributeOrder())
            );
        }

        [Test]
        public void GetXmlIgnoringAttributeOrder_DifferentValueTextOrAttribute_NotEqual()
        {
            var original = ParseElement("<div class='x'><p lang='en'>text</p></div>")
                .GetXmlIgnoringAttributeOrder();
            foreach (
                var changed in new[]
                {
                    "<div class='y'><p lang='en'>text</p></div>", // a value
                    "<div class='x'><p lang='en'>other</p></div>", // the text
                    "<div class='x'><p lang='en' dir='rtl'>text</p></div>", // an extra attribute
                    "<div class='x'><p lang='en'>text</p><p/></div>", // an extra element
                }
            )
            {
                Assert.That(
                    ParseElement(changed).GetXmlIgnoringAttributeOrder(),
                    Is.Not.EqualTo(original),
                    changed
                );
            }
        }

        [Test]
        [Category("SkipOnTeamCity")] // This is flaky on TeamCity for some reason. We need to fix it up; for now, skip it.
        public void Xml_DoesNotProvide_ThreadSafety()
        {
            // The point of this test is that unsynchronised use of an XmlDocument from several
            // threads goes wrong, which is why SafeXml exists. Showing that takes a real collision,
            // and a collision is a matter of timing. So rather than doing a fixed number of
            // operations with pauses in between (where nearly all the time is spent asleep, and on
            // an unlucky run no two operations overlap), the threads run with no pauses and stop
            // as soon as any of them throws. (They need not start together: each keeps going until
            // then, so they overlap whenever they start.) A collision then normally comes
            // within milliseconds; the ten-second limit is only there so that an XmlDocument that
            // really were thread-safe would fail the test rather than hang it.
            var doc = new XmlDocument();
            doc.LoadXml("<root i=\"0\"><child>0</child></root>");
            using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                Task Hammer(Action<int> step) =>
                    Task.Run(() =>
                    {
                        try
                        {
                            for (var i = 1; !stop.IsCancellationRequested; ++i)
                                step(i);
                        }
                        catch
                        {
                            stop.Cancel(); // one collision is enough; let the others finish
                            throw;
                        }
                    });

                var tasks = new[]
                {
                    Hammer(i =>
                    {
                        doc.FirstChild.InnerXml = $"<child>{i}</child>";
                        (doc.FirstChild as XmlElement).SetAttribute("i", i.ToString());
                    }),
                    Hammer(i =>
                    {
                        doc.FirstChild.InnerXml = $"<child>{-i}</child>";
                        (doc.FirstChild as XmlElement).SetAttribute("i", (-i).ToString());
                    }),
                    Hammer(i =>
                    {
                        var inner = doc.FirstChild.InnerXml;
                        var attr = (doc.FirstChild as XmlElement).GetAttribute("i");
                    }),
                };
                Assert.Throws<AggregateException>(
                    () => Task.WaitAll(tasks),
                    "Ten seconds of concurrent use of one XmlDocument produced no error; it seems to be thread-safe after all."
                );
            }
        }

        [Test]
        public void SafeXml_Provides_ThreadSafety()
        {
            var tasks = new List<Task>();
            var doc = SafeXmlDocument.Create();
            doc.LoadXml("<root i=\"0\"><child>0</child></root>");

            tasks.Add(
                Task.Run(() =>
                {
                    for (var i = 1; i <= 200; ++i)
                    {
                        doc.FirstChild.InnerXml = $"<child>{i}</child>";
                        doc.FirstChild.SetAttribute("i", i.ToString());
                        Thread.Sleep(5);
                    }
                })
            );
            tasks.Add(
                Task.Run(() =>
                {
                    for (var i = 1; i <= 200; ++i)
                    {
                        doc.FirstChild.InnerXml = $"<child>{i}</child>";
                        doc.FirstChild.SetAttribute("i", i.ToString());
                        Thread.Sleep(4);
                    }
                })
            );
            tasks.Add(
                Task.Run(() =>
                {
                    for (var i = 1; i <= 200; ++i)
                    {
                        var inner = doc.FirstChild.InnerXml;
                        var attr = doc.FirstChild.GetAttribute("i");
                        Assert.That(inner, Does.Match("<child>[0-9]+</child>"));
                        Assert.That(attr, Does.Match("[0-9]+"));
                        Thread.Sleep(5);
                    }
                })
            );
            Task.WaitAll(tasks.ToArray());
            Assert.That(doc.FirstChild.InnerXml, Is.EqualTo("<child>200</child>"));
            Assert.That(doc.FirstChild.GetAttribute("i"), Is.EqualTo("200"));
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
