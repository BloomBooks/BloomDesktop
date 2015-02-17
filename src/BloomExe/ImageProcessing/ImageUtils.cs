using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using Palaso.Progress;
using Palaso.UI.WindowsForms.ImageToolbox;
using Logger = Palaso.Reporting.Logger;
using TempFile = Palaso.IO.TempFile;

namespace Bloom.ImageProcessing
{
	class ImageUtils
	{
		public static bool AppearsToBeJpeg(PalasoImage imageInfo)
		{
			/*
			 * Note, each guid is VERY SIMILAR. The difference is only in the last 2 digits of the 1st group.
			   Undefined  B96B3CA9
				MemoryBMP  B96B3CAA
				BMP    B96B3CAB
				EMF    B96B3CAC
				WMF    B96B3CAD
				JPEG    B96B3CAE
				PNG    B96B3CAF
				GIF    B96B3CB0
				TIFF    B96B3CB1
				EXIF    B96B3CB2
				Icon    B96B3CB5
			 */
			if(ImageFormat.Jpeg.Guid == imageInfo.Image.RawFormat.Guid)
				return true;

			if(ImageFormat.Jpeg.Equals(imageInfo.Image.PixelFormat))//review
				return true;

			if(string.IsNullOrEmpty(imageInfo.FileName))
				return false;

			return new[] { ".jpg", ".jpeg" }.Contains(Path.GetExtension(imageInfo.FileName).ToLower());
		}

		/// <summary>
		/// Makes the image a png if it's not a jpg and saves in the book's folder.
		/// If the image has a filename, replaces any file with the same name.
		/// </summary>
		/// <returns>The name of the file, now in the book's folder.</returns>
		public static string ProcessAndSaveImageIntoFolder(PalasoImage imageInfo, string bookFolderPath)
		{
			LogMemoryUsage();
			bool isJpeg = false;
			try
			{
				isJpeg = AppearsToBeJpeg(imageInfo);
				var imageFileName = GetFileNameToUseForSavingImage(bookFolderPath, imageInfo, isJpeg);
				var destinationPath = Path.Combine(bookFolderPath, imageFileName);
				imageInfo.Save(destinationPath);
				return imageFileName;

				/* I (Hatton) have decided to stop compressing images until we have a suite of
				tests based on a number of image exemplars. Compression can be great, but it
				can also lead to very long waits; this is a "first, do no harm" decision.

				//nb: there are cases (undefined) where we get out of memory if we are not operating on a copy
				using (var image = new Bitmap(imageInfo.Image))
				{
					using (var tmp = new TempFile())
					{
						image.Save(tmp.Path, isJpeg ? ImageFormat.Jpeg : ImageFormat.Png);
						Palaso.IO.FileUtils.ReplaceFileWithUserInteractionIfNeeded(tmp.Path, destinationPath, null);
					}

				}

				using (var dlg = new ProgressDialogBackground())
				{
					dlg.ShowAndDoWork((progress, args) => ImageUpdater.CompressImage(dest, progress));
				}*/
			}
			catch (IOException)
			{
				throw; //these are informative on their own
			}
				/* No. OutOfMemory is almost meaningless when it comes to image errors. Better not to confuse people
			 * catch (OutOfMemoryException error)
			{
				//Enhance: it would be great if we could bring up that problem dialog ourselves, and offer this picture as an attachment
				throw new ApplicationException("Bloom ran out of memory while trying to import the picture. We suggest that you quit Bloom, run it again, and then try importing this picture again. If that fails, please go to the Help menu and choose 'Report a Problem'", error);
			}*/
			catch (Exception error)
			{
				if (!string.IsNullOrEmpty(imageInfo.FileName) && File.Exists(imageInfo.OriginalFilePath))
				{
					var megs = new System.IO.FileInfo(imageInfo.OriginalFilePath).Length/(1024*1000);
					if (megs > 2)
					{
						var msg =
							string.Format(
								"Bloom was not able to prepare that picture for including in the book. \r\nThis is a rather large image to be adding to a book --{0} Megs--.",
								megs);
						if (isJpeg)
						{
							msg +=
								"\r\nNote, this file is a jpeg, which is normally used for photographs, not line-drawings (png, tiff, bmp). Bloom can handle smallish jpegs, large ones are difficult to handle, especialy if memory is limited.";
						}
						throw new ApplicationException(msg, error);
					}
				}

				throw new ApplicationException(
					"Bloom was not able to prepare that picture for including in the book. We'd like to investigate, so if possible, would you please email it to issues@bloomlibrary.org?" +
					System.Environment.NewLine + imageInfo.FileName, error);
			}
		}


		private static string GetFileNameToUseForSavingImage(string bookFolderPath, PalasoImage imageInfo, bool isJpeg)
		{
			var extension = isJpeg ? ".jpg" : ".png";
			if(string.IsNullOrEmpty(imageInfo.FileName) || imageInfo.FileName.StartsWith("tmp"))
			{
				// Some images, like from a scanner or camera, won't have a name yet.  Some will need a number
				// in order to differentiate from what is already there. We don't try and be smart somehow and
				// know when to just replace the existing one with the same name... some other process will have
				// to remove unused images.

				const string s = "image";
				var i = 0;
				var suffix = "";

				while(File.Exists(Path.Combine(bookFolderPath, s + suffix + extension)))
				{
					++i;
					suffix = i.ToString(CultureInfo.InvariantCulture);
				}

				return s + suffix + extension;
			}
			else
			{
				return Path.GetFileNameWithoutExtension(imageInfo.FileName) + extension;
			}
		}

		private static void LogMemoryUsage()
		{
			using (var proc = Process.GetCurrentProcess())
			{
				const int bytesPerMegabyte = 1048576;
				Logger.WriteEvent("Paged Memory: " + proc.PagedMemorySize64 / bytesPerMegabyte + " MB");
				Logger.WriteEvent("Peak Paged Memory: " + proc.PeakPagedMemorySize64 / bytesPerMegabyte + " MB");
				Logger.WriteEvent("Peak Virtual Memory: " + proc.PeakVirtualMemorySize64 / bytesPerMegabyte + " MB");
				Logger.WriteEvent("GC Total Memory: " + GC.GetTotalMemory(false) / bytesPerMegabyte + " MB");
			}
		}

		//Up through Bloom 3.0, we would make white areas transparent when importing images, in order to make them
		//look good against the colored background of a book cover.
		//This caused problems with some PDF viewers, so in Bloom 3.1, we switched to only making them transparent at runtime.
		//This method allows us to undo that transparency-making.
		public static void RemoveTransparencyOfImagesInFolder(string folderPath, IProgress progress)
		{
			var imageFiles = Directory.GetFiles(folderPath, "*.png");
			int completed = 0;
			foreach(string path in imageFiles)
			{

				if(Path.GetFileName(path).ToLower() == "placeholder.png")
					return;

				progress.ProgressIndicator.PercentCompleted = (int)(100.0 * (float)completed / (float)imageFiles.Length);
				using(var pi = PalasoImage.FromFile(path))
				{
					if(!ImageUtils.AppearsToBeJpeg(pi))
					{
						RemoveTransparency(path, progress);
					}
				}
				completed++;
			}
		}

		private static void RemoveTransparency(string path, IProgress progress)
		{
			progress.WriteStatus("RemovingTransparency from image: " + Path.GetFileName(path));
			var original = Image.FromFile(path);
			using(var b = new Bitmap(original.Width, original.Height))
			{
				b.SetResolution(original.HorizontalResolution, original.VerticalResolution);

				using(Graphics g = Graphics.FromImage(b))
				{
					g.Clear(Color.White);
					g.DrawImageUnscaled(original, 0, 0);
				}
				original.Dispose();
				b.Save(path, ImageFormat.Png);
			}
		}
	}
}
