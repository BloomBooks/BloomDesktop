using Bloom.Api;
using Bloom.Publish.BloomLibrary;

namespace Bloom.web.controllers
{
	/// <summary>
	/// APIs related to the BloomLibraryUpload control.
	/// </summary>
	class LibraryPublishApi_Obsolete
	{
		private static BloomLibraryUploadControl _uploadControl;

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("libraryPublish/upload_obsolete", HandleUpload, true);
			apiHandler.RegisterEndpointHandler("libraryPublish/cancel_obsolete", HandleCancel, true);
		}

		public static BloomLibraryUploadControl SetUploadControl
		{
			set => _uploadControl = value;
		}

		private void HandleCancel(ApiRequest request)
		{
			_uploadControl.CancelUpload(); // Puts a cancel message in the progress box.
			request.PostSucceeded();
		}

		private void HandleUpload(ApiRequest request)
		{
			if (request.HttpMethod == HttpMethods.Get)
				return;

			// post
			var requestData = DynamicJson.Parse(request.RequiredPostJson());
			var userOpinion = requestData.sameOrDifferent;
			if (userOpinion == "same")
			{
				_uploadControl.UploadBook();
			}
			else
			{
				// The user has said this is a different book than the one on bloomlibrary.org with the same ID.
				// Change the book id and then upload.
				_uploadControl.UploadBookAfterChangingId();
			}
			request.PostSucceeded();
		}
	}
}
