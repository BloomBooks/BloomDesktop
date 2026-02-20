using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Bloom.Book;
using SIL.Xml;

namespace Bloom.SafeXml
{
    public class SafeXmlElement : SafeXmlLinkedNode
    {
        public SafeXmlElement(XmlElement element, SafeXmlDocument doc)
            : base(element, doc) { }

        // In this class, _wrappedNode is always an XmlElement, since it is readonly and set by our constructor,
        // which requires that.  This saves us from having to cast it in every method.
        private XmlElement WrappedElement => (XmlElement)_wrappedNode;

        public override string GetAttribute(string name)
        {
            lock (_doc.Lock)
                return WrappedElement.GetAttribute(name);
        }

        public string GetAttribute(string name, string namespaceURI)
        {
            lock (_doc.Lock)
                return WrappedElement.GetAttribute(name, namespaceURI);
        }

        public override void SetAttribute(string name, string value)
        {
            lock (_doc.Lock)
                WrappedElement.SetAttribute(name, value);
        }

        public void SetAttribute(string name, string ns, string value)
        {
            lock (_doc.Lock)
                WrappedElement.SetAttribute(name, ns, value);
        }

        public void RemoveAttribute(string name)
        {
            lock (_doc.Lock)
                WrappedElement.RemoveAttribute(name);
        }

        public override void WriteTo(XmlWriter writer)
        {
            lock (_doc.Lock)
                WrappedElement.WriteTo(writer);
        }

        /// <summary>
        /// Despite the name, which is to match the original XmlElement property,
        /// this is really about whether, IF it has no children, it will be written as <foo></foo> (false) or <foo/> (true).
        /// </summary>
        public bool IsEmpty
        {
            get
            {
                lock (_doc.Lock)
                    return WrappedElement.IsEmpty;
            }
            set
            {
                lock (_doc.Lock)
                    WrappedElement.IsEmpty = value;
            }
        }

        public override bool HasAttribute(string name)
        {
            lock (_doc.Lock)
                return WrappedElement.HasAttribute(name);
        }

        public SafeXmlElement[] GetElementsByTagName(string name)
        {
            lock (_doc.Lock)
                return WrapElements(WrappedElement.GetElementsByTagName(name), _doc);
        }

        public SafeXmlNode NextSibling
        {
            get
            {
                lock (_doc.Lock)
                    return WrapNode(WrappedElement.NextSibling, _doc);
            }
        }

        #region Additional Methods

        /// <summary>
        /// For HTML, return true if the class attribute contains the given class.
        /// </summary>
        public bool HasClass(string className)
        {
            lock (_doc.Lock)
                return GetClasses().Contains(className);
        }

        public static SafeXmlElement[] GetAllDivsWithClass(
            SafeXmlElement containerElement,
            string className
        )
        {
            const string xpath =
                ".//div[contains(concat(' ', normalize-space(@class), ' '), ' {0} ')]";
            var classPath = xpath.Replace("{0}", className);
            // It's a pain to have to make another array when SafeSelectNodes already made one,
            // but children that have a class are certainly elements, and it makes no sense not
            // to return the proper type of array. If it becomes a performance problem, we can
            // find a way to postpone the ToArray() until after we do the cast.
            return containerElement.SafeSelectNodes(classPath).Cast<SafeXmlElement>().ToArray();
        }

        public void AddClass(string className)
        {
            lock (_doc.Lock)
            {
                if (HasClass(className))
                    return;
                SetAttribute("class", (GetAttribute("class").Trim() + " " + className).Trim());
            }
        }

        public void RemoveClass(string classNameToRemove)
        {
            lock (_doc.Lock)
            {
                var classes = GetClasses().ToList();
                if (classes.Count == 0)
                {
                    return;
                }
                classes.Remove(classNameToRemove);
                string newClassAttributeValue = string.Join(" ", classes);
                SetAttribute("class", newClassAttributeValue);
            }
        }

        public string[] GetClasses()
        {
            lock (_doc.Lock)
            {
                return GetAttribute("class")
                    .Split(HtmlDom.kHtmlClassDelimiters, StringSplitOptions.RemoveEmptyEntries);
            }
        }

        public override string GetOptionalStringAttribute(string name, string defaultValue)
        {
            lock (_doc.Lock)
                return WrappedElement.GetOptionalStringAttribute(name, defaultValue);
        }

        /// <summary>
        /// Make an array of SafeXmlElements from an XmlNodeList that is the output of a query
        /// like SelectNodes where the xpath is known to only return elements.
        /// </summary>
        static SafeXmlElement[] WrapElements(XmlNodeList input, SafeXmlDocument doc)
        {
            return WrapElements(input.Cast<XmlNode>(), doc);
        }

        static SafeXmlElement[] WrapElements(IEnumerable<XmlNode> input, SafeXmlDocument doc)
        {
            lock (doc.Lock)
                return input.Select(node => new SafeXmlElement((XmlElement)node, doc)).ToArray();
        }

        public static SafeXmlElement WrapElement(XmlElement elt, SafeXmlDocument doc)
        {
            if (elt == null)
                return null;
            return new SafeXmlElement(elt, doc);
        }

        /// <summary>
        /// Find the closest parent of the recipient that has the indicated class.
        /// Will not match if the targetClass is a substring of a parent class.
        /// Will not return the recipient, even if it has the class.
        /// Returns null if there is no such parent.
        /// </summary>
        public SafeXmlElement ParentWithClass(string targetClass)
        {
            return (ParentNode as SafeXmlElement)?.ParentOrSelfWithClass(targetClass);
        }

        public SafeXmlElement ParentOrSelfWithClass(string targetClass)
        {
            var current = this;
            while (
                current != null
                && !(" " + current.GetAttribute("class") + " ").Contains(" " + targetClass + " ")
            )
                current = current.ParentNode as SafeXmlElement;
            return current;
        }

        public SafeXmlElement ParentWithAttribute(string targetAttribute)
        {
            var current = ParentElement;
            while (current != null && !current.HasAttribute(targetAttribute))
                current = current.ParentNode as SafeXmlElement;
            return current;
        }

        /// <summary>
        /// Get a parent element if any that has the specified value for the specified attribute
        /// Note: currently if attrVal is empty it will match both parents where the attribute
        /// is present and empty, and parents that don't have the attribute at all. It is not
        /// intended that it should be used this way.
        /// </summary>
        public SafeXmlElement ParentWithAttributeValue(string targetAttribute, string attrVal)
        {
            ArgumentException.ThrowIfNullOrEmpty(attrVal);

            var current = ParentElement;
            while (current != null && current.GetAttribute(targetAttribute) != attrVal)
                current = current.ParentNode as SafeXmlElement;
            return current;
        }

        public SafeXmlElement GetChildWithName(string name)
        {
            return ChildNodes.FirstOrDefault(n => n.Name.ToLowerInvariant() == name)
                as SafeXmlElement;
        }

        public SafeXmlElement AppendChild(string name)
        {
            var result = OwnerDocument.CreateElement(name);
            AppendChild(result);
            return result;
        }

        /// <summary>
        /// Find the closest ancestor (not the element itself) that has the specified value for the specified attribute.
        /// If no parent does, answer null.
        /// </summary>
        public SafeXmlElement AncestorWithAttributeValue(string targetAttr, string targetVal)
        {
            var current = ParentNode as SafeXmlElement;
            while (current != null && current.GetAttribute(targetAttr) != targetVal)
                current = current.ParentNode as SafeXmlElement;
            return current;
        }

        /// <summary>
        /// Replace the element with its children.
        /// </summary>
        public void UnwrapElement()
        {
            var content = ChildNodes.Cast<SafeXmlNode>().Reverse().ToArray();
            var parent = ParentNode as SafeXmlElement;
            foreach (var child in content)
            {
                parent.InsertAfter(child, this);
            }
            parent.RemoveChild(this);
        }

        public SafeXmlElement FirstElementChild =>
            ChildNodes.FirstOrDefault(n => n is SafeXmlElement) as SafeXmlElement;

        /// <summary>
        /// Use this only for tests!
        /// </summary>
        internal XmlElement UnsafePrivateWrappedXmlElement_ForTestsOnly
        {
            get
            {
                lock (_doc.Lock)
                    return _wrappedNode as XmlElement;
            }
        }

        #endregion
    }
}
