using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using SIL.IO;
using SIL.Progress;
using SIL.Windows.Forms.ImageToolbox;
using Logger = SIL.Reporting.Logger;
using TempFile = SIL.IO.TempFile;

namespace Bloom.ImageProcessing
{
	class ImageUtils
	{
		public static bool AppearsToBeJpeg(PalasoImage imageInfo)
		{
			// A user experienced a crash due to a null object in this section of the code.
			// I've added a couple of checks to prevent that kind of crash here.
			if (imageInfo == null || imageInfo.Image == null)
				return false;
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
			if(imageInfo.Image.RawFormat != null && ImageFormat.Jpeg.Guid == imageInfo.Image.RawFormat.Guid)
				return true;

			if(ImageFormat.Jpeg.Equals(imageInfo.Image.PixelFormat))//review
				return true;

			if(string.IsNullOrEmpty(imageInfo.FileName))
				return false;

			return new[] { ".jpg", ".jpeg" }.Contains(Path.GetExtension(imageInfo.FileName).ToLowerInvariant());
		}

		/// <summary>
		/// Makes the image a png if it's not a jpg and saves in the book's folder.
		/// If the image has a filename, replaces any file with the same name.
		/// 
		/// WARNING: imageInfo.Image could be replaced (causing the original to be disposed)
		/// </summary>
		/// <returns>The name of the file, now in the book's folder.</returns>
		public static string ProcessAndSaveImageIntoFolder(PalasoImage imageInfo, string bookFolderPath, bool isSameFile)
		{
			LogMemoryUsage();
			bool isEncodedAsJpeg = false;
			try
			{
				isEncodedAsJpeg = AppearsToBeJpeg(imageInfo);
				if (!isEncodedAsJpeg)
				{
					// The original imageInfo.Image is disposed of in the setter.
					// As of now (9/2016) this is safe because there are no other references to it higher in the stack.
					imageInfo.Image = CreateImageWithoutTransparentBackground(imageInfo.Image);
				}

				var shouldConvertToJpeg = !isEncodedAsJpeg && ShouldChangeFormatToJpeg(imageInfo.Image);
				string imageFileName;
				if (!shouldConvertToJpeg && isSameFile)
					imageFileName = imageInfo.FileName;
				else
					imageFileName = GetFileNameToUseForSavingImage(bookFolderPath, imageInfo, isEncodedAsJpeg || shouldConvertToJpeg);

				if (!Directory.Exists(bookFolderPath))
					throw new DirectoryNotFoundException(bookFolderPath + " does not exist");

				var destinationPath = Path.Combine(bookFolderPath, imageFileName);
				if (shouldConvertToJpeg)
				{
					SaveAsTopQualityJpeg(imageInfo.Image, destinationPath);
				}
				RobustIO.SavePalasoImage(imageInfo, destinationPath);

				return imageFileName;

				/* I (Hatton) have decided to stop compressing images until we have a suite of
				tests based on a number of image exemplars. Compression can be great, but it
				can also lead to very long waits; this is a "first, do no harm" decision.

				//nb: there are cases (undefined) where we get out of memory if we are not operating on a copy
				using (var image = new Bitmap(imageInfo.Image))
				{
					using (var tmp = new TempFile())
					{
						SIL.IO.RobustIO.SaveImage(image, tmp.Path, isJpeg ? ImageFormat.Jpeg : ImageFormat.Png);
						SIL.IO.FileUtils.ReplaceFileWithUserInteractionIfNeeded(tmp.Path, destinationPath, null);
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
				if (!string.IsNullOrEmpty(imageInfo.FileName) && RobustFile.Exists(imageInfo.OriginalFilePath))
				{
					var megs = new System.IO.FileInfo(imageInfo.OriginalFilePath).Length/(1024*1000);
					if (megs > 2)
					{
						var msg =
							string.Format(
								"Bloom was not able to prepare that picture for including in the book. \r\nThis is a rather large image to be adding to a book --{0} Megs--.",
								megs);
						if (isEncodedAsJpeg)
						{
							msg +=
								"\r\nNote, this file is a jpeg, which is normally used for photographs, and complex color artwork. Bloom can handle smallish jpegs, large ones are difficult to handle, especialy if memory is limited.";
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
			// Some images, like from a scanner or camera, won't have a name yet.  Some will need a number
			// in order to differentiate from what is already there. We don't try and be smart somehow and
			// know when to just replace the existing one with the same name... some other process will have
			// to remove unused images.
			string basename;
			if (string.IsNullOrEmpty(imageInfo.FileName) || imageInfo.FileName.StartsWith("tmp"))
			{
				basename = "image";
			}
			else
			{
				// Even pictures that aren't obviously unnamed or temporary may have the same name.
				// See https://silbloom.myjetbrains.com/youtrack/issue/BL-2627 ("Weird Image Problem").
				basename = Path.GetFileNameWithoutExtension(imageInfo.FileName);
			}
			var i = 0;
			var suffix = "";
			while (RobustFile.Exists(Path.Combine(bookFolderPath, basename + suffix + extension)))
			{
				++i;
				suffix = i.ToString(CultureInfo.InvariantCulture);
			}
			return basename + suffix + extension;
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

				if (Path.GetFileName(path).ToLowerInvariant() == "placeholder.png")
					return;

				progress.ProgressIndicator.PercentCompleted = (int)(100.0 * (float)completed / (float)imageFiles.Length);
				using(var pi = PalasoImage.FromFileRobustly(path))
				{
					if (!AppearsToBeJpeg(pi))
					{
						RemoveTransparency(pi.Image, path, progress);
					}
				}
				completed++;
			}
		}

		private static void RemoveTransparency(Image original, string path, IProgress progress)
		{
			progress.WriteStatus("RemovingTransparency from image: " + Path.GetFileName(path));
			using (var b = new Bitmap(original.Width, original.Height))
			{
				DrawImageWithWhiteBackground(original, b);
				SIL.IO.RobustIO.SaveImage(b, path, ImageFormat.Png);
			}
		}

		private static Image CreateImageWithoutTransparentBackground(Image image)
		{
			var b = new Bitmap(image.Width, image.Height);
			DrawImageWithWhiteBackground(image, b);
			return b;
		}

		private static void DrawImageWithWhiteBackground(Image source, Bitmap target)
		{
			Rectangle rect = new Rectangle(Point.Empty, source.Size);
			using (Graphics g = Graphics.FromImage(target))
			{
				g.Clear(Color.White);
				g.DrawImageUnscaledAndClipped(source, rect);
			}
		}

		/// <summary>
		/// When images are copied from LibreOffice, images that were jpegs there are converted to bitmaps for the clipboard.
		/// So when we just saved them as bitmaps (pngs), we dramatically inflated the size of user's image files (and
		/// this then led to memory problems).
		/// So the idea here is just to try and detect that we should would be better off saving the image as a jpeg.
		/// Note that even at 100%, we're still going to lose some quality. So this method is only going to recommend
		/// doing that if the size would be at least 50% less.
		/// </summary>
		public static bool ShouldChangeFormatToJpeg(Image image)
		{
			try
			{
				using(var safetyImage = new Bitmap(image))
					//nb: there are cases (notably http://jira.palaso.org/issues/browse/WS-34711, after cropping a jpeg) where we get out of memory if we are not operating on a copy
				{
					using(var jpegFile = new TempFile())
					using(var pngFile = new TempFile())
					{
						SIL.IO.RobustIO.SaveImage(image, pngFile.Path, ImageFormat.Png);
						SaveAsTopQualityJpeg(safetyImage, jpegFile.Path);
						var jpegInfo = new FileInfo(jpegFile.Path);
						var pngInfo = new FileInfo(pngFile.Path);
						// this is just our heuristic. 
						const double fractionOfTheOriginalThatWouldWarrantChangingToJpeg = .5;
						return jpegInfo.Length < (pngInfo.Length*(1.0 - fractionOfTheOriginalThatWouldWarrantChangingToJpeg));
					}
				}
			}
			catch(OutOfMemoryException e)
			{
				NonFatalProblem.Report(ModalIf.Alpha, PassiveIf.All,"Could not attempt conversion to jpeg.", "ref BL-3387", exception: e);
				return false;
			}
		}

		/// <summary>
		/// Save the image (of any format) to a jpeg file with 100 quality
		/// Note that this is still going to introduce some errors if the input is a bitmap.
		/// </summary>
		/// <remarks>Will throw if the destination is locked and the user tells us to give up. </remarks>
		public static void SaveAsTopQualityJpeg(Image image, string destinationPath)
		{
			var jpgEncoder = ImageCodecInfo.GetImageDecoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
			var encoder = Encoder.Quality;

			//nb: there are cases (notably http://jira.palaso.org/issues/browse/WS-34711, after cropping a jpeg) where we get out of memory if we are not operating on a copy

			// Use a temporary file pathname in the destination folder.  This is needed to ensure proper permissions are granted
			// to the resulting file later after FileUtils.ReplaceFileWithUserInteractionIfNeeded is called.  That method may call
			// File.Replace which replaces both the file content and the file metadata (permissions).  The result of that if we use
			// the user's temp directory is described in http://issues.bloomlibrary.org/youtrack/issue/BL-3954.
			using (var temp = TempFile.InFolderOf(destinationPath))
			using (var safetyImage = new Bitmap(image))
			{
				using(var parameters = new EncoderParameters(1))
				{
					//0 = max compression, 100 = least
					parameters.Param[0] = new EncoderParameter(encoder, 100L);
					SIL.IO.RobustIO.SaveImage(safetyImage, temp.Path, jpgEncoder, parameters);
				}
				SIL.IO.FileUtils.ReplaceFileWithUserInteractionIfNeeded(temp.Path, destinationPath, null);
			}
		}

		/// <summary>
		/// Read a bitmap image from a file.  The file must be known to exist before calling this method.
		/// </summary>
		/// <remarks>
		/// Image.FromFile and Image.FromStream lock the file until the image is disposed of.  Therefore,
		/// we copy the image and dispose of the original.  On Windows, Image.FromFile leaks file handles,
		/// so we use FromStream instead.  For details, see the last answer to
		/// http://stackoverflow.com/questions/16055667/graphics-drawimage-out-of-memory-exception
		/// </remarks>
		public static Image GetImageFromFile(string path)
		{
			Debug.Assert(RobustFile.Exists(path), String.Format("{0} does not exist for ImageUtils.GetImageFromFile()?!", path));
			using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				using (var image = new Bitmap(stream))
				{
					return new Bitmap(image);
				}
			}
		}
	}
}
