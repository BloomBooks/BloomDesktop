using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Bloom.Collection;
using L10NSharp;
using Newtonsoft.Json;

namespace Bloom.Api
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
						var post = info.GetPostDataWhenFormEncoded();

						if (post != null)
						{
							foreach (string key in post.Keys)
							{
								try
								{
									if (!d.ContainsKey(key))
									{
										var translation = GetTranslationDefaultMayNotBeEnglish(key, post[key]);
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
					var parameters = info.GetQueryParameters();
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
						// it's ok if we don't have a translation, but if the string isn't even in the list of things that need translating,
						// then we want to remind the developer to add it to the english tmx file.
						if(!LocalizationManager.GetIsStringAvailableForLangId(id, "en"))
						{
							// starting with 3.8, we're getting failed lookups when we look for template pages that may be
							// user-generated (i.e., if the dev has a custom template, that isn't a string that should be
							// added to the Bloom distribution). Perhaps we could just detect that special case, but for now
							// I'm dealing with a merge conflict with 3.7 so I'm just going to do what 3.8 did and keep all
							// these messages passive.
							//var modal = ApplicationUpdateSupport.ChannelName.StartsWith("Developer/") ? ModalIf.All : ModalIf.None;
							var modal = ModalIf.None;
							var longMsg =
								String.Format(
									"Dear Developer: Please add this dynamic string to the english.tmx file: Id=\"{0}\" English =\"{1}\"", id,
									englishText);

							NonFatalProblem.Report(modal, PassiveIf.Alpha, longMsg);
						}
						else // we *could* do this even if the above error is detected, but it just masks the problem then. So let's not.
						{
							//ok, so we don't have it translated yet. Make sure it's at least listed in the things that can be translated.
							// And return the English string, which is what we would do the next time anyway.  (BL-3374)
							LocalizationManager.GetDynamicString("Bloom", id, englishText);
						}
						info.ContentType = "text/plain";
						info.WriteCompleteOutput(englishText);
						return true;
					}
					break;
			}

			return false;
		}

		/// <summary>
		/// We want the translation of the specified key in the current UI language.
		/// The normal LocalizationManager call expects to be passed an English default, which it
		/// will return if the language is English or if it has no translation for the string in
		/// the target language.
		/// The current default passed as an argument typically comes from the content of the
		/// element whose data-i18n attribute is the key in this method. However, this string may come
		/// from an earlier loclization and therefore NOT be English. If we pass it as the english
		/// default, we will get it back unchanged if the UI language has changed (back) to English.
		/// We will also get something other than English as a default for any langauge in which we
		/// don't have the localization.
		/// </summary>
		/// <param name="key"></param>
		/// <param name="defaultCurrent"></param>
		/// <returns></returns>
		public static string GetTranslationDefaultMayNotBeEnglish(string key, string defaultCurrent)
		{
			string translation;
			if (LocalizationManager.GetIsStringAvailableForLangId(key, LocalizationManager.UILanguageId))
			{
				// If we HAVE the string in the desired localization, we don't need
				// a default and can just return the localized string; not passing a default ensures that
				// even in English we get the true English string for this ID from the TMX.
				translation = LocalizationManager.GetDynamicString("Bloom", key, null);
			}
			else
			{
				// We don't have the string in the desired localization, so will return the English.
				translation = LocalizationManager.GetDynamicStringOrEnglish("Bloom", key, null, null, "en");
				// If somehow we don't have even an English version of it, keep whatever was in the element
				// to begin with.
				if (string.IsNullOrWhiteSpace(translation))
				{
					translation = defaultCurrent;
				}
			}
			return translation;
		}
	}
}
