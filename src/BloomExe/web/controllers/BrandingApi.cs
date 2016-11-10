using System;
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

				var path = BloomFileLocator.GetOptionalBrandingFile(_collectionSettings.BrandingProjectName, fileName.NotEncoded);
				if(string.IsNullOrEmpty(path))
				{
					request.Failed("");
					// the HTML will need to be able to handle this invisibly... see http://stackoverflow.com/questions/22051573/how-to-hide-image-broken-icon-using-only-css-html-without-js
					return;
				}
				request.ReplyWithImage(path);
			});

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
