using System;
using System.Xml;
using Bloom;
using Bloom.SafeXml;
using NUnit.Framework;
using SIL.Xml;

namespace BloomTests
{
    //NB: if c# ever allows us to add static extension methods,
    //then all this could be an extension on nunit's Assert class.

    public class AssertThatXmlIn
    {
        public static AssertHtmlFile HtmlFile(string path)
        {
            return new AssertHtmlFile(path);
        }

        public static AssertXmlString String(string xmlString)
        {
            return new AssertXmlString(xmlString);
        }

        public static AssertDom Dom(SafeXmlDocument dom)
        {
            return new AssertDom(dom.UnsafePrivateWrappedXmlDocument_ForTestsOnly);
        }

        public static AssertElement Element(SafeXmlElement element)
        {
            return new AssertElement(element.UnsafePrivateWrappedXmlElement_ForTestsOnly);
        }
    }

    public class AssertXmlString : AssertXmlCommands
    {
        private readonly string _xmlString;

        public AssertXmlString(string xmlString)
        {
            _xmlString = xmlString;
        }

        protected override XmlNode NodeOrDom
        {
            get
            {
                var dom = new XmlDocument();
                dom.LoadXml(_xmlString);
                return dom;
            }
        }
    }

    public class AssertFile : AssertXmlCommands
    {
        private readonly string _path;

        public AssertFile(string path)
        {
            _path = path;
        }

        protected override XmlNode NodeOrDom
        {
            get
            {
                var dom = new XmlDocument();
                dom.Load(_path);
                return dom;
            }
        }
    }

    public class AssertHtmlFile : AssertXmlCommands
    {
        private readonly string _path;

        public AssertHtmlFile(string path)
        {
            _path = path;
        }

        protected override XmlNode NodeOrDom
        {
            get { return XmlHtmlConverter.GetXmlDomFromHtmlFile(_path, false).UnsafePrivateWrappedXmlDocument_ForTestsOnly; }
        }
    }

    public class AssertDom : AssertXmlCommands
    {
        private readonly XmlDocument _dom;

        public AssertDom(XmlDocument dom)
        {
            _dom = dom;
        }

        protected override XmlNode NodeOrDom
        {
            get { return _dom; }
        }
    }

    public class AssertElement : AssertXmlCommands
    {
        private readonly XmlElement _element;

        public AssertElement(XmlElement element)
        {
            _element = element;
        }

        protected override XmlNode NodeOrDom
        {
            get { return _element; }
        }
    }

    public abstract class AssertXmlCommands
    {
        protected abstract XmlNode NodeOrDom { get; }

        public XmlNameTable NameTable
        {
            get
            {
                var doc = NodeOrDom as XmlDocument;
                if (doc != null)
                    return doc.NameTable;
                else
                    // review: may or may not work; I've only tried cases where it IS a document.
                    return NodeOrDom.OwnerDocument.NameTable;
            }
        }

        public void HasAtLeastOneMatchForXpath(string xpath, XmlNamespaceManager nameSpaceManager)
        {
            XmlNode node = GetNode(xpath, nameSpaceManager);
            if (node == null)
            {
                Console.WriteLine("Could not match " + xpath);
                PrintNodeToConsole(NodeOrDom);
            }
            Assert.IsNotNull(node, "Not matched: " + xpath);
        }

        /// <summary>
        /// Will honor default namespace
        /// </summary>
        public void HasAtLeastOneMatchForXpath(string xpath)
        {
            XmlNode node = GetNode(xpath);
            if (node == null)
            {
                Console.WriteLine("Could not match " + xpath);
                PrintNodeToConsole(NodeOrDom);
            }
            Assert.IsNotNull(node, "Not matched: " + xpath);
        }

        /// <summary>
        /// Will honor default namespace
        /// </summary>
        public void HasSpecifiedNumberOfMatchesForXpath(string xpath, int count)
        {
            var nodes = NodeOrDom.SafeSelectNodes(xpath);
            CheckCountOfNodes(nodes, xpath, count);
        }

        public void HasSpecifiedNumberOfMatchesForXpath(
            string xpath,
            XmlNamespaceManager nameSpaceManager,
            int count
        )
        {
            var nodes = NodeOrDom.SafeSelectNodes(xpath, nameSpaceManager);
            CheckCountOfNodes(nodes, xpath, count);
        }

        private void CheckCountOfNodes(XmlNodeList nodes, string xpath, int count)
        {
            int nodeCount = 0;
            if (nodes != null)
                nodeCount = nodes.Count;
            if (nodeCount != count)
            {
                Console.WriteLine(
                    "Expected {0} but got {1} matches for {2}",
                    count,
                    nodeCount,
                    xpath
                );
                PrintNodeToConsole(NodeOrDom);
                Assert.AreEqual(count, nodeCount, "matches for " + xpath);
            }
        }

        public int CountOfMatchesForXPath(string xpath)
        {
            return NodeOrDom.SafeSelectNodes(xpath).Count;
        }

        public static void PrintNodeToConsole(XmlNode node)
        {
            // without this, we may get "DTD is not allowed in XML fragments"
            var doctype = (node as XmlDocument)?.DocumentType;
            if (doctype != null)
                node.RemoveChild(doctype);

            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.ConformanceLevel = ConformanceLevel.Fragment;
            XmlWriter writer = XmlWriter.Create(Console.Out, settings);
            node.WriteContentTo(writer);
            writer.Flush();
            Console.WriteLine();
        }

        public void HasNoMatchForXpath(string xpath, XmlNamespaceManager nameSpaceManager)
        {
            XmlNode node = GetNode(xpath, nameSpaceManager);
            if (node != null)
            {
                Console.WriteLine("Was not supposed to match " + xpath);
                PrintNodeToConsole(NodeOrDom);
            }
            Assert.IsNull(node, "Should not have matched: " + xpath);
        }

        public void HasNoMatchForXpath(string xpath)
        {
            XmlNode node = GetNode(xpath, new XmlNamespaceManager(new NameTable()));
            if (node != null)
            {
                Console.WriteLine("Was not supposed to match " + xpath);
                PrintNodeToConsole(NodeOrDom);
            }
            Assert.IsNull(node, "Should not have matched: " + xpath);
        }

        private XmlNode GetNode(string xpath)
        {
            return NodeOrDom.SelectSingleNodeHonoringDefaultNS(xpath);
        }

        private XmlNode GetNode(string xpath, XmlNamespaceManager nameSpaceManager)
        {
            return NodeOrDom.SelectSingleNode(xpath, nameSpaceManager);
        }
    }
}
