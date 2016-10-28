using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using Bloom.Properties;
using Bloom.WebLibraryIntegration;
using Newtonsoft.Json;
using SIL.Reporting;

namespace Bloom.web
{
	// These should all match the corresponding json properties except the capitalization of the first letter
	public enum UrlType
	{
		Parse,
		ParseSandbox,
		LibrarySite,
		LibrarySiteSandbox,
		CheckForUpdates,
		UserSuggestions,
		IssueTrackingSystem,
		IssueTrackingSystemBackend
	}

	public static class ErrorLevelExtensions
	{
		public static string ToJsonPropertyString(this UrlType urlType)
		{
			string urlTypeAsString = urlType.ToString();
			return urlTypeAsString.Substring(0, 1).ToLowerInvariant() + urlTypeAsString.Substring(1);
		}
	}

	public static class UrlLookup
	{
		//For source code (and fallback) purposes, current-services-urls.json lives in BloomExe/Resources.
		//But the live version is in S3 in the BloomS3Client.BloomDesktopFiles bucket.
		private const string kUrlLookupFileName = "current-service-urls.json";

		private static readonly ConcurrentDictionary<UrlType, string> s_liveUrlCache = new ConcurrentDictionary<UrlType, string>();

		private static bool _internetAvailable = true;	// assume it's available to start out

		public static string LookupUrl(UrlType urlType, bool sandbox = false, bool excludeProtocolPrefix = false)
		{
			if (Program.RunningUnitTests && (urlType == UrlType.Parse))
				return "http://bloom-parse-server-unittest.azurewebsites.net/parse"; //it's fine for the unit test url to be hard-coded, putting in the json buys us nothing.

			string fullUrl = LookupFullUrl(urlType, sandbox);
			if (excludeProtocolPrefix)
				return StripProtocol(fullUrl);
			return fullUrl;
		}

		private static string LookupFullUrl(UrlType urlType, bool sandbox = false)
		{
			if (sandbox)
				urlType = GetSandboxUrlType(urlType);

			string url;
			if (s_liveUrlCache.TryGetValue(urlType, out url))
				return url;
			if (TryLookupUrl(urlType, out url))
			{
				s_liveUrlCache.AddOrUpdate(urlType, url, (type, s) => s);
				return url;
			}

			NonFatalProblem.Report(ModalIf.Alpha, PassiveIf.Alpha, "Bloom could not retrieve the URL (type: " + urlType + ") from the live lookup", "We will try to continue with the fallback URL");
			return LookupFallbackUrl(urlType);
		}

		private static UrlType GetSandboxUrlType(UrlType urlType)
		{
			switch (urlType)
			{
				case UrlType.Parse:
				case UrlType.ParseSandbox:
					return UrlType.ParseSandbox;
				case UrlType.LibrarySite:
				case UrlType.LibrarySiteSandbox:
					return UrlType.LibrarySiteSandbox;
				default:
					// ReSharper disable once LocalizableElement
					throw new ArgumentOutOfRangeException("urlType", urlType, "There is no sandbox version for this url type.");
			}
		}

		private static bool TryLookupUrl(UrlType urlType, out string url)
		{
			url = null;
			// Once the internet has been found missing, don't bother trying it again for the duration of the program.
			if (!_internetAvailable)
				return false;
			try
			{
				using (var s3Client = new BloomS3Client(null))
				{
					s3Client.Timeout = TimeSpan.FromMilliseconds(2500.0);
					s3Client.ReadWriteTimeout = TimeSpan.FromMilliseconds(3000.0);
					s3Client.MaxErrorRetry = 1;
					var jsonContent = s3Client.DownloadFile(BloomS3Client.BloomDesktopFiles, kUrlLookupFileName);
					Urls urls = JsonConvert.DeserializeObject<Urls>(jsonContent);
					url = urls.GetUrlById(urlType.ToJsonPropertyString());
					if (!string.IsNullOrWhiteSpace(url))
						return true;
					Logger.WriteEvent("Unable to look up URL type " + urlType);
				}
			}
			catch (Exception e)
			{
				_internetAvailable = false;
				Logger.WriteEvent("Exception while attempting look up of URL type " + urlType + ": " + e);
			}
			return false;
		}

		/// <summary>
		/// Check whether or not the internet is currently available.  This may delay 2.5 seconds if the computer
		/// is on a local network, but the internet is inaccessible.
		/// </summary>
		/// <remarks>
		/// credit is due to http://stackoverflow.com/questions/520347/how-do-i-check-for-a-network-connection
		/// and https://forums.xamarin.com/discussion/19491/check-internet-connectivity.
		/// </remarks>
		public static bool IsInternetAvailable()
		{
			// The next line detects whether the computer is hooked up to a local network, wired or wireless.
			// If it's not on a network at all, we know the Internet isn't available!
			var networkConnected = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
			if (!networkConnected)
			{
				_internetAvailable = false;
				return false;
			}
			// Test whether we can talk to a known site of interest on the internet.  This will tell us
			// close enough whether or not the internet is available.
			try
			{
				// test site same as what we use to talk to Amazon S3
				var iNetRequest = (HttpWebRequest)WebRequest.Create("https://s3.amazonaws.com");
				iNetRequest.Timeout = 2500;
				var iNetResponse = iNetRequest.GetResponse();
				iNetResponse.Close();
				_internetAvailable = true;
				return true;
			}
			catch (WebException ex)
			{
				_internetAvailable = false;
				return false;
			}
		}

		/// <summary>
		/// Return the cached variable indicating Internet availability.
		/// </summary>
		public static bool FastInternetAvailable
		{
			get { return _internetAvailable; }
		}

		private static string LookupFallbackUrl(UrlType urlType)
		{
			Urls urls = JsonConvert.DeserializeObject<Urls>(Resources.CurrentServiceUrls);
			return urls.GetUrlById(urlType.ToJsonPropertyString());
		}

		private static string StripProtocol(string fullUrl)
		{
			int colonSlashSlashIndex = fullUrl.IndexOf("://", StringComparison.Ordinal);
			if (colonSlashSlashIndex < 0)
				return fullUrl;
			return fullUrl.Substring(colonSlashSlashIndex + 3);
		}
	}

	[SuppressMessage("ReSharper", "InconsistentNaming")]
	class Urls
	{
		public List<Url> urls { get; set; }

		public string GetUrlById(string id)
		{
			return urls.Single(u => u.id == id).url;
		}
	}

	[SuppressMessage("ReSharper", "InconsistentNaming")]
	class Url
	{
		public string id { get; set; }
		public string url { get; set; }
	}
}
