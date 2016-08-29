using System;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SIL.Reporting;
using SIL.Windows.Forms.ImageToolbox;

namespace Bloom.Workspace
{
	/// <summary>Allows Bloom to use the GTK classes when accessing the clipboard on Linux</summary>
	public static class BloomClipboard
	{
		public static bool ContainsText()
		{
#if __MonoCS__
			return GtkUtils.GtkClipboard.ContainsText();
#else
			return Clipboard.ContainsText();
#endif
		}

		public static string GetText()
		{
#if __MonoCS__
			return GtkUtils.GtkClipboard.GetText();
#else
			return Clipboard.GetText();
#endif
		}

		public static string GetText(TextDataFormat format)
		{
#if __MonoCS__
			return GtkUtils.GtkClipboard.GetText();
#else
			return Clipboard.GetText(format);
#endif
		}

		public static void SetText(string text)
		{
#if __MonoCS__
			GtkUtils.GtkClipboard.SetText(text);
#else
			Clipboard.SetText(text);
#endif
		}

		public static void SetText(string text, TextDataFormat format)
		{
#if __MonoCS__
			GtkUtils.GtkClipboard.SetText(text);
#else
			Clipboard.SetText(text, format);
#endif
		}

		public static bool ContainsImage()
		{
#if __MonoCS__
			return GtkUtils.GtkClipboard.ContainsImage();
#else
			return Clipboard.ContainsImage();
#endif
		}

		public static Image GetImage()
		{
#if __MonoCS__
			return GtkUtils.GtkClipboard.GetImage();
#else
			return Clipboard.GetImage();
#endif
		}

		public static void CopyImageToClipboard(PalasoImage image)
		{
			// N.B.: PalasoImage does not handle .svg files
			if(image == null)
				return;
			// Review: Someone who knows how needs to fill in the Mono section
#if __MonoCS__
#else
			if (image.Image == null)
			{
				if (String.IsNullOrEmpty(image.OriginalFilePath))
					return;
				// no image, but a path
				Clipboard.SetFileDropList(new StringCollection() {image.OriginalFilePath});
			}
			else
			{
				if (String.IsNullOrEmpty(image.OriginalFilePath))
					Clipboard.SetImage(image.Image);
				else
				{
					IDataObject clips = new DataObject();
					clips.SetData(DataFormats.UnicodeText, image.OriginalFilePath);
					clips.SetData(DataFormats.Bitmap, image.Image);
					// true here means that the image should remain on the clipboard if Bloom quits
					Clipboard.SetDataObject(clips,true);
				}
			}
#endif
		}

		public static PalasoImage GetImageFromClipboard()
		{
			// N.B.: PalasoImage does not handle .svg files
#if __MonoCS__
			if (GtkUtils.GtkClipboard.ContainsImage())
				return PalasoImage.FromImage(GtkUtils.GtkClipboard.GetImage());

			if (GtkUtils.GtkClipboard.ContainsText())
			{
				//REVIEW: I can find no documentation on GtkClipboard. If ContainsText means we have a file
				//	path, then it would be better to do PalasoImage.FromFile(); on the file path
				return PalasoImage.FromImage(GtkUtils.GtkClipboard.GetImageFromText());
			}

			return null;
#else
			var dataObject = Clipboard.GetDataObject();
			if (dataObject == null)
				return null;

			var textData = String.Empty;
			if (dataObject.GetDataPresent(DataFormats.UnicodeText))
				textData = dataObject.GetData(DataFormats.UnicodeText) as String;
			if (Clipboard.ContainsImage())
			{
				PalasoImage plainImage = null;
				try
				{
					plainImage = PalasoImage.FromImage(Clipboard.GetImage()); // this method won't copy any metadata
					var haveFileUrl = !String.IsNullOrEmpty(textData) && SafeFile.Exists(textData);

					// If we have an image on the clipboard, and we also have text that is a valid url to an image file,
					// use the url to create a PalasoImage (which will pull in any metadata associated with the image too)
					if (haveFileUrl)
					{
						var imageWithPathAndMaybeMetadata = PalasoImage.FromFile(textData);
						plainImage.Dispose();//important: don't do this until we've successfully created the imageWithPathAndMaybeMetadata
						return imageWithPathAndMaybeMetadata;
					}
					else
					{
						return plainImage;
					}
				}
				catch (Exception e)
				{
					Logger.WriteEvent("BloomClipboard.GetImageFromClipboard() failed with message " + e.Message);
					return plainImage; // at worst, we should return null; if FromFile() failed, we return an image
				}
			}
			// the ContainsImage() returns false when copying an PNG from MS Word
			// so here we explicitly ask for a PNG and see if we can convert it.
			if (dataObject.GetDataPresent("PNG"))
			{
				var o = dataObject.GetData("PNG") as Stream;
				try
				{
					return PalasoImage.FromImage(Image.FromStream(o));
				}
				catch (Exception)
				{}
			}

			//People can do a "copy" from the WIndows Photo Viewer but what it puts on the clipboard is a path, not an image
			if (dataObject.GetDataPresent(DataFormats.FileDrop))
			{
				//This line gets all the file paths that were selected in explorer
				string[] files = dataObject.GetData(DataFormats.FileDrop) as string[];
				if (files == null) return null;

				foreach (var file in files.Where(f => SafeFile.Exists(f)))
				{
					try
					{
						return PalasoImage.FromFile(file);
					}
					catch (Exception)
					{}
				}

				return null; //not an image
			}

			if (!Clipboard.ContainsText() || !SafeFile.Exists(Clipboard.GetText())) return null;

			try
			{
				return PalasoImage.FromImage( Image.FromStream(new FileStream(Clipboard.GetText(), FileMode.Open)));
			}
			catch (Exception)
			{}

			return null;
#endif
		}
	}
}
