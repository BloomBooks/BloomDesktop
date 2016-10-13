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
						// Don't report missing strings if they are numbers
						// Enhance: We might get the Javascript to do locale specific numbers someday
						// The C# side doesn't currently have the smarts to do DigitSubstitution
						// See Remark at https://msdn.microsoft.com/en-us/library/system.globalization.numberformatinfo.digitsubstitution(v=vs.110).aspx
						if (IsInteger(id))
						{
							englishText = id;
						}
						else
						{
							// it's ok if we don't have a translation, but if the string isn't even in the list of things that need translating,
							// then we want to remind the developer to add it to the english tmx file.
							if (!LocalizationManager.GetIsStringAvailableForLangId(id, "en"))
							{
								ReportL10NMissingString(id, englishText);
							}
							else
							{
								//ok, so we don't have it translated yet. Make sure it's at least listed in the things that can be translated.
								// And return the English string, which is what we would do the next time anyway.  (BL-3374)
								LocalizationManager.GetDynamicString("Bloom", id, englishText);
							}
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
				// Don't report missing strings if they are numbers
				// Enhance: We might get the Javascript to do locale specific numbers someday
				// The C# side doesn't currently have the smarts to do DigitSubstitution
				// See Remark at https://msdn.microsoft.com/en-us/library/system.globalization.numberformatinfo.digitsubstitution(v=vs.110).aspx
				if (IsInteger(key))
				{
					translation = key;
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
						ReportL10NMissingString(key, translation);
					}
				}
			}
			return translation;
		}

		private static bool IsInteger(string key)
		{
			int dummy;
			return int.TryParse(key, out dummy);
		}

		private static void ReportL10NMissingString(string id, string englishText)
		{
			if (ApplicationUpdateSupport.ChannelName.StartsWith("Developer"))
			{
				//It would be a nice improvement to l10n to allow us to write directly to the source-code TMX file, so that the
				//developer just has to check it in. But for now, we can write out a TMX element to the "local" TMX which the developer 
				//can put in the distribution one. We prefix it with CopyToDistributionTmx_, which he will have to remove, because
				//otherwise the next time we look for this string, it would get found and we would lose the ability to point out the 
				//problem to the developer.
				LocalizationManager.GetDynamicString("Bloom", "CopyToDistributionTmx_" + id, englishText);

				var longMsg =
					String.Format(
						"Dear Developer: Please add this dynamic string to the english.tmx file: Id=\"{0}\" English =\"{1}\". " +
						"The code at this time cannot add this for you, but we have created an element in your local TMX which you can copy over." +
						" Search for CopyToDistributionTmx_, and remember to remove that from the ID. It needs to be " +
						"added to the en.tmx, so that it can show up in the list of things to be localized even " +
						"when the user has not encountered this part of the interface yet.",
						id,
						englishText);
				NonFatalProblem.Report(ModalIf.None, PassiveIf.Alpha, "Missing l10n: " + englishText, longMsg);
			}
			else
			{
				NonFatalProblem.Report(ModalIf.None, PassiveIf.Alpha, "Missing l10n: " + englishText,
					"Please report that " + id + " needs to be " +
					"added to the en.tmx, so that it can show up in the list of things to be localized even " +
					"when the user has not encountered this part of the interface yet.");
			}
		}
	}
}
