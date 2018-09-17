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

								// Now that end users can create templates, it's annoying to report that their names,
								// page labels, and page descriptions don't have localizations.
								if (IsTemplateBookKey(key))
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
					langId = langId.Replace("V", request.CurrentCollectionSettings.Language1Iso639Code);
					langId = langId.Replace("N1", request.CurrentCollectionSettings.Language2Iso639Code);
					langId = langId.Replace("N2", request.CurrentCollectionSettings.Language3Iso639Code);
					langId = langId.Replace("UI", LocalizationManager.UILanguageId);
					if (LocalizationManager.GetIsStringAvailableForLangId(id, langId))
					{
						// tricky. It might be in Bloom, or it might be in BloomLowPriority. Must be somewhere, because
						// we know it's available. Can't use GetString, because it's not a literal string. So, we try in one,
						// using null as the English, so we won't get anything if not in that one. Then try the other.
						// Just in case something unexpected happens, we do pass the english the second time.
						var localizedString = LocalizationManager.GetDynamicStringOrEnglish("Bloom", id, null, null, langId);
						if (localizedString == null)
							localizedString = LocalizationManager.GetDynamicStringOrEnglish("Bloom", id, englishText, null, langId);
						request.ReplyWithText(localizedString);
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
								}
								else
								{
									//ok, so we don't have it translated yet. Make sure it's at least listed in the things that can be translated.
									// And return the English string, which is what we would do the next time anyway.  (BL-3374)
									LocalizationManager.GetDynamicString("Bloom", id, englishText);
								}
							}
						}
						request.ReplyWithText(englishText);
					}
					break;
				default:
					request.Failed();
					break;
			}
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
			if (LocalizationManager.GetIsStringAvailableForLangId(key, LocalizationManager.UILanguageId))
			{
				// If we HAVE the string in the desired localization, we don't need
				// a default and can just return the localized string; not passing a default ensures that
				// even in English we get the true English string for this ID from the XLF.
				translation = LocalizationManager.GetDynamicString("Bloom", key, null);
				if (string.IsNullOrWhiteSpace(translation))
				{
					// try low priority
					translation = LocalizationManager.GetDynamicString("BloomLowPriority", key, null);
				}
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
			if (LocalizationManager.IgnoreExistingEnglishXliffFiles)
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
				NonFatalProblem.Report(ModalIf.None, PassiveIf.Alpha, "Missing l10n: " + englishText, longMsg);
			}
			else
			{
				NonFatalProblem.Report(ModalIf.None, PassiveIf.Alpha, "Missing l10n: " + englishText,
					"Ignore this if you are looking at a 3rd-party book that does not ship with Bloom directly. " +
					"Otherwise, please report that " + id + " needs to be " +
					"added to the en.xlf, so that it can show up in the list of things to be localized even " +
					"when the user has not encountered this part of the interface yet.");
			}
		}
	}
}
