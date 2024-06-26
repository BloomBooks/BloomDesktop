using System.Net;
using Bloom.Api;

namespace BloomTests
{
    public class ApiTest
    {
        public enum ContentType
        {
            Text,
            JSON
        }

        public static string GetString(
            BloomServer server,
            string endPoint,
            string query = "",
            ContentType returnType = ContentType.Text,
            EndpointHandler handler = null,
            string endOfUrlForTest = null,
            int? timeoutInMilliseconds = null
        )
        {
            if (handler != null)
            {
                server.ApiHandler.RegisterEndpointHandler(endPoint, handler, true);
            }
            server.EnsureListening();
            var client = new WebClientWithTimeout { Timeout = timeoutInMilliseconds ?? 3000, };
            client.Headers[HttpRequestHeader.ContentType] =
                returnType == ContentType.Text ? "text/plain" : "application/json";

            if (endOfUrlForTest != null)
            {
                return client.DownloadString(
                    BloomServer.ServerUrlWithBloomPrefixEndingInSlash + "api/" + endOfUrlForTest
                );
            }
            else
            {
                if (!string.IsNullOrEmpty(query))
                    query = "?" + query;
                return client.DownloadString(
                    BloomServer.ServerUrlWithBloomPrefixEndingInSlash + "api/" + endPoint + query
                );
            }
        }

        public static string PostString(
            BloomServer server,
            string endPoint,
            string data,
            ContentType returnType,
            EndpointHandler handler = null,
            int? timeoutInMilliseconds = null
        )
        {
            if (handler != null)
            {
                server.ApiHandler.RegisterEndpointHandler(endPoint, handler, true);
            }
            server.EnsureListening();
            var client = new WebClientWithTimeout { Timeout = timeoutInMilliseconds ?? 3000 };
            client.Headers[HttpRequestHeader.ContentType] =
                returnType == ContentType.Text ? "text/plain" : "application/json";

            return client.UploadString(
                BloomServer.ServerUrlWithBloomPrefixEndingInSlash + "api/" + endPoint,
                "POST",
                data
            );
        }
    }
}
