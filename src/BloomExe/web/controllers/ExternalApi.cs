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

        private BloomLibraryBookApiClient _bloomLibraryBookApiClient;

        // Called by autofac, which creates the one instance and registers it with the server.
        public ExternalApi(BloomLibraryBookApiClient bloomLibraryBookApiClient)
        {
            _bloomLibraryBookApiClient = bloomLibraryBookApiClient;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // This is called from bloomlibrary.org after a successful login.
            apiHandler.RegisterEndpointHandler(
                "external/login",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Post)
                    {
                        // In at least some circumstances, bloomlibrary.org may POST to this endpoint with
                        // incomplete or unexpected JSON. Accessing missing properties on DynamicJson via
                        // dynamic dispatch throws a RuntimeBinderException (see BL-14503).
                        // We respond successfully and ignore such payloads.
                        var requestData =
                            DynamicJson.Parse(request.RequiredPostJson()) as DynamicJson;

                        if (
                            requestData == null
                            || !requestData.TryGetValue("sessionToken", out string token)
                            || !requestData.TryGetValue("email", out string email)
                            || !requestData.TryGetValue("userId", out string userId)
                            || string.IsNullOrEmpty(token)
                            || string.IsNullOrEmpty(email)
                            || string.IsNullOrEmpty(userId)
                        )
                        {
                            request.PostSucceeded();
                            return;
                        }
                        //Debug.WriteLine("Got login data " + email + " with token " + token + " and id " + userId);
                        _bloomLibraryBookApiClient.SetLoginData(
                            email,
                            userId,
                            token,
                            BookUpload.Destination
                        );
                        LoginSuccessful?.Invoke(this, null);

                        request.PostSucceeded();

                        Shell.ComeToFront();
                    }
                    else if (request.HttpMethod == HttpMethods.Options)
                    {
                        // blorg will send an OPTIONS request; if we don't respond successfully, things go badly.
                        request.PostSucceeded();
                    }
                },
                false
            );

            // This is called from bloomlibrary.org after a successful logout.
            apiHandler.RegisterEndpointHandler(
                "external/bringToFront",
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
                },
                false
            );
        }
    }
}
