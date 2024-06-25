using System;
using System.Xml;
using SIL.Xml;

namespace Bloom.SafeXml
{
    /// <summary>
    /// A wrapper around XmlDocument to make it thread-safe.
    /// Methods of this and its related classes have much of the API of XmlDocument and its friends,
    /// but anything that returns something like XmlElement in the original API will instead return and appropriate
    /// SafeXmlElement (unless it is making a clone).
    /// Where relevant, if an object that can be wrapped is an input argument, we will provide overloads
    /// that take either the wrapped or unwrapped version.
    /// It's also convenient to add some methods of our own, and to make ones that return the annoying
    /// XmlNodeList type return an array (which also ensures the entire process of enumerating
    /// happens inside the lock).
    /// </summary>
    public class SafeXmlDocument : SafeXmlNode
    {
        private XmlDocument Doc => (XmlDocument)_node;
        internal object Lock = new object();

        internal static SafeXmlDocument Create()
        {
            return new SafeXmlDocument(new XmlDocument());
        }

        public SafeXmlDocument(XmlDocument doc)
            : base(doc, null)
        {
            _doc = this;
        }

        public void LoadXml(string xml)
        {
            lock (Lock)
                Doc.LoadXml(xml);
        }

        public void Load(string filename)
        {
            lock (Lock)
                Doc.Load(filename);
        }

        public bool PreserveWhitespace
        {
            get
            {
                lock (Lock)
                    return Doc.PreserveWhitespace;
            }
            set
            {
                lock (Lock)
                    Doc.PreserveWhitespace = value;
            }
        }

        public new SafeXmlDocument Clone()
        {
            lock (Lock)
                return new SafeXmlDocument((XmlDocument)Doc.Clone());
        }

        public SafeXmlElement CreateElement(string name)
        {
            lock (Lock)
                return new SafeXmlElement(Doc.CreateElement(name), this);
        }

        /// <summary>
        /// Review: do we need a locked wrapper for these?
        /// </summary>
        public SafeXmlText CreateTextNode(string text)
        {
            lock (Lock)
                return new SafeXmlText(Doc.CreateTextNode(text), this);
        }

        public SafeXmlWhitespace CreateWhitespace(string text)
        {
            lock (Lock)
                return new SafeXmlWhitespace(Doc.CreateWhitespace(text), this);
        }

        public override void WriteTo(XmlWriter writer)
        {
            lock (Lock)
                Doc.WriteTo(writer);
        }

        public void Save(string path)
        {
            lock (Lock)
                Doc.Save(path);
        }

        public void Save(XmlWriter writer)
        {
            lock (Lock)
                Doc.Save(writer);
        }

        internal SafeXmlNode[] GetElementsByTagName(string name)
        {
            lock (Lock)
                return WrapNodes(Doc.GetElementsByTagName(name), this);
        }
        public override int GetHashCode()
        {
            lock (Lock)
                return _node.GetHashCode() + Lock.GetHashCode();
        }


        #region Additional Methods

        public SafeXmlElement GetOrCreateElement(string parentPath, string name)
        {
            lock (Lock)
                return new SafeXmlElement(XmlUtils.GetOrCreateElement(Doc, parentPath, name), this);
        }

        public string GetTitleOfHtml(string defaultIfMissing)
        {
            lock (Lock)
                return XmlUtils.GetTitleOfHtml(Doc, defaultIfMissing);
        }

        public SafeXmlElement DocumentElement
        {
            get
            {
                lock (Lock)
                    return new SafeXmlElement(Doc.DocumentElement, this);
            }
        }

        public SafeXmlNode ImportNode(SafeXmlNode node, bool deep)
        {
            lock (Lock)
                return WrapNode(Doc.ImportNode(node._node, deep), this);
        }

        public void WriteContentTo(XmlWriter writer)
        {
            lock (Lock)
                Doc.WriteContentTo(writer);
        }

        public SafeXmlElement Body
        {
            get
            {
                lock (Lock)
                    return GetOrCreateElement("html", "body");
            }
        }

        public SafeXmlElement Head
        {
            get
            {
                lock (Lock)
                    return GetOrCreateElement("html", "head");
            }
        }

        public void RemoveClassFromBody(string className)
        {
            lock (Lock)
                Body.RemoveClass(className);
        }

        public void AddClassToBody(string className)
        {
            lock (Lock)
                Body.AddClass(className);
        }

        public void RemoveStyleSheetIfFound(string path)
        {
            lock (Lock)
            {
                XmlDomExtensions.RemoveStyleSheetIfFound(Doc, path);
            }
        }

        /// <summary>
        /// Get a new namespace manager for this document.
        /// Note: you get a new one every time!
        /// Note: we haven't wrapped the XmlNamespaceManager, so you can't change the namespaces,
        /// so it should not be used on any DOM that is shared across threads to modify the document.
        /// </summary>
        public XmlNamespaceManager GetNewNamespaceManager()
        {
            lock (Lock)
                return new XmlNamespaceManager(Doc.NameTable);
        }

        public SafeXmlDocument StripXHtmlNameSpace()
        {
            lock (Lock)
            {
                var x = new XmlDocument();
                x.LoadXml(OuterXml.Replace("xmlns", "xmlnsNeutered"));
                return new SafeXmlDocument(x);
            }
        }

        /// <summary>
        /// Use this only for tests!
        /// </summary>
        internal XmlDocument UnsafePrivateWrappedXmlDocument_ForTestsOnly
        {
            get
            {
                lock (Lock)
                    return Doc;
            }
        }

        #endregion
    }
}
