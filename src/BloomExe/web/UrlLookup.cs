using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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

		public static string LookupUrl(UrlType urlType, bool sandbox = false, bool excludeProtocolPrefix = false)
		{
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
			try
			{
				using (var s3Client = new BloomS3Client(null))
				{
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
				Logger.WriteEvent("Exception while attemping look up of URL type " + urlType + ": " + e);
			}
			url = null;
			return false;
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
