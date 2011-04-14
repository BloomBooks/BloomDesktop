using System.Collections.Generic;
using System.Drawing;
using System.Xml;

namespace Bloom
{
	/// <summary>
	/// This is just so the first (top-left) thumbnail is empty, so that the cover page appears in the second column.
	/// </summary>
	public class PlaceHolderPage     : IPage
	{
		public string Id
		{
			get { return null; }
		}

		public string Caption
		{
			get { return null; }
		}

		public Image Thumbnail
		{
			get { return new Bitmap(32,32); }
		}

		public string XPathToDiv
		{
			get { return null; }
		}

		public XmlNode GetDivNodeForThisPage()
		{
			return null;
		}

		public Dictionary<string, string> GetSourceTexts(string idOfOneTextAreaInTheGroup)
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
	}
}