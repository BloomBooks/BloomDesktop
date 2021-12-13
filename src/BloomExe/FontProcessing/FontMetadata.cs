using System;
using System.Linq;
#if __MonoCS__
using System.Diagnostics;
using System.Text;
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
		public string licenseURL { get; private set; }
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

		/// <summary>
		/// On Window, we can use System.Windows.Media (which provides the GlyphTypeface class) to
		/// provide all the font metatdata information.
		/// On Linux, we have to use Sharpfont from nuget (which provides the Sharpfont.Face class)
		/// for reading the font's embedding flag plus running /usr/bin/otfino for everything else.
		/// We get /usr/bin/otfinfo as part of the lcdf-typetools package that is specified in the
		/// debian/control file.
		/// clas
		/// </summary>
		public FontMetadata(string name, FontGroup group)
		{
			const string kInvalid = "invalid font file";
			const string kUnsuitable = "unsuitable";

			this.name = name;
#if __MonoCS__
			try
			{
				using (var lib = new SharpFont.Library())
				{
					using (var face = new Face(lib, group.Normal))
					{
						var embeddingFlags = face.GetFSTypeFlags();
						if ((embeddingFlags & EmbeddingTypes.RestrictedLicense) == EmbeddingTypes.RestrictedLicense)
						{
							fsType = "Restricted License";
						}
						else if ((embeddingFlags & EmbeddingTypes.BitmapOnly) == EmbeddingTypes.BitmapOnly)
						{
							fsType = "Bitmaps Only";
						}
						else if ((embeddingFlags & EmbeddingTypes.Editable) == EmbeddingTypes.Editable)
						{
							fsType = "Editable";
						}
						else if ((embeddingFlags & EmbeddingTypes.PreviewAndPrint) == EmbeddingTypes.PreviewAndPrint)
						{
							fsType = "Print and preview";
						}
						else
						{
							fsType = "Installable";	// but is it really?
						}
						// Every call to face.GetSfntName(i) throws a null object exception.
						// Otherwise the code would build on this fragment.
						//var count = face.GetSfntNameCount();
						//for (uint i = 0; i < count; ++i)
						//{
						//	try
						//	{
						//		var sfntName = face.GetSfntName(i);
						//		...
						//	}
						//	catch (Exception ex)
						//	{
						//	}
						//}
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("SharpLib EXCEPTION for {0}: {1}", group.Normal, ex);
				determinedSuitabilityNotes = $"SharpLib exception: {ex}";
				determinedSuitability = kUnsuitable;
				fsType = kInvalid;
				return;
			}
			try
			{
				var process = new Process
				{
					StartInfo = new ProcessStartInfo()
					{
						FileName = "/usr/bin/otfinfo",
						Arguments = $"-i \"{group.Normal}\"",
						UseShellExecute = false,
						CreateNoWindow = true,
						RedirectStandardOutput = true,
						RedirectStandardError = true,
					},
				};
				process.Start();
				process.WaitForExit();
				var standardOutput = process.StandardOutput.ReadToEnd();
				var standardError = process.StandardError.ReadToEnd();
				if (process.ExitCode == 0 && standardError.Length == 0)
				{
					ParseOtfinfoOutput(standardOutput);
				}
				else
				{
					Console.WriteLine("oftinfo -i \"{0}\" returned {1}.  Standard Error =\n{2}", group.Normal, process.ExitCode, standardError);
					var msg = $"otfinfo returned: {process.ExitCode}. Standard Error={Environment.NewLine}{standardError}";
					if (String.IsNullOrEmpty(determinedSuitabilityNotes))
						determinedSuitabilityNotes = msg;
					else
						determinedSuitabilityNotes = determinedSuitabilityNotes + "; " + msg;
					determinedSuitability = kUnsuitable;
					fsType = kInvalid;
					return;
				}
			}
			catch (Exception e)
			{
				Console.WriteLine("Process.Start() of /usr/bin/otfinfo -i \"{0}\" threw an exception: {1}", group.Normal, e);
				var msg = $"Process.Start of \"otfinfo\" exception: {e}";
				if (String.IsNullOrEmpty(determinedSuitabilityNotes))
					determinedSuitabilityNotes = msg;
				else
					determinedSuitabilityNotes = determinedSuitabilityNotes + "; " + msg;
				determinedSuitability = kUnsuitable;
				fsType = kInvalid;
				return;
			}
#else
			GlyphTypeface gtf = null;
			try
			{
				gtf = new GlyphTypeface(new Uri("file:///" + group.Normal));
				var english = System.Globalization.CultureInfo.GetCultureInfo("en-US");
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
			}
			catch (Exception e)
			{
				// file is somehow corrupt or not really a font file? Just ignore it.
				Console.WriteLine("GlyphTypeface for \"{0}\" threw an exception: {1}", group.Normal, e);
				var msg = $"GlyphTypeface exception: {e}";
				if (String.IsNullOrEmpty(determinedSuitabilityNotes))
					determinedSuitabilityNotes = msg;
				else
					determinedSuitabilityNotes = determinedSuitabilityNotes + "; " + msg;
				determinedSuitability = kUnsuitable;
				fsType = kInvalid;
				return;
			}
#endif
			variants = group.GetAvailableVariants().ToArray();

			// Now for the hard part: setting DeterminedSuitability
			if (!String.IsNullOrEmpty(license))
			{
				if (license.Contains("Open Font License") || license.Contains("OFL") ||
					license.StartsWith("Licensed under the Apache License") ||
					license.Contains("GNU GPL") || license.Contains("GNU General Public License") || license.Contains(" GPL ") || license.Contains(" GNU ") ||
					(license.Contains("GNU license") && license.Contains("www.gnu.org")) ||
					license.Contains("GNU LGPL") || license.Contains("GNU Lesser General Public License"))
				{
					determinedSuitability = "ok";
					if (license.Contains("Open Font License") || license.Contains("OFL"))
						determinedSuitabilityNotes = "Open Font License";
					else if (license.StartsWith("Licensed under the Apache License"))
						determinedSuitabilityNotes = "Apache License";
					else if (license.Contains("GNU LGPL") || license.Contains("GNU Lesser General Public License"))
						determinedSuitabilityNotes = "GNU LGPL";
					else
						determinedSuitabilityNotes = "GNU GPL";
					return;
				}
				if (license.Replace('\n',' ').Contains("free of charge") && license.Contains("Bitstream"))
				{
					determinedSuitability = "ok";
					determinedSuitabilityNotes = "Bitstream free license";
					return;
				}
			}
			if (licenseURL == "http://dejavu-fonts.org/wiki/License")
			{
				determinedSuitability = "ok";
				determinedSuitabilityNotes = "Bitstream free license";
				return;
			}
			if (!String.IsNullOrEmpty(copyright))
			{
				// some people put license information in the copyright string.
				if (copyright.Contains("Artistic License"))
				{
					determinedSuitability = "ok";
					determinedSuitabilityNotes = "Artistic License";
					return;
				}
				if (copyright.Contains("GNU General Public License") || copyright.Contains(" GPL "))
				{
					determinedSuitability = "ok";
					determinedSuitabilityNotes = "GNU GPL";
					return;
				}
				if (copyright.Contains("SIL Open Font License"))
				{
					determinedSuitability = "ok";
					determinedSuitabilityNotes = "Open Font License";
					return;
				}
				if (copyright.Contains("Ubuntu Font Licence"))  // British spelling I assume...
				{
					determinedSuitability = "ok";
					determinedSuitabilityNotes = "Ubuntu Font Licence";
					return;
				}
			}
			if (fsType == "Restricted License" || fsType == "Bitmaps Only")
			{
				determinedSuitability = "unsuitable";
				determinedSuitabilityNotes = "unambiguous fsType value";
				return;
			}
			if (manufacturer == "Microsoft Corporation" || manufacturer == "Microsoft Corp." ||
				(license != null && license.Contains("Microsoft supplied font") && manufacturer != null && manufacturer.Contains("Monotype")) ||
				(license == null && manufacturer == null &&
					copyright != null && copyright.Contains("Microsoft Corporation") &&
					trademark != null && trademark.Contains("is a trademark of Microsoft Corporation"))
				)
			{
				// Review what about "Print and Preview"?
				determinedSuitability = "ok";
				determinedSuitabilityNotes = "fsType from reliable source";
				return;
			}
			// Give up.  More heuristics may suggest themselves.
			determinedSuitability = "unknown";
			determinedSuitabilityNotes = "no reliable information";
		}

#if __MonoCS__
		private struct Labels
		{
			internal const string kVersion = "Version: ";
			internal const string kDesigner = "Designer: ";
			internal const string kDesignerUrl = "Designer URL: ";
			internal const string kManufacturer = "Manufacturer: ";
			internal const string kVendorUrl = "Vendor URL: ";
			internal const string kTrademark = "Trademark: ";
			internal const string kCopyright = "Copyright: ";
			internal const string kLicenseUrl = "License URL: ";
			internal const string kLicense = "License Description: ";
			internal const string kVendorId = "Vendor ID: ";	// use this only to delimit license
		}
		/// <summary>
		/// otfinfo output has fields marked with these labels at the beginnings of lines (in
		/// the order shown here):
		/// 	Family:
		/// 	Subfamily:
		/// 	Full name:
		/// 	PostScript name:
		/// 	Version:
		/// 	Unique ID:
		/// 	Description:
		/// 	Designer:
		/// 	Designer URL:
		/// 	Manufacturer:
		/// 	Vendor URL:
		/// 	Trademark:
		/// 	Copyright:
		/// 	License URL:
		/// 	License Description:
		/// 	Vendor ID:
		/// One or more of these fields may be missing in the output.  Some fields may occupy
		/// multiple lines (notably Copyright and License Description).
		/// </summary>
		/// <param name="standardOutput">Standard output.</param>
		private void ParseOtfinfoOutput(string standardOutput)
		{
			var lines = standardOutput.Split(new[] { '\n', '\r' });
			for (int i = 0; i < lines.Length; ++i)
			{
				if (lines[i].StartsWith(Labels.kVersion, StringComparison.Ordinal))
				{
					version = lines[i].Substring(Labels.kVersion.Length).Trim();
					if (version.StartsWith("Version ", StringComparison.Ordinal))
						version = version.Substring(8);
				}
				else if (lines[i].StartsWith(Labels.kDesigner, StringComparison.Ordinal))
				{
					designer = lines[i].Substring(Labels.kDesigner.Length).Trim();
				}
				else if (lines[i].StartsWith(Labels.kDesignerUrl, StringComparison.Ordinal))
				{
					designerURL = lines[i].Substring(Labels.kDesignerUrl.Length).Trim();
				}
				else if (lines[i].StartsWith(Labels.kManufacturer, StringComparison.Ordinal))
				{
					manufacturer = lines[i].Substring(Labels.kManufacturer.Length).Trim();
				}
				else if (lines[i].StartsWith(Labels.kVendorUrl, StringComparison.Ordinal))
				{
					manufacturerURL = lines[i].Substring(Labels.kVendorUrl.Length).Trim();
				}
				else if (lines[i].StartsWith(Labels.kTrademark, StringComparison.Ordinal))
				{
					trademark = lines[i].Substring(Labels.kTrademark.Length).Trim();
				}
				else if (lines[i].StartsWith(Labels.kCopyright, StringComparison.Ordinal))
				{
					var copyrightBldr = new StringBuilder();
					copyrightBldr.AppendLine(lines[i].Substring(Labels.kCopyright.Length).Trim());
					while (++i < lines.Length)
					{
						if (lines[i].StartsWith(Labels.kLicenseUrl, StringComparison.Ordinal) ||
							lines[i].StartsWith(Labels.kLicense, StringComparison.Ordinal) ||
							lines[i].StartsWith(Labels.kVendorId, StringComparison.Ordinal))
						{
							--i;
							break;
						}
						copyrightBldr.AppendLine(lines[i]);
					}
					copyright = copyrightBldr.ToString().Trim();
				}
				else if (lines[i].StartsWith(Labels.kLicenseUrl, StringComparison.Ordinal))
				{
					licenseURL = lines[i].Substring(Labels.kLicenseUrl.Length).Trim();
				}
				else if (lines[i].StartsWith(Labels.kLicense, StringComparison.Ordinal))
				{
					var licenseBldr = new StringBuilder();
					licenseBldr.AppendLine(lines[i].Substring(Labels.kLicense.Length).Trim());
					while (++i < lines.Length)
					{
						if (lines[i].StartsWith(Labels.kVendorId, StringComparison.Ordinal))
						{
							--i;
							break;
						}
						licenseBldr.AppendLine(lines[i]);
					}
					license = licenseBldr.ToString();
				}
			}
		}
#endif
	}
}
