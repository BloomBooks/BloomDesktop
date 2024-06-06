using System.Xml;

namespace Bloom.SafeXml
{
    // Needed for SafeXmlDocument.CreateTextNode method, which is used in a number of places.
    public class SafeXmlText : SafeXmlNode
    {
        public SafeXmlText(XmlText node, SafeXmlDocument doc)
            : base(node, doc) { }

        public string Value
        {
            get
            {
                lock (_doc.Lock)
                    return ((XmlText)_node).Value;
            }
            set
            {
                lock (_doc.Lock)
                    ((XmlText)_node).Value = value;
            }
        }
    }
}
