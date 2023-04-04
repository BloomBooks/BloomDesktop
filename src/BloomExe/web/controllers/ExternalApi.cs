using System;
using Bloom.Api;
using Bloom.WebLibraryIntegration;

namespace Bloom.web.controllers
{
	/// <summary>
	/// API functions which are called from outside of Bloom
	/// </summary>
	public class ExternalApi
	{
		public static event EventHandler LoginSuccessful;

		private BloomParseClient _parseClient;

		// Called by autofac, which creates the one instance and registers it with the server.
		public ExternalApi(BloomParseClient parseClient)
		{
			_parseClient = parseClient;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			// This is called from bloomlibrary.org after a successful login.
			apiHandler.RegisterEndpointHandler("external/login",
				request =>
				{
					if (request.HttpMethod == HttpMethods.Post)
					{
						var requestData = DynamicJson.Parse(request.RequiredPostJson());
						string token = requestData.sessionToken;
						string email = requestData.email;
						string userId = requestData.userId;
						//Debug.WriteLine("Got login data " + email + " with token " + token + " and id " + userId);
						_parseClient.SetLoginData(email, userId, token, BookUpload.Destination);
						LoginSuccessful?.Invoke(this, null);

						request.PostSucceeded();

						Shell.ComeToFront();
					}
					else if (request.HttpMethod == HttpMethods.Options)
					{
						// blorg will send an OPTIONS request; if we don't respond successfully, things go badly.
						request.PostSucceeded();
					}
				}, false);

			// This is called from bloomlibrary.org after a successful logout.
			apiHandler.RegisterEndpointHandler("external/bringToFront",
				request =>
				{
					if (request.HttpMethod == HttpMethods.Post)
					{
						request.PostSucceeded();

						Shell.ComeToFront();
					}
					else if (request.HttpMethod == HttpMethods.Options)
					{
						// blorg will send an OPTIONS request; if we don't respond successfully, things go badly.
						request.PostSucceeded();
					}
				}, false);
		}
	}
}
