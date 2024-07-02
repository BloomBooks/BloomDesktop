using System;
using System.Xml;
using SIL.Xml;

namespace Bloom.SafeXml
{
    /// <summary>
    /// A wrapper around XmlDocument to make it thread-safe.
    /// Methods of this and its related classes have much of the API of XmlDocument and its friends,
    /// but anything that returns something like XmlElement in the original API will instead return an appropriate
    /// SafeXmlElement.
    /// Constructors may take an XmlNode based object, and will wrap it in an appropriate SafeXmlNode object.
    /// It's also convenient to add some methods of our own, and to make ones that return the annoying
    /// XmlNodeList type return an array (which also ensures the entire process of enumerating
    /// happens inside the lock).
    /// </summary>
    public class SafeXmlDocument : SafeXmlNode
    {
        // In this class, _wrappedNode is always an XmlDocument, since it is readonly and set by our constructor,
        // which requires that.  This saves us from having to cast it in every method.
        private XmlDocument WrappedDocument => (XmlDocument)_wrappedNode;
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
                WrappedDocument.LoadXml(xml);
        }

        public void Load(string filename)
        {
            lock (Lock)
                WrappedDocument.Load(filename);
        }

        public bool PreserveWhitespace
        {
            get
            {
                lock (Lock)
                    return WrappedDocument.PreserveWhitespace;
            }
            set
            {
                lock (Lock)
                    WrappedDocument.PreserveWhitespace = value;
            }
        }

        public new SafeXmlDocument Clone()
        {
            lock (Lock)
                return new SafeXmlDocument((XmlDocument)WrappedDocument.Clone());
        }

        public SafeXmlElement CreateElement(string name)
        {
            lock (Lock)
                return new SafeXmlElement(WrappedDocument.CreateElement(name), this);
        }

        public SafeXmlText CreateTextNode(string text)
        {
            lock (Lock)
                return new SafeXmlText(WrappedDocument.CreateTextNode(text), this);
        }

        public SafeXmlWhitespace CreateWhitespace(string text)
        {
            lock (Lock)
                return new SafeXmlWhitespace(WrappedDocument.CreateWhitespace(text), this);
        }

        public override void WriteTo(XmlWriter writer)
        {
            lock (Lock)
                WrappedDocument.WriteTo(writer);
        }

        public void Save(string path)
        {
            lock (Lock)
                WrappedDocument.Save(path);
        }

        public void Save(XmlWriter writer)
        {
            lock (Lock)
                WrappedDocument.Save(writer);
        }

        internal SafeXmlNode[] GetElementsByTagName(string name)
        {
            lock (Lock)
                return WrapNodes(WrappedDocument.GetElementsByTagName(name), this);
        }
        public override int GetHashCode()
        {
            lock (Lock)
                return _wrappedNode.GetHashCode() + Lock.GetHashCode();
        }


        #region Additional Methods

        public SafeXmlElement GetOrCreateElement(string parentPath, string name)
        {
            lock (Lock)
                return new SafeXmlElement(XmlUtils.GetOrCreateElement(WrappedDocument, parentPath, name), this);
        }

        public string GetTitleOfHtml(string defaultIfMissing)
        {
            lock (Lock)
                return XmlUtils.GetTitleOfHtml(WrappedDocument, defaultIfMissing);
        }

        public SafeXmlElement DocumentElement
        {
            get
            {
                lock (Lock)
                    return new SafeXmlElement(WrappedDocument.DocumentElement, this);
            }
        }

        public SafeXmlNode ImportNode(SafeXmlNode node, bool deep)
        {
            lock (Lock)
                return WrapNode(WrappedDocument.ImportNode(node._wrappedNode, deep), this);
        }

        public void WriteContentTo(XmlWriter writer)
        {
            lock (Lock)
                WrappedDocument.WriteContentTo(writer);
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
                XmlDomExtensions.RemoveStyleSheetIfFound(WrappedDocument, path);
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
                return new XmlNamespaceManager(WrappedDocument.NameTable);
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

        public string GetMetaValue(string name, string defaultValue)
        {
            lock (Lock)
            {
                var node = SelectSingleNode(
                    "//head/meta[@name='" + name + "' or @name='" + name.ToLowerInvariant() + "']"
                );
                if (node != null)
                    return node.GetAttribute("content");
                return defaultValue;
            }
        }

        public void UpdateMetaElement(string name, string value)
        {
            lock (Lock)
            {
                var n = SelectSingleNode("//meta[@name='" + name + "']") as SafeXmlElement;
                if (n == null)
                {
                    n = CreateElement("meta");
                    n.SetAttribute("name", name);
                    SelectSingleNode("//head").AppendChild(n);
                }
                n.SetAttribute("content", value);
            }
        }

        public void RemoveMetaElement(string name)
        {
            lock (Lock)
            {
                foreach (var n in SafeSelectNodes("//head/meta[@name='" + name + "']"))
                    n.ParentNode.RemoveChild(n);
            }
        }

        public void RemoveMetaElement(string oldName, Func<string> read, Action<string> write)
        {
            lock (Lock)
            {
                if (!HasMetaElement(oldName))
                    return;

                if (!String.IsNullOrEmpty(read()))
                {
                    RemoveMetaElement(oldName);
                    return;
                }

                //ok, so we do have to transfer the value over

                write(GetMetaValue(oldName, ""));

                //and remove any of the old name
                foreach (var node in SafeSelectNodes("//head/meta[@name='" + oldName + "']"))
                {
                    node.ParentNode.RemoveChild(node);
                }
            }
        }

        public bool HasMetaElement(string name)
        {
            lock (Lock)
                return SafeSelectNodes("//head/meta[@name='" + name + "']").Length > 0;
        }

        /// <summary>
        /// Use this only for tests!
        /// </summary>
        internal XmlDocument UnsafePrivateWrappedXmlDocument_ForTestsOnly
        {
            get
            {
                lock (Lock)
                    return WrappedDocument;
            }
        }

        #endregion
    }
}
