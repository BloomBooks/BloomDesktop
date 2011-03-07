using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Xml;
using Palaso.Xml;

namespace Bloom
{
	public interface IPage
	{
		string Id { get; }
		string Caption { get; }
		Image Thumbnail { get; }
		string XPathToDiv { get; }
		XmlNode GetDivNodeForThisPage();
		Dictionary<string,string> GetSourceTexts(string textAreaId);
	}

	public class Page : IPage
	{
		private readonly string _id;
		private readonly XmlElement _sourcePage;
		private readonly Func<IPage, Image> _getThumbnail;
		private readonly Func<IPage, XmlNode> _getDivNodeForThisPageMethod;

		public Page(XmlElement sourcePage,  string caption, Func<IPage, Image> getThumbnail, Func<IPage, XmlNode> getDivNodeForThisPageMethod)
		{
			_id = sourcePage.Attributes["id"].Value;
			_sourcePage = sourcePage;
			_getThumbnail = getThumbnail;
			_getDivNodeForThisPageMethod = getDivNodeForThisPageMethod;
			Caption = caption;
		}

		public string Id{get { return _id; }}

		public string Caption { get; private set; }
		public Image Thumbnail { get { return _getThumbnail(this); } }

		public string XPathToDiv
		{
			get { return "/html/body/div[@id='"+_id+"']";}
		}

		public XmlNode GetDivNodeForThisPage()
		{
			return _getDivNodeForThisPageMethod(this);
		}

		public Dictionary<string, string> GetSourceTexts(string textAreaId)
		{
			var d = new Dictionary<string, string>();

			var textareas = _sourcePage.SafeSelectNodes(string.Format("//div[@id='{0}']//textarea[@id='{1}']", _sourcePage.GetAttribute("id"), textAreaId));
			foreach (XmlElement textarea in textareas)
			{
				var lang = textarea.GetAttribute("lang");
				if (string.IsNullOrEmpty(lang))
					continue;
				Debug.Assert(!d.ContainsKey(lang), "There is more than one textarea with "+lang);
				d.Add(lang, textarea.InnerText);
			}

			return d;
		}

		public static string GetPageSelectorXPath(XmlDocument pageDom)
		{
			var id = pageDom.SelectSingleNodeHonoringDefaultNS("/html/body/div").Attributes["id"].Value;
			return string.Format("/html/body/div[@id='{0}']", id);
		}
	}
}