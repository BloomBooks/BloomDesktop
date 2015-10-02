using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace Bloom.Publish
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
		/// (as far as we can tell) we are allowed to embed.</returns>
		public IEnumerable<string> GetFilesForFont(string fontName)
		{
			var group = GetGroupForFont(fontName);
			if (group == null)
				return new string[0];
			return group;
		}

		public FontGroup GetGroupForFont(string fontName)
		{
		// Review Linux: very likely something here is not portable.
			if (FontNameToFiles == null)
			{
				FontNameToFiles = new Dictionary<string, FontGroup>();
				foreach (var fontFile in Directory.GetFiles(Environment.GetFolderPath(Environment.SpecialFolder.Fonts)))
				{
					// Epub only understands these types, so skip anything else.
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
			}
			FontGroup result;
			FontNameToFiles.TryGetValue(fontName, out result);
			return result;
		}
	}
}
