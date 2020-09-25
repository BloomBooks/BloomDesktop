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
		/* JDH Sep 2020 commenting out because I found this to be unused by anything
		 public static string FindBrandingImageFileIfPossible(string branding, string filename, Layout layout)
		{
			string path;
			if (layout.SizeAndOrientation.IsLandScape)
			{
				// we will first try to find a landscape-specific image
				var ext = Path.GetExtension(filename);
				var filenameNoExt = Path.ChangeExtension(filename, null);
				var landscapeFileName = Path.ChangeExtension(filenameNoExt + "-landscape", ext);
				path = BloomFileLocator.GetOptionalBrandingFile(branding, landscapeFileName);
				if (!string.IsNullOrEmpty(path))
					return path;
				path = BloomFileLocator.GetOptionalBrandingFile(branding, Path.ChangeExtension(landscapeFileName, "png"));
				if (!string.IsNullOrEmpty(path))
					return path;
			}
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
		*/

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
		/// extract the base and flavor parts of a Branding name
		/// </summary>
		/// <param name="fullBrandingName">the full key</param>
		/// <param name="folderName">the name before any branding; this will match the folder holding all the files.</param>
		/// <param name="flavor">a name or empty string</param>
		public static void ParseBrandingKey(String fullBrandingName, out String folderName, out String flavor)
		{
			// A Branding may optionally have a suffix of the form "[FLAVOR]" where flavor is typically
			// a language name. This is used to select different logo files without having to create
			// a completely separate branding folder (complete with summary, stylesheets, etc) for each
			// language in a project that is publishing in a situation with multiple major languages.
			var parts = fullBrandingName.Split('[');
			folderName = parts[0];
			flavor = parts.Length > 1 ? parts[1].Replace("]","") : "";
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
				ParseBrandingKey(brandingNameOrFolderPath, out var brandingFolderName, out var flavor);

				// check to see if we have a special branding.json just for this flavor.
				// Note that we could instead add code that allows a single branding.json to
				// have rules that apply only on a flavor basis. As of 4.9, all we have is the
				// ability for a branding.json (and anything else) to use "{flavor}" anywhere in the
				// name of an image; this will often be enough to avoid making a new branding.json.
				// But if we needed to have different boilerplate text, well then we would need to
				// either use this here mechanism (separate json) or implement the ability to add
				// "flavor:" to the rules.
				var settingsPath = BloomFileLocator.GetOptionalBrandingFile(brandingFolderName, "branding["+flavor+"].json");

				// if not, fall bck to just "branding.json"
				if (string.IsNullOrEmpty(settingsPath))
				{
					settingsPath = BloomFileLocator.GetOptionalBrandingFile(brandingFolderName, "branding.json");
				}

				if (!string.IsNullOrEmpty(settingsPath))
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
	}
}
