using System.Xml;

namespace Bloom.SafeXml
{
    // The class in the file is not used in the Bloom project except in unit tests
    // which don't need more than this bare skeleton.
    public class SafeXmlWhitespace : SafeXmlCharacterData
    {
        public SafeXmlWhitespace(XmlWhitespace node, SafeXmlDocument doc) : base(node, doc)
        {
        }
    }
}
