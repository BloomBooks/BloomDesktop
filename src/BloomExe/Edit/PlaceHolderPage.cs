using System.Collections.Generic;
using System.Drawing;
using System.Xml;
using Bloom.Book;
using Bloom.SafeXml;

namespace Bloom.Edit
{
    /// <summary>
    /// This is just so the first (top-left) thumbnail is empty, so that the cover page appears in the second column.
    /// </summary>
    public class PlaceHolderPage : IPage
    {
        public string Id
        {
            get { return null; }
        }

        public string Caption
        {
            get { return null; }
        }

        public string CaptionI18nId
        {
            get { return null; }
        }

        public string XPathToDiv
        {
            get { return null; }
        }

        public SafeXmlElement GetDivNodeForThisPage()
        {
            return null;
        }

        public Dictionary<string, string> GetSourceTexts(string groupId, string vernacularCode)
        {
            return new Dictionary<string, string>();
        }

        public bool Required
        {
            get { return true; }
        }

        public bool CanRelocate
        {
            get { return false; }
        }

        public Book.Book Book
        {
            get { return null; }
            set { }
        }

        public bool IsBackMatter
        {
            get { return false; }
        }

        public string GetCaptionOrPageNumber(ref int pageNumber, out string captionI18nId)
        {
            captionI18nId = null;
            return Caption;
        }

        public int GetIndex()
        {
            return -1;
        }

        public string IdOfFirstAncestor
        {
            get { return null; }
        }

        public bool IsXMatter
        {
            get { return false; }
        }

        public bool IsCoverPage
        {
            get { return false; }
        }

        public bool IsFrontCoverPage
        {
            get { return false; }
        }
    }
}
