using System;
using System.Collections.Generic;
using Bloom.Api;
using Newtonsoft.Json;
using System.Collections.Concurrent;

namespace Bloom.FontProcessing
{
	public class FontsApi
	{
		public const string kApiUrlPart = "fonts/";
		private static ConcurrentDictionary<string, FontMetadata> _fontNameToMetadata = new ConcurrentDictionary<string, FontMetadata>();
		private static FontFileFinder _finder;
		private static object _fontMetadataLock = new object();

		public FontsApi()
		{
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "names", HandleNamesRequest, false);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "metadata", HandleMetadataRequest, false);
		}

		private void HandleNamesRequest(ApiRequest request)
		{
			var list = SortedListOfFontNames();
			request.ReplyWithJson(JsonConvert.SerializeObject(new { fonts = list }));
		}

		private void HandleMetadataRequest(ApiRequest request)
		{
			request.ReplyWithJson(JsonConvert.SerializeObject(AvailableFontMetadata));
		}

		public static IEnumerable<string> SortedListOfFontNames()
		{
			var list = new List<string>(Browser.NamesOfFontsThatBrowserCanRender());
			list.Sort();
			return list;
		}

		/// <summary>
		/// Return a list of FontMetadata objects for all fonts on the system.  This will be SLOW the
		/// first time it is called, but caches the result so that it will be fast in any later calls.
		/// </summary>
		public static IEnumerable<FontMetadata> GetAllFontMetadata()
		{
			lock(_fontMetadataLock)
			{
				if (_finder != null)
					return GetFontMetadataSortedByName();

				_finder = FontFileFinder.GetInstance(isReuseAllowed: true);
				foreach (var name in SortedListOfFontNames())
				{
					var group = _finder.GetGroupForFont(name);
					if (group == null || string.IsNullOrEmpty(group.Normal))
						continue;
					var meta = new FontMetadata(name, group);
					_fontNameToMetadata[name] = meta;
				}
				return GetFontMetadataSortedByName();
			}
		}

		private static IEnumerable<FontMetadata> GetFontMetadataSortedByName()
		{
			var list = new List<FontMetadata>(_fontNameToMetadata.Values);
			list.Sort((a,b) => a.name.CompareTo(b.name));
			return list;
		}

		/// <summary>
		/// Return a list of FontMetadata objects for all fonts that we currently know about.
		/// </summary>
		public static IEnumerable<FontMetadata> AvailableFontMetadata => GetFontMetadataSortedByName();

		public static IDictionary<string, FontMetadata> AvailableFontMetadataDictionary => _fontNameToMetadata;
	}
}
