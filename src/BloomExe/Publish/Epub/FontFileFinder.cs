using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.IO;
using Bloom.web;
#if __MonoCS__
using SharpFont;				// Linux only (interface to libfreetype.so.6)
#else
using System.Windows.Media;		// not implemented in Mono
#endif

namespace Bloom.Publish.Epub
{
	/// <summary>
	/// This class handles the problem of finding what files contain the definition of the various faces of a particular font.
	/// So far the best approach involves a scan of the whole font directory, so it is much more efficient to build a dictionary
	/// once, or at least once per operation that uses it.
	/// The dictionary and method could be static, but then, we would miss the chance to find any new fonts added since
	/// the last operation that needed this information.
	/// </summary>
	class FontFileFinder: IFontFinder
	{
		private Dictionary<string, FontGroup> FontNameToFiles { get; set; }

		/// <summary>
		/// This is really hard. We somehow need to figure out what font file(s) are used for a particular font.
		/// http://stackoverflow.com/questions/16769758/get-a-font-filename-based-on-the-font-handle-hfont
		/// has some ideas; the result would be Windows-specific. And at some point we should ideally consider
		/// what faces are needed.
		/// For now we use brute force.
		/// 'Andika New Basic' -> AndikaNewBasic-{R,B,I,BI}.ttf
		/// Arial -> arial.ttf/ariali.ttf/arialbd.ttf/arialbi.ttf
		/// 'Charis SIL' -> CharisSIL{R,B,I,BI}.ttf (note: no hyphen)
		/// Doulos SIL -> DoulosSILR
		/// </summary>
		/// <param name="fontName"></param>
		/// <returns>enumeration of file paths (possibly none) that contain data for the specified font name, and which
		/// (as far as we can tell) we are allowed to embed.
		/// Currently we only embed the normal face...see BL-4202 and comments in EpubMaker.EmbedFonts()</returns>
		public IEnumerable<string> GetFilesForFont(string fontName)
		{
			var group = GetGroupForFont(fontName);
			if (group != null && !string.IsNullOrEmpty(group.Normal))
				yield return group.Normal;
		}

		public HashSet<string> FontsWeCantInstall { get; private set; }

		public bool NoteFontsWeCantInstall { get; set; }

		public FontGroup GetGroupForFont(string fontName)
		{
		// Review Linux: very likely something here is not portable.
			if (FontNameToFiles == null)
				InitializeFontData();
			FontGroup result;
			FontNameToFiles.TryGetValue(fontName, out result);
			return result;
		}

		private void InitializeFontData()
		{
			FontNameToFiles = new Dictionary<string, FontGroup>();
			if (NoteFontsWeCantInstall)
				FontsWeCantInstall = new HashSet<string>();
#if __MonoCS__
				using (var lib = new SharpFont.Library())
				{
					// Find all the font files in the standard system location (/usr/share/font) and $HOME/.font (if it exists)
					foreach (var fontFile in FindLinuxFonts())
					{
						try
						{
							using (var face = new SharpFont.Face(lib, fontFile))
							{
								var embeddingTypes = face.GetFSTypeFlags();
								if ((embeddingTypes & EmbeddingTypes.RestrictedLicense) == EmbeddingTypes.RestrictedLicense ||
									(embeddingTypes & EmbeddingTypes.BitmapOnly) == EmbeddingTypes.BitmapOnly)
								{
									if (NoteFontsWeCantInstall)
										FontsWeCantInstall.Add(face.FamilyName);
									continue;
								}
								var name = face.FamilyName;
								// If you care about bold, italic, etc, you can filter here.
								FontGroup files;
								if (!FontNameToFiles.TryGetValue(name, out files))
								{
									files = new FontGroup();
									FontNameToFiles[name] = files;
								}
								files.Add(face, fontFile);
							}
						}
						catch (Exception)
						{
						}
					}
				}
#else
			foreach (var fontFile in FindWindowsFonts())
			{
				GlyphTypeface gtf;
				try
				{
					gtf = new GlyphTypeface(new Uri("file:///" + fontFile));
				}
				catch (Exception)
				{
					continue; // file is somehow corrupt or not really a font file? Just ignore it.
				}
				switch (gtf.EmbeddingRights)
				{
					case FontEmbeddingRight.Editable:
					case FontEmbeddingRight.EditableButNoSubsetting:
					case FontEmbeddingRight.Installable:
					case FontEmbeddingRight.InstallableButNoSubsetting:
					case FontEmbeddingRight.PreviewAndPrint:
					case FontEmbeddingRight.PreviewAndPrintButNoSubsetting:
						break;
					default:
						if (NoteFontsWeCantInstall)
						{
							string name1 = GetFontNameFromFile(fontFile);
							if (name1 != null)
								FontsWeCantInstall.Add(name1);
						}
						continue; // not allowed to embed
				}

				string name = GetFontNameFromFile(fontFile);
				if (name == null)
					continue; // not sure how this can happen but I've seen it.
				// If you care about bold, italic, etc, you can filter here.
				FontGroup files;
				if (!FontNameToFiles.TryGetValue(name, out files))
				{
					files = new FontGroup();
					FontNameToFiles[name] = files;
				}
				files.Add(gtf, fontFile);
			}
#endif
		}

		private static string GetFontNameFromFile(string fontFile)
		{
			var fc = new PrivateFontCollection();
			try
			{
				fc.AddFontFile(fontFile);
			}
			catch (FileNotFoundException)
			{
				return null;
			}
			return fc.Families[0].Name;
		}

#if __MonoCS__
		IEnumerable<string> FindLinuxFonts()
		{
			var fontFiles = new List<string>();
			fontFiles.AddRange(FindFontsInFolder("/usr/share/fonts"));				// primary system location for fonts shared by all users
			fontFiles.AddRange(FindFontsInFolder("/usr/local/share/fonts"));		// secondary system location for fonts shared by all users
			fontFiles.AddRange(FindFontsInFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"fonts")));	// $HOME/.local/share/fonts - used by font-manager
			fontFiles.AddRange(FindFontsInFolder(Environment.GetFolderPath(Environment.SpecialFolder.Fonts)));		// $HOME/.fonts (used for manually added user specific fonts)
			return fontFiles;
		}
#else
		IEnumerable<string> FindWindowsFonts()
		{
			var fontFiles = new List<string>();
			fontFiles.AddRange(FindFontsInFolder(Environment.GetFolderPath(Environment.SpecialFolder.Fonts)));
			// Starting with Windows 10 build 1809, it is possible to install fonts without administrator privilege in the
			// user directory %userprofile%\AppData\Local\Microsoft\Windows\Fonts.  For details, see https://github.com/matplotlib/matplotlib/issues/12954 and
			// https://social.technet.microsoft.com/Forums/Windows/en-US/4434bb03-953b-4f61-bfad-9876c1703dca/set-default-windows-font-install-location-back-to-cwindowsfonts-for-all-users-clicking?forum=win10itprogeneral.
			// For the effect on Bloom, see https://issues.bloomlibrary.org/youtrack/issue/BL-7043.
			fontFiles.AddRange(FindFontsInFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","Windows","Fonts")));
			return fontFiles;
		}
#endif

		IEnumerable<string> FindFontsInFolder(string folder)
		{
			var fontFiles = new List<string>();
			if (Directory.Exists(folder))
			{
				foreach (var subfolder in Directory.EnumerateDirectories(folder))
					fontFiles.AddRange(FindFontsInFolder(subfolder));
				foreach (var file in Directory.GetFiles(folder))
				{
					// ePUB only understands these types, so skip anything else.
					switch (Path.GetExtension(file).ToLowerInvariant())
					{
						case ".ttf":
						case ".otf":
						case ".woff":
						case ".woff2":
							fontFiles.Add(file);
							break;
						default:
							break;
					}
				}
			}
			return fontFiles;
		}
	}

	// its public interface (for purposes of test stubbing)
	public interface IFontFinder
	{
		IEnumerable<string> GetFilesForFont(string fontName);
		bool NoteFontsWeCantInstall { get; set; }
		HashSet<string> FontsWeCantInstall { get; }
		FontGroup GetGroupForFont(string fontName);
	}
}
