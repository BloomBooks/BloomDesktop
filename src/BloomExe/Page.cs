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
		XmlNode GetDivNodeForThisPage();
	}

	public class Page : IPage
	{
		private readonly Func<Image> _getThumbnail;
		private readonly Func<XmlNode> _getDivNodeForThisPageMethod;

		public Page(string id, string caption, Func<Image> getThumbnail, Func<XmlNode> getDivNodeForThisPageMethod)
		{
			_getThumbnail = getThumbnail;
			_getDivNodeForThisPageMethod = getDivNodeForThisPageMethod;
			Id = id;
			Caption = caption;
		}


		public string Id {get;private set;}
		public string Caption { get; private set; }
		public Image Thumbnail { get { return _getThumbnail(); } }

		public XmlNode GetDivNodeForThisPage()
		{
			return _getDivNodeForThisPageMethod();
		}
	}
}