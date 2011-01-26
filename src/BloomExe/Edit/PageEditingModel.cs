using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Palaso.UI.WindowsForms.ImageToolbox;
using Palaso.Xml;

namespace Bloom.Edit
{
	public class PageEditingModel
	{
		public void ChangePicture(string bookFolderPath, XmlDocument dom, string id, PalasoImage imageInfo)
		{

			var matches = dom.SafeSelectNodes("//img[@id='" + id + "']");
			XmlElement img = matches[0] as XmlElement;

			var imageFileName = ProcessAndCopyImage(imageInfo, bookFolderPath);

			img.SetAttribute("src", imageFileName);

		}

		/// <summary>
		/// Makes the image png if it's not a jpg, makes white transparent, and saves in the book's folder.
		/// Replaces any file with the same name.
		/// </summary>
		/// <returns>The name of the file, now in the book's folder.</returns>
		private string ProcessAndCopyImage(PalasoImage imageInfo, string bookFolderPath)
		{
			try
			{
				using (Bitmap image = new Bitmap(imageInfo.Image))//review: do we really need to copy it?
				{
					image.MakeTransparent(Color.White); //make white look realistic against background

					var isJpeg = ShouldSaveAsJpeg(imageInfo);

					var imageFileName = Path.GetFileNameWithoutExtension(imageInfo.FileName) + (isJpeg ? ".jpg" : ".png");
					var dest = Path.Combine(bookFolderPath, imageFileName);
					if (File.Exists(dest))
						File.Delete(dest);
					image.Save(dest, isJpeg ? ImageFormat.Jpeg : ImageFormat.Png);
					return imageFileName;
				}
			}
			catch (Exception error)
			{
				//the error is often "out of memory" which is completely misleading, so let's just wrap it.
				throw new ApplicationException("Error processing image: " + imageInfo.FileName, error);
			}
		}


		private bool ShouldSaveAsJpeg(PalasoImage imageInfo)
		{
			return ImageFormat.Jpeg.Guid == imageInfo.Image.RawFormat.Guid
				|| new []{"jpg", "jpeg"}.Contains(Path.GetExtension(imageInfo.FileName).ToLower());
		}
	}
}
