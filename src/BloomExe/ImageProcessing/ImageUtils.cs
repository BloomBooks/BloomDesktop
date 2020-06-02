using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Progress;
using SIL.Windows.Forms.ImageToolbox;
using TagLib;
using TagLib.Png;
using TagLib.Xmp;
using Encoder = System.Drawing.Imaging.Encoder;
using Logger = SIL.Reporting.Logger;
using TempFile = SIL.IO.TempFile;

namespace Bloom.ImageProcessing
{
	class ImageUtils
	{
		public const int MaxLength = 3500;		// 8 pixels less than length of A4 at 300dpi (max width for landscape, height for portrait)
		public const int MaxBreadth = 2550;		// = 8.5 inches at 300dpi (max height for landscape, width for portrait)
		public const double MaxImageAspectPortrait = 3500.0 / 2550.0;
		public const double MaxImageAspectLandscape = 2550.0 / 3500.0;

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
			if (ImageFormat.Jpeg.Guid == imageInfo.Image.RawFormat.Guid)
				return true;

			if (String.IsNullOrEmpty(imageInfo.FileName))
				return false;

			return HasJpegExtension(imageInfo.FileName);
		}

		public static bool HasJpegExtension(string filename)
		{
			return new[] { ".jpg", ".jpeg" }.Contains(Path.GetExtension(filename)?.ToLowerInvariant());
		}

		/// <summary>
		/// Ensure the image does not exceed the maximum size we've set with MaxLength and MaxBreadth.
		/// Ensure that non-jpeg files have an opaque background.
		/// Make the image a png if it's not a jpeg.  Make large png images into jpeg images to save space.
		/// Save the processed image in the book's folder.
		///
		/// If the image has a filename, that name is used in creating any new files.
		/// WARNING: imageInfo.Image could be replaced (causing the original to be disposed)
		/// </summary>
		/// <returns>The name of the file, now in the book's folder.</returns>
		public static string ProcessAndSaveImageIntoFolder(PalasoImage imageInfo, string bookFolderPath, bool isSameFile)
		{
			LogMemoryUsage();
			bool isEncodedAsJpeg = false;
			try
			{
				var size = GetDesiredImageSize(imageInfo.Image.Width, imageInfo.Image.Height);
				isEncodedAsJpeg = AppearsToBeJpeg(imageInfo);
				if (!isEncodedAsJpeg)
				{
					// As explained in the comments for RemoveTransparencyOfImagesInFolder(), some PDF viewers don't
					// handle transparent images very well, so if we aren't sure this image is opaque, replace it with
					// one that is opaque.  Also replace it if it's larger than our maximum allowed size.
					if (size.Width != imageInfo.Image.Width || size.Height != imageInfo.Image.Height || !IsIndexedAndOpaque(imageInfo.Image))
					{
						// The original imageInfo.Image is disposed of in the setter.
						// As of now (9/2016) this is safe because there are no other references to it higher in the stack.
						imageInfo.Image = CreateImageWithoutTransparentBackground(imageInfo, size);
					}
				}
				else if (size.Width != imageInfo.Image.Width || size.Height != imageInfo.Image.Height)
				{
					// need to shrink jpeg file since it's larger than our maximum allowed size.
					imageInfo.Image = ResizeImageWithGraphicsMagick(imageInfo, size);
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
				PalasoImage.SaveImageRobustly(imageInfo, destinationPath);

				return imageFileName;

				/* I (Hatton) have decided to stop compressing images until we have a suite of
				tests based on a number of image exemplars. Compression can be great, but it
				can also lead to very long waits; this is a "first, do no harm" decision.

				//nb: there are cases (undefined) where we get out of memory if we are not operating on a copy
				using (var image = new Bitmap(imageInfo.Image))
				{
					using (var tmp = new TempFile())
					{
						RobustImageIO.SaveImage(image, tmp.Path, isJpeg ? ImageFormat.Jpeg : ImageFormat.Png);
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
				if (!String.IsNullOrEmpty(imageInfo.FileName) && RobustFile.Exists(imageInfo.OriginalFilePath))
				{
					var megs = new FileInfo(imageInfo.OriginalFilePath).Length/(1024*1000);
					if (megs > 2)
					{
						var msg =
							String.Format(
								"Bloom was not able to prepare that picture for including in the book. \r\nThis is a rather large image to be adding to a book --{0} Megs--.",
								megs);
						if (isEncodedAsJpeg)
						{
							msg +=
								"\r\nNote, this file is a jpeg, which is normally used for photographs, and complex color artwork. Bloom can handle smallish jpegs, large ones are difficult to handle, especially if memory is limited.";
						}
						throw new ApplicationException(msg, error);
					}
				}

				throw new ApplicationException(
					"Bloom was not able to prepare that picture for including in the book. We'd like to investigate, so if possible, would you please email it to issues@bloomlibrary.org?" +
					Environment.NewLine + imageInfo.FileName, error);
			}
		}

		/// <summary>
		/// Return the largest image size that either matches the original width and height or
		/// is bounded by the length of A4 paper and the width of Letter paper, both at 300dpi.
		/// </summary>
		internal static Size GetDesiredImageSize(int width, int height)
		{
			var aspect = (double) height / (double) width;
			if (height > width)
			{
				// portrait orientation
				if (height > MaxLength || width > MaxBreadth)
				{
					if (aspect <= MaxImageAspectPortrait)
						return new Size(MaxBreadth, (int)(aspect * (double)MaxBreadth));
					else
						return new Size((int)((double)MaxLength / aspect), MaxLength);
				}
			}
			else if (width > height)
			{
				// landscape orientation
				if (height > MaxBreadth || width > MaxLength)
				{
					if (aspect > MaxImageAspectLandscape)
						return new Size((int)((double)MaxBreadth / aspect), MaxBreadth);
					else
						return new Size(MaxLength, (int)(aspect * (double)MaxLength));
				}
			}
			else
			{
				// square picture
				if (width > MaxBreadth)
					return new Size(MaxBreadth, MaxBreadth);
			}
			return new Size(width, height);
		}

		private static string GetFileNameToUseForSavingImage(string bookFolderPath, PalasoImage imageInfo, bool isJpeg)
		{
			var extension = isJpeg ? ".jpg" : ".png";
			// Some images, like from a scanner or camera, won't have a name yet.  Some will need a number
			// in order to differentiate from what is already there. We don't try and be smart somehow and
			// know when to just replace the existing one with the same name... some other process will have
			// to remove unused images.
			string basename;
			if (String.IsNullOrEmpty(imageInfo.FileName) || imageInfo.FileName.StartsWith("tmp"))
			{
				basename = "image";
			}
			else
			{
				// Even pictures that aren't obviously unnamed or temporary may have the same name.
				// See https://silbloom.myjetbrains.com/youtrack/issue/BL-2627 ("Weird Image Problem").
				basename = Path.GetFileNameWithoutExtension(imageInfo.FileName);
			}
			return GetUnusedFilename(bookFolderPath, basename, extension);
		}

		/// <summary>
		/// Get an unused filename in the given folder based on the basename and extension. "extension" must
		/// start with a period.
		/// </summary>
		internal static string GetUnusedFilename(string bookFolderPath, string basename, string extension)
		{
			// basename may already end in one or more digits. Try to strip off digits, parse and increment.
			int i;
			var newBasename = ParseFilename(basename, out i);
			while (RobustFile.Exists(ConstructFilename(bookFolderPath, newBasename, i, extension)))
			{
				++i;
			}
			return newBasename + GetCounterString(i) + extension;
		}

		private static string ConstructFilename(string folderPath, string basename, int currentNum, string extension)
		{
			return Path.Combine(folderPath,
				basename +
				GetCounterString(currentNum) +
				extension);
		}

		private static string GetCounterString(int currentCounter)
		{
			return currentCounter == 0 ? string.Empty : currentCounter.ToString(CultureInfo.InvariantCulture);
		}

		private static string ParseFilename(string basename, out int versionNumber)
		{
			const string digits = "0123456789";
			var length = basename.Length;
			var i = length;
			while (i > 0 && digits.Contains(basename[i - 1]))
			{
				--i;
			}
			// i will be the index of the first digit
			if (i == length)
			{
				// In this case, there are no digits to be had
				versionNumber = 0;
				return basename;
			}
			if (i == 0)
			{
				// In this case, the whole filename is digits
				versionNumber = int.Parse(basename);
				return string.Empty;
			}
			// We have some combination of letters with digits at the end.
			var newBasename = basename.Substring(0, i);
			versionNumber = int.Parse(basename.Substring(i, length - i));
			return newBasename;
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
		// This method also allows to shrink enormous PNG files to our desired maximum size (but not converting them to
		// JPEG as can sometimes happen when initially setting the image files from the Image Chooser dialog).
		// Some books have acquired large images that cause frequent "out of memory" errors, some of which are
		// hidden from the user.
		public static void RemoveTransparencyOfImagesInFolder(string folderPath, IProgress progress)
		{
			var imageFiles = Directory.GetFiles(folderPath, "*.png");
			int completed = 0;
			foreach (string path in imageFiles)
			{
				progress.ProgressIndicator.PercentCompleted = (int)(100.0 * (float)completed / (float)imageFiles.Length);
				if (Path.GetFileName(path).ToLowerInvariant() == "placeholder.png")
				{
					++completed;
					continue;
				}
				// Very large PNG files can cause "out of memory" errors here, while making thumbnails,
				// and when creating ePUBs or BloomPub books.  So, we check for sizes bigger than our
				// maximum and reduce the image here if needed.
				var tagFile = TagLib.File.Create(path);
				if (tagFile.Properties != null && tagFile.Properties.Description.Contains("PNG"))
				{
					var size = GetDesiredImageSize(tagFile.Properties.PhotoWidth, tagFile.Properties.PhotoHeight);
					if (size.Width != tagFile.Properties.PhotoWidth || size.Height != tagFile.Properties.PhotoHeight)
					{
						if (ReplacePngFileWithSmallerOpaqueCopy(path, size, tagFile))
						{
							++completed;
							continue;
						}
					}
				}
				using(var pi = PalasoImage.FromFileRobustly(path))
				{
					// If the image isn't jpeg, and we can't be sure it's already opaque, change the
					// image to be opaque.  As explained above, some PDF viewers don't handle transparent
					// images very well.
					if (!AppearsToBeJpeg(pi) && !IsIndexedAndOpaque(pi.Image))
					{
						RemoveTransparency(pi, path, progress);
					}
				}
				completed++;
			}
		}

		/// <summary>
		/// Use GraphicsMagick to replace a PNG file with one of the given size having an opaque background.
		/// </summary>
		/// <returns>true if successful, false if GraphicsMagick doesn't exist or didn't work</returns>
		private static bool ReplacePngFileWithSmallerOpaqueCopy(string path, Size size, TagLib.File oldMetaData)
		{
			var exeGraphicsMagick = GetGraphicsMagickPath();
			if (!RobustFile.Exists(exeGraphicsMagick))
				return false;
			var tempCopy = GetUnusedFilename(Path.GetTempPath(), Path.GetFileNameWithoutExtension(path),
				Path.GetExtension(path));
			try
			{
				var proc = RunGraphicsMagick(exeGraphicsMagick, path, tempCopy, size, true);
				if (proc.ExitCode == 0)
				{
					RobustFile.Copy(tempCopy, path, true);
					// Copy metadata from older file to the new one.  GraphicsMagick does a poor job on metadata.
					var newMeta = TagLib.File.Create(path);
					CopyTags(oldMetaData, newMeta);
					newMeta.Save();
					return true;
				}
				else
				{
					LogGraphicsMagickFailure(proc);
				}
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
			}
			finally
			{
				// Ignore any errors deleting temp files.  If we leak, we leak...
				try
				{
					RobustFile.Delete(tempCopy);	// don't need this any longer
				}
				catch (Exception e)
				{
					// ignore
				}
			}
			return false;
		}

		/// <summary>
		/// Copy the metadata from one image file to another using TagLib.File objects to represent the two files.
		/// Note that PNG files uses both PNG tags and XMP tags and JPEG files use XMP tags.  (JPEG files may also
		/// use other types of tags, but in practice the XMP tags carry all the intellectual property information
		/// we care about.)
		/// </summary>
		private static void CopyTags(TagLib.File originalTags, TagLib.File newTags)
		{
			if ((originalTags.TagTypes & TagTypes.Png) == TagTypes.Png)
			{
				if (originalTags.GetTag(TagTypes.Png) is PngTag tag &&
					newTags.GetTag(TagTypes.Png, true) is PngTag newTag)
				{
					foreach (KeyValuePair<string, string> kvp in tag)
						newTag.SetKeyword(kvp.Key, kvp.Value);
				}
			}
			if ((originalTags.TagTypes & TagTypes.XMP) == TagTypes.XMP)
			{
				if (originalTags.GetTag(TagTypes.XMP) is XmpTag tag &&
					newTags.GetTag(TagTypes.XMP, true) is XmpTag newTag)
				{
					// Don't bother copying camera/scanner related information.
					// We just want the creator/copyright/description type information.
					foreach (var node in tag.NodeTree.Children)
					{
						if (node.Namespace == "http://purl.org/dc/elements/1.1/" ||
							node.Namespace == "http://creativecommons.org/ns#" ||
							node.Namespace == "http://www.metadataworkinggroup.com/schemas/collections/" ||
							(node.Namespace == "http://ns.adobe.com/exif/1.0/" && node.Name == "UserComment"))
						{
							newTag.NodeTree.AddChild(node);
						}
					}
				}
			}
		}

		private static void RemoveTransparency(PalasoImage original, string path, IProgress progress)
		{
			progress.WriteStatus("RemovingTransparency from image: " + Path.GetFileName(path));
			using (var b = new Bitmap(original.Image.Width, original.Image.Height))
			{
				DrawImageWithWhiteBackground(original.Image, b);
				original.Image = b;
				PalasoImage.SaveImageRobustly(original, path); // BL-4148: this method preserves existing metadata
			}
		}

		/// <summary>
		/// Check whether this image has an indexed format, and if so, whether all of its colors are totally opaque.
		/// (If so, we won't need to create a copy of the image without any transparency.
		/// </summary>
		/// <remarks>
		/// It's too hard/expensive to check all the pixels in a non-indexed image to see if they're all opaque.
		/// </remarks>
		public static bool IsIndexedAndOpaque(Image image)
		{
			if ((image.PixelFormat & PixelFormat.Indexed) == PixelFormat.Indexed)
			{
				foreach (var color in image.Palette.Entries)
				{
					if (color.A != 255)
						return false;	// 255 == opaque, other values varying amount of transparency
				}
				return true;
			}
			// Too hard / expensive to determine transparency/opacity
			return false;
		}

		/// <summary>
		/// Create an image with a solid white background.  Note that the new image will be 32-bit RGBA
		/// even if the original is 1-bit black and white.
		/// </summary>
		private static Image CreateImageWithoutTransparentBackground(PalasoImage imageInfo, Size size)
		{
			return ResizeImageWithGraphicsMagick(imageInfo, size, true);
		}

		/// <summary>
		/// If GraphicsMagick exists, use it to resize the image, optionally making it opaque in the process.
		/// If GraphicsMagick cannot be found, use the C# .Net code for the desired operation.
		/// </summary>
		/// <remarks>
		/// The reason for using GraphicsMagick is that some images are just too big to handle without getting
		/// an "out of memory" error.
		/// </remarks>
		private static Image ResizeImageWithGraphicsMagick(PalasoImage imageInfo, Size size, bool makeOpaque=false)
		{
			var graphicsMagickPath = GetGraphicsMagickPath();
			if (RobustFile.Exists(graphicsMagickPath))
			{
				var sourcePath = imageInfo.OriginalFilePath;
				var isJpegImage = AppearsToBeJpeg(imageInfo);
				if (String.IsNullOrEmpty(sourcePath) || !RobustFile.Exists(sourcePath))
				{
					// This must be from a paste instead of the ImageChooser dialog.
					sourcePath = Path.GetTempFileName();
					RobustFile.Delete(sourcePath);
					if (isJpegImage)
					{
						sourcePath = sourcePath + ".jpg";
						imageInfo.Image.Save(sourcePath, ImageFormat.Jpeg);
					}
					else
					{
						sourcePath = sourcePath + ".png";
						imageInfo.Image.Save(sourcePath, ImageFormat.Png);
					}
				}
				var destPath = GetFileNameToUseForSavingImage(Path.GetTempPath(), imageInfo, isJpegImage);
				try
				{
					var proc = RunGraphicsMagick(graphicsMagickPath, sourcePath, destPath, size, makeOpaque);
					if (proc.ExitCode == 0)
					{
						return GetImageFromFile(destPath);
					}
					else
					{
						LogGraphicsMagickFailure(proc);
					}
				}
				catch (Exception e)
				{
					Console.WriteLine(e);
				}
				finally
				{
					// Ignore any errors deleting temp files.  If we leak, we leak...
					try
					{
						RobustFile.Delete(destPath);	// don't need this any longer
						if (sourcePath != imageInfo.OriginalFilePath)
							RobustFile.Delete(sourcePath);
					}
					catch (Exception e)
					{
						// ignore
					}
				}
			}
			// GraphicsMagick is not working (or doesn't exist).  Try the old way with System.Drawing operations.
			var bm = new Bitmap(size.Width, size.Height);
			var rect = new Rectangle(Point.Empty, size);
			using (var g = Graphics.FromImage(bm))
			{
				if (makeOpaque)
					g.Clear(Color.White);
				g.DrawImage(imageInfo.Image, rect);
			}
			return bm;
		}

		private static void LogGraphicsMagickFailure(Process proc)
		{
			var standardOutput = proc.StandardOutput.ReadToEnd();
			var standardError = proc.StandardError.ReadToEnd();
			var msgBldr = new StringBuilder();
			msgBldr.AppendLine("GraphicsMagick failed to convert an image file.");
			msgBldr.AppendFormat("{0} {1}", proc.StartInfo.FileName, proc.StartInfo.Arguments);
			msgBldr.AppendLine();
			msgBldr.AppendFormat("GraphicsMagick exit code = {0}", proc.ExitCode);
			msgBldr.AppendLine();
			msgBldr.AppendLine("stderr =");
			msgBldr.AppendLine(standardError);
			msgBldr.AppendLine("stdout =");
			msgBldr.AppendLine(standardOutput);
			Logger.WriteEvent(msgBldr.ToString());
			Console.Write(msgBldr.ToString());
		}

		private static Process RunGraphicsMagick(string graphicsMagickPath, string sourcePath, string destPath, Size size,
			bool makeOpaque)
		{
			var argsBldr = new StringBuilder();
			argsBldr.AppendFormat("convert \"{0}\"", sourcePath);
			if (makeOpaque)
				argsBldr.Append(" -background white -extent 0x0 +matte");
			argsBldr.AppendFormat(" -scale {0}x{1} \"{2}\"", size.Width, size.Height, destPath);
			var arguments = argsBldr.ToString();
			var proc = new Process
			{
				StartInfo =
				{
					FileName = graphicsMagickPath,
					Arguments = arguments,
					UseShellExecute = false, // enables CreateNoWindow
					CreateNoWindow = true, // don't need a DOS box
					RedirectStandardOutput = true,
					RedirectStandardError = true,
				}
			};
			proc.Start();
			proc.WaitForExit();
			return proc;
		}

		private static string GetGraphicsMagickPath()
		{
			if (Platform.IsLinux)
			{
				return "/usr/bin/gm";
			}
			else
			{
				var codeBaseDir = BloomFileLocator.GetCodeBaseFolder();
				return Path.Combine(codeBaseDir, "gm", "gm.exe");
			}
		}

		private static void DrawImageWithWhiteBackground(Image source, Bitmap target)
		{
			// Color.White is not a constant value, so it can't be used as a default method parameter value.
			DrawImageWithOpaqueBackground(source, target, Color.White);
		}

		public static void DrawImageWithOpaqueBackground(Image source, Bitmap target, Color color)
		{
			Rectangle rect = new Rectangle(Point.Empty, source.Size);
			using (Graphics g = Graphics.FromImage(target))
			{
				g.Clear(color);
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
						RobustImageIO.SaveImage(image, pngFile.Path, ImageFormat.Png);
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
			// RobustFile.Replace which replaces both the file content and the file metadata (permissions).  The result of that if we use
			// the user's temp directory is described in http://issues.bloomlibrary.org/youtrack/issue/BL-3954.
			using (var temp = TempFile.InFolderOf(destinationPath))
			using (var safetyImage = new Bitmap(image))
			{
				using(var parameters = new EncoderParameters(1))
				{
					//0 = max compression, 100 = least
					parameters.Param[0] = new EncoderParameter(encoder, 100L);
					RobustImageIO.SaveImage(safetyImage, temp.Path, jpgEncoder, parameters);
				}
				FileUtils.ReplaceFileWithUserInteractionIfNeeded(temp.Path, destinationPath, null);
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

		public static bool TryCssColorFromString(string input, out Color result)
		{
			result = Color.White; // some default in case of error.
			if (!input.StartsWith("#") || input.Length != 7)
				return false; // arbitrary failure
			try
			{
				result = ColorTranslator.FromHtml(input);
			}
			catch (Exception e)
			{
				return false;
			}
			return true;
		}

		public static Image ResizeImageIfNecessary(Size maxSize, Image image)
		{
			return DrawResizedImage(maxSize, image, false);
		}

		public static Image CenterImageIfNecessary(Size size, Image image)
		{
			return DrawResizedImage(size, image, true);
		}

		/// <summary>
		/// Return a possibly resized and possibly centered image.  If no change is needed,
		/// a new copy of the image is returned.
		/// </summary>
		/// <remarks>
		/// Always returning a new image simplifies keeping track of when to dispose the original
		/// image.
		/// Note that this method never returns a larger image than the original: only one the
		/// same size or smaller.
		/// </remarks>
		private static Image DrawResizedImage(Size maxSize, Image image, bool centerImage)
		{
			// adapted from https://www.c-sharpcorner.com/article/resize-image-in-c-sharp/
			var desiredHeight = maxSize.Height;
			var desiredWidth = maxSize.Width;
			if (image.Width == desiredWidth && image.Height == desiredHeight)
				return new Bitmap(image);	// exact match already
			int newHeight;
			int newWidth;
			if (image.Height <= desiredHeight && image.Width <= desiredWidth)
			{
				if (!centerImage)
					return new Bitmap(image);
				newHeight = image.Height;	// not really new...
				newWidth = image.Width;
			}
			else
			{
				// Try resizing to desired width first
				newHeight = image.Height * desiredWidth / image.Width;
				newWidth = desiredWidth;
				if (newHeight > desiredHeight)
				{
					// Resize to desired height instead
					newWidth = image.Width * desiredHeight / image.Height;
					newHeight = desiredHeight;
				}
			}
			Image newImage;
			if (centerImage)
				newImage = new Bitmap(desiredWidth, desiredHeight);
			else
				newImage = new Bitmap(newWidth, newHeight);
			using (var graphic = Graphics.FromImage(newImage))
			{
				// I tried using HighSpeed settings in here with no appreciable difference in loading speed.
				// However, the "High Quality" settings can greatly increase memory use, possibly causing "Out of Memory"
				// errors when creating thumbnail images.  So we use the default settings for drawing the image here.
				// Some thumbnails may be a bit uglier, but they're supposed to just give an idea of what the front cover
				// looks like: they're not works of art themselves.
				// See https://stackoverflow.com/questions/15438509/graphics-drawimage-throws-out-of-memory-exception?lq=1
				// (the second answer).
				graphic.DrawImage(image, (newImage.Width - newWidth)/2, (newImage.Height - newHeight)/2, newWidth, newHeight);
			}
			return newImage;
		}
	}
}
