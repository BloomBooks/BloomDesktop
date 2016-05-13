using System.Net;
using Bloom.Api;

namespace BloomTests
{
	public class ApiTest
	{
		public enum ContentType  {Text, JSON}

		public static string GetString(EnhancedImageServer server, string endPoint, string query = "",
			ContentType returnType = ContentType.Text, EndpointHandler handler = null, string endOfUrlForTest = null)
		{
			if(handler != null)
			{
				server.RegisterEndpointHandler(endPoint, handler);
			}
			server.StartListening();
			var client = new WebClientWithTimeout
			{
				Timeout = 3000,
			};
			client.Headers[HttpRequestHeader.ContentType] = returnType == ContentType.Text ? "text/plain" : "application/json";

			if(endOfUrlForTest != null)
			{
				return client.DownloadString(ServerBase.ServerUrlWithBloomPrefixEndingInSlash + "api/" + endOfUrlForTest);
			}
			else
			{
				if(!string.IsNullOrEmpty(query))
					query = "?" + query;
				return client.DownloadString(ServerBase.ServerUrlWithBloomPrefixEndingInSlash + "api/" + endPoint + query);
			}
		}

		public static string PostString(EnhancedImageServer server, string endPoint, string data, ContentType returnType,
			EndpointHandler handler = null)
		{
			if(handler != null)
			{
				server.RegisterEndpointHandler(endPoint, handler);
			}
			server.StartListening();
			var client = new WebClientWithTimeout
			{
				Timeout = 3000
			};
			client.Headers[HttpRequestHeader.ContentType] = returnType == ContentType.Text ? "text/plain" : "application/json";

			return client.UploadString(ServerBase.ServerUrlWithBloomPrefixEndingInSlash + "api/" + endPoint, "POST", data);
		}
	}
}