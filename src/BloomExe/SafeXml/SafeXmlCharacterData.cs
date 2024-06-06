using System.Xml;

namespace Bloom.SafeXml
{
    /// <summary>
    /// This is just to imitate the original XmlElement class hierarchy.
    /// </summary>
    public class SafeXmlCharacterData : SafeXmlLinkedNode
    {
        public SafeXmlCharacterData(XmlNode node, SafeXmlDocument doc) : base(node, doc)
        {
        }

        public string Value
        {
            get
            {
                lock (_doc.Lock)
                    return ((XmlCharacterData)_node).Value;
            }
            set
            {
                lock (_doc.Lock)
                    ((XmlCharacterData)_node).Value = value;
            }
        }
    }
}
