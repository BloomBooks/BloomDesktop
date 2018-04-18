using System.Collections;
using System.Collections.Generic;
#if __MonoCS__
using SharpFont;
#else
using System.Windows.Media;		// not implemented in Mono
#endif

namespace Bloom.Publish.Epub
{
	/// <summary>
	/// Set of up to four files useful for a given font name
	/// </summary>
	public class FontGroup : IEnumerable<string>
	{
		public string Normal;
		public string Bold;
		public string Italic;
		public string BoldItalic;

#if __MonoCS__
		public void Add(Face face, string path)
		{
			if (Normal == null)
				Normal = path;
			if (face.StyleFlags == (StyleFlags.Bold | StyleFlags.Italic))
				BoldItalic = path;
			else if (face.StyleFlags == StyleFlags.Bold)
				Bold = path;
			else if (face.StyleFlags == StyleFlags.Italic)
				Italic = path;
			else
				Normal = path;
		}
#else
		public void Add(GlyphTypeface gtf, string path)
		{
			if (Normal == null)
				Normal = path;
			if (gtf.Style == System.Windows.FontStyles.Italic)
			{
				if (isBoldFont(gtf))
					BoldItalic = path;
				else
					Italic = path;
			}
			else
			{
				if (isBoldFont(gtf))
					Bold = path;
				else
					Normal = path;
			}
		}

		private static bool isBoldFont(GlyphTypeface gtf)
		{
			return gtf.Weight.ToOpenTypeWeight() > 600;
		}
#endif

		public IEnumerator<string> GetEnumerator()
		{
			if (Normal != null)
				yield return Normal;
			if (Bold != null)
				yield return Bold;
			if (Italic != null)
				yield return Italic;
			if (BoldItalic != null)
				yield return BoldItalic;
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}
}
