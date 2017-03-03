using System;
using System.IO;
using Bloom.Collection;
using Newtonsoft.Json;
using SIL.IO;

namespace Bloom.Api
{
	/// <summary>
	/// Supports branding (e.g. logos) needed by projects
	/// </summary>
	class BrandingApi
	{
		public const string kBrandingImageUrlPart = "branding/image";
		private readonly CollectionSettings _collectionSettings;

		public BrandingApi(CollectionSettings collectionSettings)
		{
			_collectionSettings = collectionSettings;
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler(kBrandingImageUrlPart, request =>
			{
				var fileName = request.RequiredFileNameOrPath("id");
				var path = FindBrandingImageFileIfPossible(_collectionSettings.BrandingProjectName, fileName.NotEncoded);

				// And this is perfectly normal, to not have a branding image at all, for a particular page:
				if (string.IsNullOrEmpty(path))
				{
					request.Failed("");
					// the HTML will need to be able to handle this invisibly... see http://stackoverflow.com/questions/22051573/how-to-hide-image-broken-icon-using-only-css-html-without-js
					return;
				}
				request.ReplyWithImage(path);
			});

		}

		/// <summary>
		/// Find the requested branding image file for the given branding, looking for a .png file if the .svg file does not exist.
		/// </summary>
		/// <remarks>
		/// This method is used by EpubMaker as well as here in BrandingApi.
		/// </remarks>
		public static string FindBrandingImageFileIfPossible(string branding, string filename)
		{
			// Note: in Bloom 3.7, our Firefox, when making PDFs, would render svg's as blurry. This was fixed in Bloom 3.8 with
			// a new Firefox. So SVGs are requested by the html...
			var path = BloomFileLocator.GetOptionalBrandingFile(branding, filename);

			// ... but if there is no SVG, we can actually send back a PNG instead, and that works fine:
			if(string.IsNullOrEmpty(path))
				path = BloomFileLocator.GetOptionalBrandingFile(branding, Path.ChangeExtension(filename, "png"));

			return path;
		}

		public class Settings
		{
			public string CopyrightNotice;
			public string LicenseUrl;
			public string LicenseRightsStatement;
		}

		/// <summary>
		/// branding folders can optionally contain a settings.json file which aligns with this Settings class
		/// </summary>
		/// <param name="brandingNameOrFolderPath"> Normally, the branding is just a name, which we look up in the official branding folder
		//but unit tests can instead provide a path to the folder.
		/// </param>
		public static Settings GetSettings(string brandingNameOrFolderPath)
		{
			try
			{
				var settingsPath = BloomFileLocator.GetOptionalBrandingFile(brandingNameOrFolderPath, "settings.json");
				if(!string.IsNullOrEmpty(settingsPath))
				{
					var content = RobustFile.ReadAllText(settingsPath);
					var settings = JsonConvert.DeserializeObject<Settings>(content);
					if(settings == null)
					{
						NonFatalProblem.Report(ModalIf.Beta, PassiveIf.All, "Trouble reading branding settings",
							"settings.json of the branding " + brandingNameOrFolderPath + " may be corrupt. It had: " + content);
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
