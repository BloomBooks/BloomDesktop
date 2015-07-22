using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Bloom.Collection;
using L10NSharp;
using Newtonsoft.Json;

namespace Bloom.web
{
	/// <summary>
	/// This class handles requests for internationalization. It uses the L10NSharp LocalizationManager to look up values.
	/// </summary>
	static class I18NHandler
	{
		private static bool _localizing = false;

		public static bool HandleRequest(string localPath, IRequestInfo info, CollectionSettings currentCollectionSettings)
		{
			var lastSep = localPath.IndexOf("/", System.StringComparison.Ordinal);
			var lastSegment = (lastSep > -1) ? localPath.Substring(lastSep + 1) : localPath;

			switch (lastSegment)
			{
				case "loadStrings":

					while (_localizing)
					{
						Thread.Sleep(0);
					}

					try
					{
						_localizing = true;

						var d = new Dictionary<string, string>();
						var post = info.GetPostData();

						if (post != null)
						{
							foreach (string key in post.Keys)
							{
								try
								{
									if (!d.ContainsKey(key))
									{
										var translation = LocalizationManager.GetDynamicString("Bloom", key, post[key]);
										d.Add(key, translation);
									}
								}
								catch (Exception error)
								{
									Debug.Fail("Debug Only:" +error.Message+Environment.NewLine+"A bug reported at this location is BL-923");
									//Until BL-923 is fixed (hard... it's a race condition, it's better to swallow this for users
								}
							}
						}

						info.ContentType = "application/json";
						info.WriteCompleteOutput(JsonConvert.SerializeObject(d));
						return true;
					}
					finally
					{
						_localizing = false;
					}
					break;

				case "translate":
					var parameters = info.GetQueryString();
					string id = parameters["key"];
					string englishText = parameters["englishText"];
					string langId = parameters["langId"];
					langId = langId.Replace("V", currentCollectionSettings.Language1Iso639Code);
					langId = langId.Replace("N1", currentCollectionSettings.Language2Iso639Code);
					langId = langId.Replace("N2", currentCollectionSettings.Language3Iso639Code);
					langId = langId.Replace("UI", LocalizationManager.UILanguageId);
					if (LocalizationManager.GetIsStringAvailableForLangId(id, langId))
					{
						info.ContentType = "text/plain";
						info.WriteCompleteOutput(LocalizationManager.GetDynamicStringOrEnglish("Bloom", id, englishText, null, langId));
						return true;
					}
					else
					{
						//ok, so we don't have it translated yet. Make sure it's at least listed in the things that can be translated.
						LocalizationManager.GetDynamicString("Bloom", id, englishText);
						return false;
					}
					break;
			}

			return false;
		}
	}
}
