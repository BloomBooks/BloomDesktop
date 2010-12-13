using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
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

			if(Path.GetExtension(imageFullPath).ToLower().StartsWith(".tif"))
			{
				using (var image = Image.FromFile(imageFullPath))
				{
					imageFileName = Path.GetFileNameWithoutExtension(imageFileName) + ".png";
					var dest = Path.Combine(bookFolderPath, imageFileName);
					image.Save(dest, ImageFormat.Png);
				}
			}
			else
			{
				var dest = Path.Combine(bookFolderPath, imageFileName);
				File.Copy(imageFullPath, dest, true);
			}
			img.SetAttribute("src", imageFileName);

		}
	}
}
