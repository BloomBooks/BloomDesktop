using System;
using System.Diagnostics;
using System.IO;
using Bloom.Book;
using Bloom.Collection;
using Newtonsoft.Json;
using SIL.IO;

namespace Bloom.Api
{
	/// <summary>
	/// Supports branding (e.g. logos, CC License) needed by projects.
	/// Currently we don't allow the image server to see these requests, which always occur in xmatter.
	/// Instead, as part of the process of bringing xmatter up to date, we change the image src attributes
	/// to point to the svg or png file which we copy into the book folder.
	/// This process (in XMatterHelper.CleanupBrandingImages()) allows the books to look right when
	/// opened in a browser and also in BloomReader. (It would also help with making Epubs, though that
	/// code is already written to handle branding.)
	/// Keeping this class active (a) because most of its logic is used by CleanupBrandingImages(),
	/// and (b) as a safety net, in case there's some way an api/branding url still gets presented
	/// to the image server.
	/// </summary>
	class BrandingSettings
	{
		public const string kBrandingImageUrlPart = "branding/image";
		private readonly CollectionSettings _collectionSettings;

		public BrandingSettings(CollectionSettings collectionSettings)
		{
			_collectionSettings = collectionSettings;
		}



		/// <summary>
		/// Find the requested branding image file for the given branding, looking for a .png file if the .svg file does not exist.
		/// </summary>
		/// <remarks>
		/// This method is used by EpubMaker as well as here in BrandingApi.
		/// </remarks>
		public static string FindBrandingImageFileIfPossible(string branding, string filename, Layout layout)
		{
			// First look for an orientation-specific image.
			var orientationName = layout.SizeAndOrientation.OrientationName.ToLowerInvariant();
			var ext = Path.GetExtension(filename);
			var filenameNoExt = Path.ChangeExtension(filename, null);
			var orientedFileName = Path.ChangeExtension(filenameNoExt + "-" + orientationName, ext);
			var path = BloomFileLocator.GetOptionalBrandingFile(branding, orientedFileName);
			if (!string.IsNullOrEmpty(path))
				return path;
			path = BloomFileLocator.GetOptionalBrandingFile(branding, Path.ChangeExtension(orientedFileName, "png"));
			if (!string.IsNullOrEmpty(path))
				return path;
			// OK, no orientation-specific image exists, look for the given image file as specified.
			// Note: in Bloom 3.7, our Firefox, when making PDFs, would render svg's as blurry. This was fixed in Bloom 3.8 with
			// a new Firefox. So SVGs are requested by the html...
			path = BloomFileLocator.GetOptionalBrandingFile(branding, filename);

			// ... but if there is no SVG, we can actually send back a PNG instead, and that works fine:
			if(string.IsNullOrEmpty(path))
				path = BloomFileLocator.GetOptionalBrandingFile(branding, Path.ChangeExtension(filename, "png"));

			// ... and if there is no PNG, look for a "jpg":
			if (string.IsNullOrEmpty(path))
				path = BloomFileLocator.GetOptionalBrandingFile(branding, Path.ChangeExtension(filename, "jpg"));

			return path;
		}

		public class PresetItem
		{
			[JsonProperty("data-book")]
			public string DataBook;
			[JsonProperty("lang")]
			public string Lang;
			[JsonProperty("content")]
			public string Content;
			[JsonProperty("condition")]
			public string Condition; // one of always (override), ifEmpty (default), ifAllCopyrightEmpty
		}

		public class Settings
		{
			[JsonProperty("presets")]
			public PresetItem[] Presets;
		}

		/// <summary>
		/// branding folders can optionally contain a branding.json file which aligns with this Settings class
		/// </summary>
		/// <param name="brandingNameOrFolderPath"> Normally, the branding is just a name, which we look up in the official branding folder
		//but unit tests can instead provide a path to the folder.
		/// </param>
		public static Settings GetSettings(string brandingNameOrFolderPath)
		{
			try
			{
				var settingsPath = BloomFileLocator.GetOptionalBrandingFile(brandingNameOrFolderPath, "branding.json");
				if(!string.IsNullOrEmpty(settingsPath))
				{
					var content = RobustFile.ReadAllText(settingsPath);
					var settings = JsonConvert.DeserializeObject<Settings>(content);
					if(settings == null)
					{
						NonFatalProblem.Report(ModalIf.Beta, PassiveIf.All, "Trouble reading branding settings",
							"branding.json of the branding " + brandingNameOrFolderPath + " may be corrupt. It had: " + content);
						return null;
					}
					return settings;
				}
			}
			catch(Exception e)
			{
				NonFatalProblem.Report(ModalIf.Beta, PassiveIf.All, "Trouble reading branding settings", exception: e);
			}
			return null;
		}

		public const string kApiBrandingImage = "/bloom/api/branding/image";
	}
}
