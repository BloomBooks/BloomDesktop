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

//            if(Path.GetExtension(imageFullPath).ToLower().StartsWith(".tif"))
//            {
				imageFileName = MakeImagePngAndTranparent(imageFullPath, imageFileName, bookFolderPath);
//            }
//            else
//            {
//                var dest = Path.Combine(bookFolderPath, imageFileName);
//                File.Copy(imageFullPath, dest, true);
//            }
			img.SetAttribute("src", imageFileName);

		}

		private string MakeImagePngAndTranparent(string imageFullPath, string imageFileName, string bookFolderPath)
		{
			using (Bitmap image = ((Bitmap)Bitmap.FromFile(imageFullPath)))
			{
				imageFileName = Path.GetFileNameWithoutExtension(imageFileName) + ".png";
				var dest = Path.Combine(bookFolderPath, imageFileName);
				image.MakeTransparent(Color.White);//make white look realistic against background
				if(File.Exists(dest))
					File.Delete(dest);
				image.Save(dest, ImageFormat.Png);
			}
			return imageFileName;
		}
	}
}
