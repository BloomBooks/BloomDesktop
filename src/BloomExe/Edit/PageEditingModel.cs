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
using Gecko;

namespace Bloom.Edit
{
	public class PageEditingModel
	{
		public void ChangePicture(string bookFolderPath, XmlDocument dom, GeckoElement img, PalasoImage imageInfo)
		{
			var imageFileName = ProcessAndCopyImage(imageInfo, bookFolderPath);
			img.SetAttribute("src", imageFileName);
			UpdateMetdataAttributesOnImgElement(img, imageInfo);
		}

		/// <summary>
		/// for testing.... todo: maybe they should test ProcessAndCopyImage() directly, instead
		/// </summary>
		public void ChangePicture(string bookFolderPath, XmlDocument dom, string imageId, PalasoImage imageInfo)
		{

			var matches = dom.SafeSelectNodes("//img[@id='" + imageId + "']");
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
			var isJpeg = ShouldSaveAsJpeg(imageInfo);
			try
			{

				using (Bitmap image = new Bitmap(imageInfo.Image))//nb: there are cases (undefined) where we get out of memory if we are not operating on a copy
				{


					//photographs don't work if you try to make the white transparent
					if(!isJpeg && image is Bitmap)
						((Bitmap)image).MakeTransparent(Color.White); //make white look realistic against background


					string imageFileName = GetImageFileName(bookFolderPath, imageInfo, isJpeg);
					var dest = Path.Combine(bookFolderPath, imageFileName);
					if (File.Exists(dest))
					{
						try
						{
							File.Delete(dest);
						}
						catch (System.IO.IOException error)
						{
							throw new ApplicationException("Bloom could not replace the image "+imageFileName+", probably because Bloom itself has it locked.");
						}
					}
					image.Save(dest, isJpeg ? ImageFormat.Jpeg : ImageFormat.Png);
					imageInfo.Metadata.Write(dest);
					return imageFileName;
				}
			}
			catch (System.IO.IOException)
			{
				throw; //these are informative on their own
			}
			catch (Exception error)
			{
				if (!string.IsNullOrEmpty(imageInfo.FileName) && File.Exists(imageInfo.OriginalFilePath))
				{
					var megs = new System.IO.FileInfo(imageInfo.OriginalFilePath).Length / (1024 * 1000);
					if (megs > 2)
					{
						var msg = string.Format("Bloom was not able to prepare that picture for including in the book. \r\nThis is a rather large image to be adding to a book --{0} Megs--.", megs);
						if(isJpeg)
						{
							msg += "\r\nNote, this file is a jpeg, which is normally used for photographs, not line-drawings (png, tiff, bmp). Bloom can handle smallish jpegs, large ones are difficult to handle, especialy if memory is limitted.";
						}
						throw new ApplicationException(msg, error);
					}
				}

				throw new ApplicationException("Bloom was not able to prepare that picture for including in the book. Is it too large, or an odd format?\r\n" + imageInfo.FileName, error);
			}
		}


		private static string GetImageFileName(string bookFolderPath, PalasoImage imageInfo, bool isJpeg)
		{
			string s;
			if(string.IsNullOrEmpty(imageInfo.FileName))
			{
				// Some images, like from a scanner or camera, won't have a name yet.  Some will need a number
				// in order to differentiate from what is already there. We don't try and be smart somehow and
				// know when to just replace the existing one with the same name... some other process will have
				// to remove unused images.

				s = "image";
				int i = 0;
				string suffix = "";
				string extension = isJpeg ? ".jpg" : ".png";

				while (File.Exists(Path.Combine(bookFolderPath, s + suffix + extension)))
				{
					++i;
					suffix = i.ToString();
				}

				return s + suffix + extension;
			}
			else
			{
				var extension = isJpeg ? ".jpg" : ".png";
				return Path.GetFileNameWithoutExtension(imageInfo.FileName) + extension;
			}
		}


		private bool ShouldSaveAsJpeg(PalasoImage imageInfo)
		{
			if(ImageFormat.Jpeg.Guid == imageInfo.Image.RawFormat.Guid)
				return true;

			if(ImageFormat.Jpeg.Equals(imageInfo.Image.PixelFormat))//review
				return true;

			if(string.IsNullOrEmpty(imageInfo.FileName))
				return false;

			return  new []{"jpg", "jpeg"}.Contains(Path.GetExtension(imageInfo.FileName).ToLower());
		}

		public void UpdateMetdataAttributesOnImgElement(GeckoElement img, PalasoImage imageInfo)
		{
			img.SetAttribute("data-copyright",
							 string.IsNullOrEmpty(imageInfo.Metadata.CopyrightNotice) ? "" : imageInfo.Metadata.CopyrightNotice);

			img.SetAttribute("data-creator", string.IsNullOrEmpty(imageInfo.Metadata.Creator) ? "" : imageInfo.Metadata.Creator);


			img.SetAttribute("data-license", imageInfo.Metadata.License == null ? "" : imageInfo.Metadata.License.ToString());

			img.Click(); //wake up javascript to update overlays
		}
	}
}
