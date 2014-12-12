// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Diagnostics;

namespace Bloom.WebLibraryIntegration
{
	/// <summary>
	/// Helper class that reads the proxy settings from environment variable http_proxy
	/// or HTTP_PROXY and provides the information for the Amazon S3Client.
	/// </summary>
	public class ProxyManager
	{
		public ProxyManager()
		{
			var proxy = Environment.GetEnvironmentVariable("http_proxy");
			if (string.IsNullOrEmpty(proxy))
				proxy = Environment.GetEnvironmentVariable("HTTP_PROXY");

			Parse(proxy);
		}

		protected void Parse(string proxyString)
		{
			if (string.IsNullOrEmpty(proxyString))
				return;

			try
			{
				var uri = new Uri(proxyString);
				Hostname = uri.Host;
				Port = uri.Port;
				if (!string.IsNullOrEmpty(uri.UserInfo))
				{
					Username = uri.UserInfo;
					if (Username.Contains(":"))
					{
						var parts = Username.Split(':');
						Username = parts[0];
						Password = parts[1];
					}

				}
			}
			catch (UriFormatException)
			{
				// We simply ignore invalid URIs (which might be as simple as missing http://)
				return;
			}
		}

		public string Hostname { get; private set; }

		public int Port { get; private set; }

		public string Username { get; private set; }

		public string Password { get; private set; }
	}
}
