using System;
using System.Drawing;

namespace Bloom
{
	public class Page
	{
		public Page(string id, string caption, Image thumbnail)
		{
			Id = id;
			Caption = caption;
			Thumbnail = thumbnail;
		}


		public string Id {get;private set;}
		public string Caption { get; private set; }
		public Image Thumbnail { get; private set; }

		public string GetHtmlOfDiv()
		{
			throw new NotImplementedException();
		}
	}
}