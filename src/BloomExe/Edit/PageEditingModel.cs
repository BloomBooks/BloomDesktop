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
using Skybound.Gecko;

namespace Bloom.Edit
{
	public class PageEditingModel
	{
		public void ChangePicture(string bookFolderPath, XmlDocument dom, GeckoElement img, PalasoImage imageInfo)
		{
			var imageFileName = ProcessAndCopyImage(imageInfo, bookFolderPath);
			img.SetAttribute("src", imageFileName);
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
			try
			{
				using (Bitmap image = new Bitmap(imageInfo.Image))//review: do we really need to copy it?
				{
					var isJpeg = ShouldSaveAsJpeg(imageInfo);

					//photographs don't work if you try to make the white transparent
					if(!isJpeg)
						image.MakeTransparent(Color.White); //make white look realistic against background


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
			catch (ApplicationException)
			{
				throw; //these are informative on their own
			}
			catch (Exception error)
			{
				//the error is often "out of memory" which is completely misleading, so let's just wrap it.
				throw new ApplicationException("Error processing image: " + imageInfo.FileName, error);
			}
		}

		/// <summary>
		/// Some images, like from a scanner or camera, won't have a name yet.  Some will need a number
		/// in order to differentiate from what is already there. We don't try and be smart somehow and
		/// know when to just replace the existing one with the same name... some other process will have
		/// to remove unused images.
		/// </summary>
		private static string GetImageFileName(string bookFolderPath, PalasoImage imageInfo, bool isJpeg)
		{
			string s;
			if(string.IsNullOrEmpty(imageInfo.FileName))
			{
				s = "image";
			}
			else
			{
				s = Path.GetFileNameWithoutExtension(imageInfo.FileName);
			}

			int i = 0;
			string suffix = "";
			string extension = isJpeg ? ".jpg" : ".png";
			while (File.Exists(Path.Combine(bookFolderPath, s + suffix+extension)))
			{
				++i;
				suffix = i.ToString();
			}

			return s + suffix + extension;
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
	}
}
