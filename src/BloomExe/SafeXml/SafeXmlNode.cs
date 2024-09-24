using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using SIL.Code;
using SIL.Xml;

namespace Bloom.SafeXml
{
    public class SafeXmlNode
    {
        internal readonly XmlNode _wrappedNode; // cannot be protected due to SafeXmlDocument.ImportNode method.

        // I want this to be readonly, but can't find a way to make the constructor of SafeXmlDocument work.
        protected SafeXmlDocument _doc;

        // Usually should instead call WrapNode; only for subclass constructors
        protected SafeXmlNode(XmlNode node, SafeXmlDocument doc)
        {
            _wrappedNode = node;
            _doc = doc;
        }

        public SafeXmlNode ParentNode
        {
            get
            {
                lock (_doc.Lock)
                    return WrapNode(_wrappedNode.ParentNode, _doc);
            }
        }

        public SafeXmlNode RemoveChild(SafeXmlNode child)
        {
            lock (_doc.Lock)
            {
                _wrappedNode.RemoveChild(child._wrappedNode);
                return child; // what the original does; we don't need to make a new one.
            }
        }

        public SafeXmlNode ReplaceChild(SafeXmlNode newChild, SafeXmlNode oldChild)
        {
            lock (_doc.Lock)
            {
                _wrappedNode.ReplaceChild(newChild._wrappedNode, oldChild._wrappedNode);
                return oldChild; // what the original does; we don't need to make a new one.
            }
        }

        public SafeXmlNode InsertBefore(SafeXmlNode newChild, SafeXmlNode refChild)
        {
            lock (_doc.Lock)
            {
                _wrappedNode.InsertBefore(newChild._wrappedNode, refChild?._wrappedNode);
                return newChild; // what the original does; we don't need to make a new one.
            }
        }

        public SafeXmlNode InsertAfter(SafeXmlNode newChild, SafeXmlNode refChild)
        {
            lock (_doc.Lock)
            {
                _wrappedNode.InsertAfter(newChild._wrappedNode, refChild?._wrappedNode);
                return newChild; // what the original does; we don't need to make a new one.
            }
        }

        public XmlNodeType NodeType
        {
            get
            {
                lock (_doc.Lock)
                    return _wrappedNode.NodeType;
            }
        }
        public string Name
        {
            get
            {
                lock (_doc.Lock)
                    return _wrappedNode.Name;
            }
        }

        public string LocalName
        {
            get
            {
                lock (_doc.Lock)
                    return _wrappedNode.LocalName;
            }
        }

        public string OuterXml
        {
            get
            {
                lock (_doc.Lock)
                    return _wrappedNode.OuterXml;
            }
        }

        public SafeXmlNode AppendChild(SafeXmlNode newChild)
        {
            lock (_doc.Lock)
            {
                _wrappedNode.AppendChild(newChild._wrappedNode);
                return newChild; // what the original does; we don't need to make a new one.
            }
        }

        public SafeXmlNode PrependChild(SafeXmlNode newChild)
        {
            lock (_doc.Lock)
            {
                _wrappedNode.PrependChild(newChild._wrappedNode);
                return newChild; // what the original does; we don't need to make a new one.
            }
        }

        public string InnerText
        {
            get
            {
                lock (_doc.Lock)
                    return _wrappedNode.InnerText;
            }
            set
            {
                lock (_doc.Lock)
                    _wrappedNode.InnerText = value;
            }
        }

        public string InnerXml
        {
            get
            {
                lock (_doc.Lock)
                    return _wrappedNode.InnerXml;
            }
            set
            {
                lock (_doc.Lock)
                    _wrappedNode.InnerXml = value;
            }
        }

        public string Value
        {
            get
            {
                lock (_doc.Lock)
                    return _wrappedNode.Value;
            }
            set
            {
                lock (_doc.Lock)
                    _wrappedNode.Value = value;
            }
        }

        public SafeXmlNode[] ChildNodes
        {
            get
            {
                lock (_doc.Lock)
                    return WrapNodes(_wrappedNode.ChildNodes, _doc);
            }
        }

        public SafeXmlNode FirstChild
        {
            get
            {
                lock (_doc.Lock)
                    return WrapNode(_wrappedNode.FirstChild, _doc);
            }
        }

        public SafeXmlNode LastChild
        {
            get
            {
                lock (_doc.Lock)
                    return WrapNode(_wrappedNode.LastChild, _doc);
            }
        }

        public bool HasChildNodes
        {
            get
            {
                lock (_doc.Lock)
                    return _wrappedNode.HasChildNodes;
            }
        }

        public virtual void WriteTo(XmlWriter w)
        {
            throw new NotImplementedException(
                "Only SafeXmlElement and SafeXmlDocument can write to XmlWriter"
            );
        }

        public SafeXmlDocument OwnerDocument => _doc;

        public SafeXmlNode Clone()
        {
            lock (_doc.Lock)
                return WrapNode(_wrappedNode.Clone(), _doc);
        }

        public SafeXmlNode CloneNode(bool deep)
        {
            lock (_doc.Lock)
                return WrapNode(_wrappedNode.CloneNode(deep), _doc);
        }

        public void RemoveAll()
        {
            lock (_doc.Lock)
                _wrappedNode.RemoveAll();
        }

        public static bool operator ==(SafeXmlNode a, SafeXmlNode b)
        {
            if (a != null)
            {
                lock (a._doc.Lock)
                    return a._wrappedNode == b?._wrappedNode;
            }
            return (object)b == null;
        }

        public static bool operator !=(SafeXmlNode a, SafeXmlNode b)
        {
            if ((object)a != null)
            {
                lock (a._doc.Lock)
                    return a._wrappedNode != b?._wrappedNode;
            }
            return (object)b != null;
        }

        public override bool Equals(object obj)
        {
            lock (_doc.Lock)
            {
                if (obj is SafeXmlNode)
                    return _wrappedNode.Equals((obj as SafeXmlNode)._wrappedNode);
                else
                    return false;
            }
        }

        public override int GetHashCode()
        {
            lock (_doc.Lock)
                return _wrappedNode.GetHashCode() + _doc.GetHashCode();
        }

        #region Additional Methods

        /// <summary>
        /// Overridden in SafeXmlElement, which actually has attributes.
        /// </summary>
        public virtual string GetAttribute(string name)
        {
            return null;
        }

        public virtual string GetOptionalStringAttribute(string name, string defaultValue)
        {
            return null;
        }

        public virtual void SetAttribute(string name, string value)
        {
            throw new NotImplementedException("Only Elements have attributes");
        }

        public virtual bool HasAttribute(string name)
        {
            return false; // Nodes never do; we just allow it to make iterating over node lists easier.
        }

        public SafeXmlNode[] SafeSelectNodes(string xpath)
        {
            lock (_doc.Lock)
                return WrapNodes(_wrappedNode.SafeSelectNodes(xpath), _doc);
        }

        public SafeXmlNode[] SafeSelectNodes(string xpath, XmlNamespaceManager ns)
        {
            lock (_doc.Lock)
                return WrapNodes(_wrappedNode.SafeSelectNodes(xpath, ns), _doc);
        }

        /// <summary>
        /// Convert the input to SafeXmlNode objects (using the appropriate subclasses as necessary)
        /// </summary>
        protected static SafeXmlNode[] WrapNodes(XmlNodeList input, SafeXmlDocument doc)
        {
            return WrapNodes(input.Cast<XmlNode>(), doc);
        }

        protected static SafeXmlNode[] WrapNodes(IEnumerable<XmlNode> input, SafeXmlDocument doc)
        {
            return input.Select(node => WrapNode(node, doc)).ToArray();
        }

        protected static SafeXmlNode WrapNode(XmlNode node, SafeXmlDocument doc)
        {
            if (node == null)
                return null;
            if (node is XmlElement elt)
                return new SafeXmlElement(elt, doc);
            else if (node is XmlText txt)
                return new SafeXmlText(txt, doc);
            else if (node is XmlWhitespace spc)
                return new SafeXmlWhitespace(spc, doc);
            else if (node is XmlComment com)
                return new SafeXmlComment(com, doc);
            else if (node is XmlDocument xd)
                return new SafeXmlDocument(xd);
            else if (node is XmlAttribute xa)
                return new SafeXmlAttribute(xa, doc);
            else if (node is XmlCDataSection cdata)
                return new SafeXmlCDataSection(cdata, doc);
            // Enhance: if we use more subtypes add more cases

            Guard.Against(
                node.GetType() != typeof(XmlNode),
                $"trying to convert an unexpected type of XmlNode: {node.GetType().Name}"
            );
            return new SafeXmlNode(node, doc);
        }

        public SafeXmlNode SelectSingleNode(string xpath)
        {
            lock (_doc.Lock)
                return WrapNode(_wrappedNode.SelectSingleNode(xpath), _doc);
        }

        public SafeXmlNode SelectSingleNode(string xpath, XmlNamespaceManager nsmgr)
        {
            lock (_doc.Lock)
                return WrapNode(_wrappedNode.SelectSingleNode(xpath, nsmgr), _doc);
        }

        /// <summary>
        /// This is a much simpler object than wrapping Attributes for many cases where we need a list.
        /// Hoping we don't need to make a SafeXmlAttributeList class.
        /// </summary>
        public string[] AttributeNames
        {
            get
            {
                lock (_doc.Lock)
                    return _wrappedNode.Attributes
                        ?.Cast<XmlAttribute>()
                        .Select(attr => attr.Name)
                        .ToArray();
            }
        }

        /// <summary>
        /// Another way to get at the name and value of each attr, without the overhead
        /// of wrapping XmlAttributeList or XmlAttribute.
        /// </summary>
        public NameValue[] AttributePairs
        {
            get
            {
                lock (_doc.Lock)
                    return _wrappedNode.Attributes
                        ?.Cast<XmlAttribute>()
                        .Select(attr => new NameValue(attr.Name, attr.Value))
                        .ToArray();
            }
        }

        /// <summary>
        /// Deletes the specified nodes from their parents.
        /// </summary>
        /// <remarks>We shouldn't delete the nodes while iterating over the
        /// node list because that modifies the enumeration that we're looping over.</remarks>
        public void DeleteNodes(string path)
        {
            lock (_doc.Lock)
                foreach (
                    var toDelete in _wrappedNode
                        .SafeSelectNodes(path)
                        .OfType<XmlElement>()
                        .ToArray()
                )
                    toDelete.ParentNode.RemoveChild(toDelete);
        }

        /// <summary>
        /// This is for doing selections in xhtml, where there is a default namespace, which makes
        /// normal selects fail.  This tries to set a namespace and inject prefix into the xpath.
        /// </summary>
        public SafeXmlNode SelectSingleNodeHonoringDefaultNS(string path)
        {
            lock (_doc.Lock)
                return WrapNode(_wrappedNode.SelectSingleNodeHonoringDefaultNS(path), _doc);
        }

        #endregion
    }
}
