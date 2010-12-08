using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Palaso.Xml;

namespace Bloom.Edit
{
	public class PageEditingModel
	{
		public void ChangePicture(string bookFolderPath, XmlDocument dom, string id, string imageFullPath)
		{
			var imageFileName = Path.GetFileName(imageFullPath);

			var matches = dom.SafeSelectNodes("//img[@id='" + id + "']");
			XmlElement img = matches[0] as XmlElement;

			img.SetAttribute("src", imageFileName);

			var dest = Path.Combine(bookFolderPath, imageFileName);
			File.Copy(imageFullPath, dest, true);
		}
	}
}
