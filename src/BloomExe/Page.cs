using System;
using System.Drawing;
using System.Xml;

namespace Bloom
{
	public interface IPage
	{
		string Id { get; }
		string Caption { get; }
		Image Thumbnail { get; }
		string XPathToDiv { get; }
		XmlNode GetDivNodeForThisPage();
	}

	public class Page : IPage
	{
		private readonly string _id;
		private readonly Func<IPage, Image> _getThumbnail;
		private readonly Func<IPage, XmlNode> _getDivNodeForThisPageMethod;

		public Page(XmlElement sourcePage,  string caption, Func<IPage, Image> getThumbnail, Func<IPage, XmlNode> getDivNodeForThisPageMethod)
		{
			_id = sourcePage.Attributes["id"].Value;
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

		public static string GetPageSelectorXPath(XmlDocument pageDom)
		{
			var id = pageDom.SelectSingleNodeHonoringDefaultNS("/html/body/div").Attributes["id"].Value;
			return string.Format("/html/body/div[@id='{0}']", id);
		}
	}
}