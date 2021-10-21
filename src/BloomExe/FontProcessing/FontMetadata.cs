using System;
using System.Linq;
#if __MonoCS__
using SharpFont;
#else
using System.Windows.Media;
#endif

namespace Bloom.FontProcessing
{
	public class FontMetadata
	{
		public string name { get; private set; }
		public string version { get; private set; }
		public string license { get; private set; }
		public string copyright { get; private set; }
		public string manufacturer { get; private set; }
		public string manufacturerURL { get; private set; }
		public string fsType { get; private set; }
		public string[] variants { get; private set; }
		public string designer { get; private set; }
		public string designerURL { get; private set; }
		public string trademark { get; private set; }
		public string determinedSuitability { get; private set; }
		public string determinedSuitabilityNotes { get; private set; }

		public FontMetadata(string name, FontGroup group)
		{
#if __MonoCS__
			// This is going to be tricky on Linux since SharpFont isn't cooperating very well.
			// Every call to face.GetSfntName(i) throws a null object exception.
			//try
			//{
			//	using (var lib = new SharpFont.Library())
			//	{
			//		using (var face = new SharpFont.Face(lib, group.Normal))
			//		{
			//			var count = face.GetSfntNameCount();
			//			Console.WriteLine("DEBUG: SfntNameCount = {0} for {1}", count, group.Normal);
			//			for (uint i = 0; i < count; ++i)
			//			{
			//				try
			//				{
			//					Console.WriteLine("DEBUG: calling face.GetSfntName({0}) for {1}", i, Path.GetFileName(group.Normal));
			//					var sfntName = face.GetSfntName(i);
			//					Console.WriteLine("DEBUG: SfntName[{0}] => encId={1}, langId={2}, nameId={3}, platId={4}, str={5}",
			//						i, sfntName.EncodingId, sfntName.LanguageId, sfntName.NameId, sfntName.PlatformId, sfntName.String);
			//				}
			//				catch (Exception ex)
			//				{
			//					Console.WriteLine("SharpLib EXCEPTION for i={0} of {1}: {2}", i, Path.GetFileName(group.Normal), ex);
			//				}
			//			}
			//		}
			//	}
			//}
			//catch (Exception ex)
			//{
			//	Console.WriteLine("SharpLib EXCEPTION: {0}", ex);
			//}
#else
			GlyphTypeface gtf = null;
			try
			{
				gtf = new GlyphTypeface(new Uri("file:///" + group.Normal));
			}
			catch (Exception)
			{
					// file is somehow corrupt or not really a font file? Just ignore it.
			}
			var english = System.Globalization.CultureInfo.GetCultureInfo("en-US");
			this.name = name;
			version = gtf.VersionStrings[english];
			copyright = gtf.Copyrights[english];
			var embeddingRights = gtf.EmbeddingRights;
			switch (embeddingRights)
			{
				case FontEmbeddingRight.Editable:
				case FontEmbeddingRight.EditableButNoSubsetting:
					fsType = "Editable";
					break;
				case FontEmbeddingRight.Installable:
				case FontEmbeddingRight.InstallableButNoSubsetting:
					fsType = "Installable";
					break;
				case FontEmbeddingRight.PreviewAndPrint:
				case FontEmbeddingRight.PreviewAndPrintButNoSubsetting:
					fsType = "Print and preview";
					break;
				case FontEmbeddingRight.RestrictedLicense:
					fsType = "Restricted License";
					break;
				case FontEmbeddingRight.EditableButNoSubsettingAndWithBitmapsOnly:
				case FontEmbeddingRight.EditableButWithBitmapsOnly:
				case FontEmbeddingRight.InstallableButNoSubsettingAndWithBitmapsOnly:
				case FontEmbeddingRight.InstallableButWithBitmapsOnly:
				case FontEmbeddingRight.PreviewAndPrintButNoSubsettingAndWithBitmapsOnly:
				case FontEmbeddingRight.PreviewAndPrintButWithBitmapsOnly:
					fsType = "Bitmaps Only";
					break;
			}
			designer = gtf.DesignerNames[english];
			designerURL = gtf.DesignerUrls[english];
			license = gtf.LicenseDescriptions[english];
			manufacturer = gtf.ManufacturerNames[english];
			manufacturerURL = gtf.VendorUrls[english];
			trademark = gtf.Trademarks[english];
			variants = group.GetAvailableVariants().ToArray();

			// Now for the hard part: setting DeterminedSuitability
			if (!String.IsNullOrEmpty(license))
			{
				if (license.Contains("Open Font License") || license.Contains("OFL") ||
					license.Contains("Apache") ||
					license.Contains("GNU GPL") || license.Contains("GNU General Public License") ||
					license.Contains("GNU LGPL") || license.Contains("GNU Lesser General Public License"))
				{
					determinedSuitability = "suitable";
					if (license.Contains("Open Font License") || license.Contains("OFL"))
						determinedSuitabilityNotes = "Open Font License";
					else if (license.StartsWith("Licensed under the Apache License"))
						determinedSuitabilityNotes = "Apache";
					else if (license.Contains("GNU GPL") || license.Contains("GNU General Public License"))
						determinedSuitabilityNotes = "GNU GPL";
					else
						determinedSuitabilityNotes = "GNU LGPL";
					return;
				}
			}
			if (embeddingRights == FontEmbeddingRight.RestrictedLicense || fsType == "Bitmaps Only")
			{
				determinedSuitability = "unsuitable";
				determinedSuitabilityNotes = "unambiguous fsType value";
				return;
			}
			if (manufacturer == "Microsoft Corporation" || (license != null && license.Contains("Microsoft supplied font") && manufacturer != null && manufacturer.Contains("Monotype")))
			{
				// Review what about "Print and Preview"?
				determinedSuitability = "ok";
				determinedSuitabilityNotes = "fsType from reliable source";
				return;
			}
			// Give up.  More heuristics may suggest themselves.
			// For example, Copyright.ToLowerInvariant().Contains("freeware") => ok
			//              Copyright.ToLowerInvariant().Contains("all rights reserved") => unsuitable
			//              trust all Monotype fonts, not just those sublicensed to Microsoft
			determinedSuitability = "unknown";
			determinedSuitabilityNotes = "no reliable information";
#endif
		}
	}
}
