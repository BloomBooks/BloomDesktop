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
		XmlNode GetDivNode();
	}

	public class Page : IPage
	{
		private readonly Func<Image> _getThumbnail;
		private readonly Func<XmlNode> _getDivNodeMethod;

		public Page(string id, string caption, Func<Image> GetThumbnail, Func<XmlNode> GetDivNodeMethod)
		{
			_getThumbnail = GetThumbnail;
			_getDivNodeMethod = GetDivNodeMethod;
			Id = id;
			Caption = caption;
		}


		public string Id {get;private set;}
		public string Caption { get; private set; }
		public Image Thumbnail { get { return _getThumbnail(); } }

		public XmlNode GetDivNode()
		{
			return _getDivNodeMethod();
		}
	}
}