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

		public FontsApi()
		{
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointLegacy(kApiUrlPart + "names", HandleNamesRequest, false);
			apiHandler.RegisterEndpointLegacy(kApiUrlPart + "metadata", HandleMetadataRequest, false);
		}

		/// <summary>
		/// Return a list of FontMetadata objects for all fonts that were available when GetAllFontMetadata() was run.
		/// If it hasn't been run yet, this will be an empty list.  GetAllFontMetadata() is presumably run at the
		/// beginning of the program.  If the user adds fonts while running Bloom, well, restarting Bloom isn't the
		/// most unexpected thing to do.
		/// </summary>
		public static IEnumerable<FontMetadata> AvailableFontMetadata => GetFontMetadataSortedByName();

		public static IDictionary<string, FontMetadata> AvailableFontMetadataDictionary => _fontNameToMetadata;

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
		/// <remarks>
		/// This method is actually called only once in Program.Main just before Run().  If something
		/// calls this a second time (or calls AvailableFontMetadata) before it finishes, then a partial
		/// list of fonts is returned.
		/// </remarks>
		public static IEnumerable<FontMetadata> GetAllFontMetadata()
		{
			// This return shouldn't be used but might be triggered by a test.
			if (_finder != null)
				return GetFontMetadataSortedByName();

			var starting = DateTime.Now;
			foreach (var name in SortedListOfFontNames())
			{
				_finder = FontFileFinder.GetInstance(isReuseAllowed: true);
				try
				{
					var group = _finder.GetGroupForFont(name);
					if (group == null || string.IsNullOrEmpty(group.Normal))
						continue;
					var meta = new FontMetadata(name, group);
					lock (_fontNameToMetadata)
					{
						_fontNameToMetadata[name] = meta;
					}
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Trying to get font data for \"{name}\" threw: {ex}");
				}
			}
			var list = GetFontMetadataSortedByName();
			var ending = DateTime.Now;
			System.Diagnostics.Debug.WriteLine($"DEBUG: Collecting font metadata took {ending - starting}");
			return list;
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

		private static IEnumerable<FontMetadata> GetFontMetadataSortedByName()
		{
			lock (_fontNameToMetadata)
			{
				var list = new List<FontMetadata>(_fontNameToMetadata.Values);
				list.Sort((a, b) => a.name.CompareTo(b.name));
				return list;
			}
		}
	}
}
