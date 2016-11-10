using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Xml;
using SIL.Code;

namespace Bloom.Book
{
	public interface IPage
	{
		string Id { get; }
		string Caption { get; }
		string CaptionI18nId { get; }
		Image Thumbnail { get; }
		string XPathToDiv { get; }
		XmlElement GetDivNodeForThisPage();
		bool Required { get; }
		bool CanRelocate { get;}
		Book Book { get; set; }
		bool IsBackMatter { get; }
		string GetCaptionOrPageNumber(ref int pageNumber, out string captionI18nId);
		int GetIndex();
		string IdOfFirstAncestor { get;}
	}

	public class Page : IPage
	{
		private readonly string _id;
		private readonly Func<IPage, Image> _getThumbnail;
		private readonly Func<IPage, XmlElement> _getDivNodeForThisPageMethod;
		private List<string> _classes;
		private List<string> _tags;
		private string[] _pageLineage;

		public Page(Book book, XmlElement sourcePage,  string caption, string captionI18nId, /*Func<IPage, Image> getThumbnail,*/ Func<IPage, XmlElement> getDivNodeForThisPageMethod)
		{
			_id = FixPageId(sourcePage.Attributes["id"].Value);
			var lineage = sourcePage.Attributes["data-pagelineage"];
			_pageLineage = lineage == null ? new string[] {} : lineage.Value.Split(new[] { ',' });

			Guard.AgainstNull(book,"Book");
			Book = book;
			_getDivNodeForThisPageMethod = getDivNodeForThisPageMethod;
			Caption = caption;
			CaptionI18nId = captionI18nId;
			ReadClasses(sourcePage);
			ReadPageTags(sourcePage);
		}

		//in the beta, 0.8, the ID of the page in the front-matter template was used for the 1st
		//page of every book. This screws up thumbnail caching.
		private string FixPageId(string id)
		{
			//Note: there were 4 other xmatter pages with teh same problem, but I'm only fixing
			//the cover page one a the moment. We've solved the larger problem for new books (or those
			//with rebuilt front matter).
			const string guidMistakenlyUsedForEveryCoverPage = "74731b2d-18b0-420f-ac96-6de20f659810";
			if (id == guidMistakenlyUsedForEveryCoverPage)
			{
				return Guid.NewGuid().ToString();
			}
			return id;
		}

//
//    	private void ReadPageLabel(XmlElement sourcePage)
//    	{
//    		PageLabel = "Foobar";
//    	}

//    	protected string PageLabel { get; set; }

		private void ReadClasses(XmlElement sourcePage)
		{
			_classes = new List<string>();
			var classesString = sourcePage.GetAttribute("class");
			if (!string.IsNullOrEmpty(classesString))
			{
				_classes.AddRange(classesString.Split(new char[]{' '},StringSplitOptions.RemoveEmptyEntries));
			}
		}
		private void ReadPageTags(XmlElement sourcePage)
		{
			_tags = new List<string>();
			var tags = sourcePage.GetAttribute("data-page");
			if (!string.IsNullOrEmpty(tags))
			{
				_tags.AddRange(tags.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
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
				if(Required)
					//review: for now, we're conflating "required" with "can't move"
					return false; // front and back matter and similar can't move
				// For now, can't move pages while translating a book.
				// Enhance: possibly we may want to allow moving pages ADDED to the original book?
				if (Book.LockedDown)
					return false;
				return true;
			}
		}

		public Book Book { get; set; }

		public bool IsBackMatter
		{
			get
			{
				return XMatterHelper.IsBackMatterPage(_getDivNodeForThisPageMethod(this));
			}
		}

		public string GetCaptionOrPageNumber(ref int pageNumber, out string captionI18nId)
		{
			string outerXml = _getDivNodeForThisPageMethod(this).OuterXml;

			//at the moment, I can't remember why this is even needed (it works fine without it), but we might as well honor it in code
			if (outerXml.Contains("bloom-startPageNumbering"))
			{
				pageNumber = 1;
			}
			if (outerXml.Contains("numberedPage") || outerXml.Contains("countPageButDoNotShowNumber"))
			{
				pageNumber++;
			}
			if(outerXml.Contains("numberedPage"))
			{
				captionI18nId = pageNumber.ToString();
				return pageNumber.ToString();
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

		public string Id{get { return _id; }}

		public string Caption { get; private set; }
		public string CaptionI18nId { get; private set; }
		public Image Thumbnail { get
		{ return _getThumbnail(this); } }

		public string XPathToDiv
		{
			get { return "/html/body/div[@id='"+_id+"']";}
		}

		public XmlElement GetDivNodeForThisPage()
		{
			return _getDivNodeForThisPageMethod(this);
		}


		public static string GetPageSelectorXPath(XmlDocument pageDom)
		{
//    		var id = pageDom.SelectSingleNodeHonoringDefaultNS("/html/body/div").Attributes["id"].Value;
			var id = pageDom.SelectSingleNode("/html/body/div").Attributes["id"].Value;
			return string.Format("/html/body/div[@id='{0}']", id);
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
				if (page == this) return i;
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