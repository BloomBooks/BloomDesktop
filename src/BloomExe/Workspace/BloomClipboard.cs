using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.IO;
using Palaso.UI.WindowsForms.ImageToolbox;

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

		public static PalasoImage GetImageFromClipboard()
		{
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
			if (Clipboard.ContainsImage())
			{
				return PalasoImage.FromImage(Clipboard.GetImage());
			}

			var dataObject = Clipboard.GetDataObject();
			if (dataObject == null)
				return null;

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

				foreach (var file in files.Where(f => File.Exists(f)))
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

			if (!Clipboard.ContainsText() || !File.Exists(Clipboard.GetText())) return null;

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
