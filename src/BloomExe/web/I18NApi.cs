using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Bloom.Collection;
using L10NSharp;
using Newtonsoft.Json;

namespace Bloom.Api
{
	/// <summary>
	/// This class handles requests for internationalization. It uses the L10NSharp LocalizationManager to look up values.
	/// </summary>
	public class I18NApi
	{
		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("i18n/", HandleI18nRequest, false);
		}

		public void HandleI18nRequest(ApiRequest request)
		{
			var lastSegment = request.LocalPath().Split(new char[] { '/' }).Last();
			switch (lastSegment)
			{
				case "loadStrings":
					var d = new Dictionary<string, string>();
					var post = request.GetPostDataWhenFormEncoded();

					if (post != null)
					{
						foreach (string key in post.Keys)
						{
							try
							{
								if (d.ContainsKey(key))
									continue;

								var translation = GetTranslationDefaultMayNotBeEnglish(key, post[key]);
								d.Add(key, translation);
							}
							catch (Exception error)
							{
								Debug.Fail("Debug Only:" + error.Message + Environment.NewLine + "A bug reported at this location is BL-923");
								//Until BL-923 is fixed (hard... it's a race condition, it's better to swallow this for users
							}
						}
					}
					request.ReplyWithJson(JsonConvert.SerializeObject(d));
					break;

				case "translate":
					var parameters = request.Parameters;
					string id = parameters["key"];
					string englishText = parameters["englishText"];
					string langId = parameters["langId"];
					string isoCode1 = null;
					string isoCode2 = null;
					string isoCode3 = null;
					if (request.CurrentBook != null)
					{
						isoCode1 = request.CurrentBook.BookData.Language1.Iso639Code;
						isoCode2 = request.CurrentBook.BookData.Language2.Iso639Code;
						isoCode3 = request.CurrentBook.BookData.Language3.Iso639Code;
					}
					else
					{
						isoCode1 = request.CurrentCollectionSettings.Language1.Iso639Code;
						isoCode2 = request.CurrentCollectionSettings.Language2.Iso639Code;
						isoCode3 = request.CurrentCollectionSettings.Language3.Iso639Code;
					}
					langId = langId.Replace("V", isoCode1);
					langId = langId.Replace("N1", isoCode2);
					langId = langId.Replace("N2", isoCode3);
					langId = langId.Replace("UI", LocalizationManager.UILanguageId);
					string localizedString;
					if (GetSomeTranslation(id, langId, out localizedString))
					{
						// Ensure that we actually have a value for localized string.  (This should already be true, but I'm paranoid.)
						if (localizedString == null)
							localizedString = englishText;
						request.ReplyWithJson(new {text = localizedString, success = true });
					}
					else
					{
						var idFound = true;
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
							// Now that end users can create templates, it's annoying to report that their names,
							// page labels, and page descriptions don't have localizations.
							if (IsTemplateBookKey(id))
							{
								englishText = englishText.Trim();
							}
							else
							{
								// it's ok if we don't have a translation, but if the string isn't even in the list of things that need translating,
								// then we want to remind the developer to add it to the english xlf file.
								if (!LocalizationManager.GetIsStringAvailableForLangId(id, "en"))
								{
									ReportL10NMissingString(id, englishText, UrlPathString.CreateFromUrlEncodedString(parameters["comment"]??"").NotEncoded);
									idFound = false;
								}
								else
								{
									//ok, so we don't have it translated yet. Make sure it's at least listed in the things that can be translated.
									// And return the English string, which is what we would do the next time anyway.  (BL-3374)
									LocalizationManager.GetDynamicString("Bloom", id, englishText);
								}
							}
						}
						request.ReplyWithJson(new {text = englishText, success = idFound });
					}
					break;
				case "uilang":
					request.ReplyWithText(LocalizationManager.UILanguageId);
					break;
				default:
					request.Failed();
					break;
			}
		}

		// Get a translation of the specified string, in the specified language,
		// or the closest fallback language (other than English).
		// Needs to use something from the GetDynamicString method family, because GetString is only
		// permitted with fixed, literal ids.
		// However, the dynamic methods all require an appId, and we want to search both
		// Bloom and BloomLowPriority. But if it's not there at all, we need to know that,
		// so callers can report the missing string. If we passed a default English string
		// to GetDynamicString, we couldn't tell whether it was in the English xliff or not.
		// So, we first call GetIsStringAvailableForLangId to see whether it is.
		// But, THAT routine, while smart about trying all the appIds, only tries one language.
		// And we want to search using whatever FallbackLanguageIds are in effect (except English...
		// if it's only in English this routine should return null, and the caller will check
		// whether it's missing from the xliff, report if so, and use the default).
		// So, we loop over the interesting language IDs, and when we find one that has a result,
		// we have to try GetDynamicString on each of the appIds until we find it.
		static bool GetSomeTranslation(string id, string initialLangId, out string val)
		{
			var langsToTry = new List<string>();
			langsToTry.Add(initialLangId);
			langsToTry.AddRange(LocalizationManager.FallbackLanguageIds.Except(new [] {"en"}));
			foreach (var langId in langsToTry)
			{
				if (LocalizationManager.GetIsStringAvailableForLangId(id, langId))
				{
					// tricky. It might be in Bloom, or BloomMediumPriority, or BloomLowPriority. Must be somewhere, because
					// we know it's available. Can't use GetString, because it's not a literal string. So, we try in one,
					// using null as the English, so we won't get anything if not in that one. Then try the next.
					// We don't need to pass English (in fact, it's not passed to this method) because we only come
					// here if LM DOES have a translation in the requested language (which we've made sure is not English)
					var localizedString = LocalizationManager.GetDynamicStringOrEnglish("Bloom", id, null, null, langId);
					if (string.IsNullOrEmpty(localizedString))
						localizedString = LocalizationManager.GetDynamicStringOrEnglish("BloomMediumPriority", id, null, null, langId);
					if (string.IsNullOrEmpty(localizedString))
						localizedString = LocalizationManager.GetDynamicStringOrEnglish("BloomLowPriority", id, null, null, langId);
					val = localizedString;
					return true;
				}
			}

			val = null;
			return false;
		}

		private static bool IsTemplateBookKey(string key)
		{
			return key.StartsWith("TemplateBooks.BookName") ||
					key.StartsWith("TemplateBooks.PageLabel") ||
					key.StartsWith("TemplateBooks.PageDescription");
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
		/// <param name="comment">Localization comment, if any</param>
		/// <returns></returns>
		public static string GetTranslationDefaultMayNotBeEnglish(string key, string defaultCurrent, string comment = null)
		{
			string translation;
			if (!I18NApi.GetSomeTranslation(key, LocalizationManager.UILanguageId, out translation))
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
					if (string.IsNullOrWhiteSpace(translation))
					{
						// try medium priority
						translation = LocalizationManager.GetDynamicStringOrEnglish("BloomMediumPriority", key, null, null, "en");
					}
					if (string.IsNullOrWhiteSpace(translation))
					{
						// try low priority
						translation = LocalizationManager.GetDynamicStringOrEnglish("BloomLowPriority", key, null, null, "en");
					}
					// If somehow we don't have even an English version of it, keep whatever was in the element
					// to begin with.
					if (string.IsNullOrWhiteSpace(translation))
					{
						translation = defaultCurrent;
						// Now that end users can create templates, it's annoying to report that their names,
						// page labels, and page descriptions don't have localizations.
						if (!IsTemplateBookKey(key))
							ReportL10NMissingString(key, translation, comment);
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

		private static void ReportL10NMissingString(string id, string englishText, string comment)
		{
			if (LocalizationManager.IgnoreExistingEnglishTranslationFiles)
			{
				// This will store it in the generated xliff file.
				LocalizationManager.GetDynamicString("Bloom", id, englishText, comment);
				return;
			}
			if (ApplicationUpdateSupport.ChannelName.StartsWith("Developer"))
			{
				//It would be a nice improvement to l10n to allow us to write directly to the source-code XLF file, so that the
				//developer just has to check it in. But for now, we can write out a xliff element to the "local" XLF which the developer
				//can put in the distribution one. We prefix it with CopyToDistributionXlf_, which he will have to remove, because
				//otherwise the next time we look for this string, it would get found and we would lose the ability to point out the
				//problem to the developer.
				LocalizationManager.GetDynamicString("Bloom", "CopyToDistributionXlf_" + id, englishText, comment);

				var longMsg =
					String.Format(
						"Dear Developer: Ignore this if you are looking at a 3rd-party book that we don't ship with Bloom."+
						" Please add this dynamic string to the english.xlf file: Id=\"{0}\" English =\"{1}\". " +
						"The code at this time cannot add this for you, but we have created an element in your local xlf which you can copy over." +
						" Search for CopyToDistributionXlf_, and remember to remove that from the ID. It needs to be " +
						"added to the en.xlf, so that it can show up in the list of things to be localized even " +
						"when the user has not encountered this part of the interface yet.",
						id,
						englishText);
				NonFatalProblem.Report(ModalIf.None, PassiveIf.Alpha, "Missing l10n: " + englishText, longMsg, skipSentryReport: true);
			}
			else
			{
				NonFatalProblem.Report(ModalIf.None, PassiveIf.Alpha, "Missing l10n: " + englishText,
					"Ignore this if you are looking at a 3rd-party book that does not ship with Bloom directly. " +
					"Otherwise, please report that " + id + " needs to be " +
					"added to the en.xlf, so that it can show up in the list of things to be localized even " +
					"when the user has not encountered this part of the interface yet.", skipSentryReport: true);
			}
		}
	}
}
