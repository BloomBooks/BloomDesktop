using System.Xml;

namespace Bloom.SafeXml
{
    // The EpubMaker.CheckForEpubProperties method uses this class.  I couldn't see any way around it.
    // But this is a minimal implementation that should be safe and sufficient.
    public class SafeXmlAttribute : SafeXmlNode
    {
        public SafeXmlAttribute(XmlAttribute node, SafeXmlDocument doc)
            : base(node, doc) { }
    }
}
