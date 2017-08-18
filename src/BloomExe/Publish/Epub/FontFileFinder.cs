using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.IO;
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
	class FontFileFinder
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

		public FontGroup GetGroupForFont(string fontName)
		{
		// Review Linux: very likely something here is not portable.
			if (FontNameToFiles == null)
			{
				FontNameToFiles = new Dictionary<string, FontGroup>();
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
				foreach (var fontFile in Directory.GetFiles(Environment.GetFolderPath(Environment.SpecialFolder.Fonts)))
				{
					// ePUB only understands these types, so skip anything else.
					switch (Path.GetExtension(fontFile))
					{
						case ".ttf":
						case ".otf":
						case ".woff":
							break;
						default:
							continue;
					}
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
							continue; // not allowed to embed (enhance: warn user?)
					}

					var fc = new PrivateFontCollection();
					try
					{
						fc.AddFontFile(fontFile);
					}
					catch (FileNotFoundException)
					{
						continue; // not sure how this can happen but I've seen it.
					}
					var name = fc.Families[0].Name;
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
			FontGroup result;
			FontNameToFiles.TryGetValue(fontName, out result);
			return result;
		}

#if __MonoCS__
		IEnumerable<string> FindLinuxFonts()
		{
			var fontFiles = new List<string>();
			fontFiles.AddRange(FindLinuxFonts("/usr/share/fonts"));
			fontFiles.AddRange(FindLinuxFonts(Environment.GetFolderPath(Environment.SpecialFolder.Fonts)));	// $HOME/.fonts
			return fontFiles;
		}

		IEnumerable<string> FindLinuxFonts(string folder)
		{
			var fontFiles = new List<string>();
			if (Directory.Exists(folder))
			{
				foreach (var subfolder in Directory.EnumerateDirectories(folder))
					fontFiles.AddRange(FindLinuxFonts(subfolder));
				foreach (var file in Directory.EnumerateFiles(folder))
				{
					if (file.EndsWith(".ttf") || file.EndsWith(".otf"))
						fontFiles.Add(file);
				}
			}
			return fontFiles;
		}
#endif
	}
}
