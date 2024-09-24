using System.Xml;

namespace Bloom.SafeXml
{
    /// <summary>
    /// This is just to imitate the original XmlElement class hierarchy.
    /// </summary>
    public class SafeXmlCharacterData : SafeXmlLinkedNode
    {
        public SafeXmlCharacterData(XmlCharacterData node, SafeXmlDocument doc)
            : base(node, doc) { }
    }
}
