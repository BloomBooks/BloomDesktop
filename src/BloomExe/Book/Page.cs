using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Xml;
using Bloom.SafeXml;
using SIL.Code;

namespace Bloom.Book
{
    public interface IPage
    {
        string Id { get; }
        string Caption { get; }
        string CaptionI18nId { get; }
        string XPathToDiv { get; }
        SafeXmlElement GetDivNodeForThisPage();
        bool Required { get; }
        bool CanRelocate { get; }
        Book Book { get; set; }
        bool IsBackMatter { get; }
        bool IsXMatter { get; }
        bool IsCoverPage { get; }
        string GetCaptionOrPageNumber(ref int pageNumber, out string captionI18nId);
        int GetIndex();
        string IdOfFirstAncestor { get; }
    }

    public class Page : IPage
    {
        private readonly string _id;
        private readonly Func<IPage, SafeXmlElement> _getDivNodeForThisPageMethod;
        private List<string> _classes;
        private List<string> _tags;
        private string[] _pageLineage;

        public Page(
            Book book,
            SafeXmlElement sourcePage,
            string caption,
            string captionI18nId,
            Func<IPage, SafeXmlElement> getDivNodeForThisPageMethod
        )
        {
            sourcePage = EnsureID(sourcePage);
            _id = FixPageId(sourcePage.GetAttribute("id"));
            var lineage = sourcePage.GetAttribute("data-pagelineage");
            _pageLineage = lineage.Split(new[] { ',', ';' });

            Guard.AgainstNull(book, "Book");
            Book = book;
            _getDivNodeForThisPageMethod = getDivNodeForThisPageMethod;
            Caption = caption;
            CaptionI18nId = captionI18nId;
            ReadClasses(sourcePage);
            ReadPageTags(sourcePage);
        }

        private SafeXmlElement EnsureID(SafeXmlElement sourcePage)
        {
            if (!sourcePage.HasAttribute("id"))
            {
                // Probably a unit test. If not, maybe the book got mangled some other way, but we don't want to leave
                // the page mangled, so add a new page guid.
                var guid = Guid.NewGuid();
                sourcePage.SetAttribute("id", guid.ToString());
            }
            return sourcePage;
        }

        //in the beta, 0.8, the ID of the page in the front-matter template was used for the 1st
        //page of every book. This screws up thumbnail caching.
        private string FixPageId(string id)
        {
            //Note: there were 4 other xmatter pages with teh same problem, but I'm only fixing
            //the cover page one a the moment. We've solved the larger problem for new books (or those
            //with rebuilt front matter).
            const string guidMistakenlyUsedForEveryCoverPage =
                "74731b2d-18b0-420f-ac96-6de20f659810";
            if (id == guidMistakenlyUsedForEveryCoverPage)
            {
                return Guid.NewGuid().ToString();
            }
            return id;
        }

        private void ReadClasses(SafeXmlElement sourcePage)
        {
            _classes = sourcePage.GetClasses().ToList(); // Enhance: can we just make it an array?
        }

        private void ReadPageTags(SafeXmlElement sourcePage)
        {
            _tags = new List<string>();
            var tags = sourcePage.GetAttribute("data-page");
            if (!string.IsNullOrEmpty(tags))
            {
                _tags.AddRange(
                    tags.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                );
            }
        }

        public bool Required
        {
            get { return _tags.Contains("required"); }
        }

        public bool CanRelocate
        {
            get
            {
                if (Required)
                    //review: for now, we're conflating "required" with "can't move"
                    return false; // front and back matter and similar can't move
                return true;
            }
        }

        public Book Book { get; set; }

        public bool IsBackMatter
        {
            get { return XMatterHelper.IsBackMatterPage(_getDivNodeForThisPageMethod(this)); }
        }

        public bool IsFrontMatter
        {
            get { return XMatterHelper.IsFrontMatterPage(_getDivNodeForThisPageMethod(this)); }
        }

        public bool IsXMatter
        {
            get { return IsBackMatter || IsFrontMatter; }
        }

        public bool IsCoverPage
        {
            get { return XMatterHelper.IsCoverPage(_getDivNodeForThisPageMethod(this)); }
        }

        public string GetCaptionOrPageNumber(ref int pageNumber, out string captionI18nId)
        {
            string outerXml = _getDivNodeForThisPageMethod(this).OuterXml;

            //at the moment, I can't remember why this is even needed (it works fine without it), but we might as well honor it in code
            if (outerXml.Contains("bloom-startPageNumbering"))
            {
                pageNumber = 1;
            }
            if (
                outerXml.Contains("numberedPage")
                || outerXml.Contains("countPageButDoNotShowNumber")
            )
            {
                pageNumber++;
            }
            if (outerXml.Contains("numberedPage"))
            {
                captionI18nId = pageNumber.ToString();
                return pageNumber.ToString();
            }
            // This phrase is too long to use as a page label.
            // We can generalize this if we get others.
            if (Caption == "Comprehension Questions")
            {
                Caption = "Quiz";
            }
            if (CaptionI18nId == null)
            {
                if (string.IsNullOrEmpty(Caption))
                    captionI18nId = null;
                else
                    captionI18nId = "TemplateBooks.PageLabel." + Caption;
            }
            else
                captionI18nId = CaptionI18nId;
            return Caption;
        }

        public string Id
        {
            get { return _id; }
        }

        public string Caption { get; private set; }
        public string CaptionI18nId { get; private set; }

        public string XPathToDiv
        {
            get { return "/html/body/div[@id='" + _id + "']"; }
        }

        public SafeXmlElement GetDivNodeForThisPage()
        {
            return _getDivNodeForThisPageMethod(this);
        }

        /// <summary>
        /// Return the index of this page in the IEnumerable of pages
        /// </summary>
        /// <returns>Index of the page, or -1 if the page was not found</returns>
        public int GetIndex()
        {
            var i = 0;
            foreach (var page in Book.GetPages())
            {
                if (page == this)
                    return i;
                i++;
            }

            return -1;
        }

        public string IdOfFirstAncestor
        {
            get { return _pageLineage.FirstOrDefault(); }
        }

        internal void UpdateLineage(string[] lineage)
        {
            _pageLineage = lineage;
        }
    }
}
