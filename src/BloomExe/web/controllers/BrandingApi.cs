using Bloom.Collection;

namespace Bloom.Api
{
	/// <summary>
	/// Supports branding (e.g. logos) needed by projects
	/// </summary>
	class BrandingApi
	{
		private readonly CollectionSettings _collectionSettings;


		public BrandingApi(CollectionSettings collectionSettings)
		{
			_collectionSettings = collectionSettings;
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler("branding/image", request =>
			{
				var fileName = request.RequiredFileNameOrPath("id");
				
				var path = BloomFileLocator.GetFileDistributedWithApplication(true,"branding", _collectionSettings.BrandingProjectName, fileName.NotEncoded);
				if(string.IsNullOrEmpty(path))
				{
					request.Failed(""); // the HTML will need to be able to handle this invisibly... see http://stackoverflow.com/questions/22051573/how-to-hide-image-broken-icon-using-only-css-html-without-js
					return;
				}
				request.ReplyWithImage(path);
			});

		}
	}
}
