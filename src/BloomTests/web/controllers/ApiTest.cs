using Bloom.Api;

namespace BloomTests
{
    public class ApiTest
    {
        public enum ContentType
        {
            Text,
            JSON,
        }

        // Issues a GET to the given endpoint and returns the response body.
        // (Unlike PostString, there is no ContentType/returnType option: a GET has no body, so
        // no Content-Type request header is sent, and the server ignores it for GETs anyway.)
        public static string GetString(
            BloomServer server,
            string endPoint,
            string query = "",
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
            var client = new WebClientWithTimeout { Timeout = timeoutInMilliseconds ?? 3000 };

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
            client.ContentType = returnType == ContentType.Text ? "text/plain" : "application/json";

            return client.UploadString(
                BloomServer.ServerUrlWithBloomPrefixEndingInSlash + "api/" + endPoint,
                "POST",
                data
            );
        }
    }
}
