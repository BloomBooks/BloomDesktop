using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using Bloom.Collection;
using L10NSharp;
using Newtonsoft.Json;
using Sentry;
using SIL.Reporting;

namespace Bloom.Api
{
    /// <summary>
    /// This class handles requests for internationalization. It uses the L10NSharp LocalizationManager to look up values.
    /// </summary>
    public class I18NApi
    {
        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // We get lots of these requests, and they don't use any non-local data except the LocalizationManager,
            // which is designed to be thread-safe for lookup functions. So we can take advantage of parallelism here.
            apiHandler.RegisterEndpointHandler(
                "i18n/loadStrings",
                HandleI18nLoadStrings,
                false,
                false
            );
            apiHandler.RegisterEndpointHandler("i18n/translate", HandleI18nTranslate, false, false);
            apiHandler.RegisterEndpointHandler(
                "i18n/getStringInLang",
                HandleGetStringInLang,
                false,
                false
            );
            apiHandler.RegisterEndpointHandler("i18n/uilang", HandleI18nUiLang, false, false);
        }

        public void HandleI18nLoadStrings(ApiRequest request)
        {
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
                        Debug.Fail(
                            "Debug Only:"
                                + error.Message
                                + Environment.NewLine
                                + "A bug reported at this location is BL-923"
                        );
                        //Until BL-923 is fixed (hard... it's a race condition, it's better to swallow this for users
                    }
                }
            }
            request.ReplyWithJson(JsonConvert.SerializeObject(d));
        }

        /// <summary>
        /// Try to get the string in the language requested. Failing that, try the book languages,
        /// the current UI language, the current fallback languages, and finally English.
        /// Returns a json giving a result and the language it is actually in.
        /// </summary>
        /// <param name="request"></param>
        private void HandleGetStringInLang(ApiRequest request)
        {
            var parameters = request.Parameters;
            string id = parameters["key"];
            string langId = parameters["langId"];
            var langsToTry = new List<string>(new[] { langId });
            void addLangIfMissing(string lang)
            {
                if (!string.IsNullOrEmpty(lang) && !langsToTry.Contains(lang))
                {
                    langsToTry.Add(lang);
                }
            }
            if (request.CurrentBook != null)
            {
                addLangIfMissing(request.CurrentBook.BookData.Language1Tag);
                addLangIfMissing(request.CurrentBook.BookData.MetadataLanguage1Tag);
                addLangIfMissing(request.CurrentBook.BookData.MetadataLanguage2Tag);
            }
            addLangIfMissing(LocalizationManager.UILanguageId);
            foreach (var lang in LocalizationManager.FallbackLanguageIds)
            {
                addLangIfMissing(lang);
            }
            addLangIfMissing("en");
            var text = GetTranslationInLang(id, langsToTry, out string langFound);
            request.ReplyWithJson(new { text, langFound });
        }

        public void HandleI18nTranslate(ApiRequest request)
        {
            var parameters = request.Parameters;
            string id = parameters["key"];
            string englishText = parameters["englishText"];
            string langId = parameters["langId"];
            string dontWarnIfMissing = parameters["dontWarnIfMissing"];
            bool isDontWarnIfMissing =
                dontWarnIfMissing != null && dontWarnIfMissing.Equals("true");
            string langTag1;
            string langTagM1;
            string langTagM2;
            if (request.CurrentBook != null)
            {
                langTag1 = request.CurrentBook.BookData.Language1Tag;
                langTagM1 = request.CurrentBook.BookData.MetadataLanguage1Tag;
                langTagM2 = request.CurrentBook.BookData.MetadataLanguage2Tag;
            }
            else
            {
                langTag1 = request.CurrentCollectionSettings.Language1.Tag;
                langTagM1 = request.CurrentCollectionSettings.Language2.Tag;
                langTagM2 = request.CurrentCollectionSettings.Language3?.Tag ?? "";
            }
            langId = langId.Replace("V", langTag1);
            langId = langId.Replace("N1", langTagM1);
            langId = langId.Replace("N2", langTagM2);
            langId = langId.Replace("UI", LocalizationManager.UILanguageId);
            if (GetSomeTranslation(id, langId, out var localizedString))
            {
                // Ensure that we actually have a value for localized string.  (This should already be true, but I'm paranoid.)
                if (localizedString == null)
                    localizedString = englishText;
                request.ReplyWithJson(new { text = localizedString, success = true });
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
                            if (!isDontWarnIfMissing)
                                ReportL10NMissingString(
                                    id,
                                    englishText,
                                    UrlPathString
                                        .CreateFromUrlEncodedString(parameters["comment"] ?? "")
                                        .NotEncoded
                                );
                            idFound = false;
                        }
                        else
                        {
                            //ok, so we don't have it translated yet. Make sure it's at least listed in the things that can be translated.
                            // And return the English string, which is what we would do the next time anyway.  (BL-3374)

                            //This is what we had before 5.5: LocalizationManager.GetDynamicString("Bloom", id, englishText);
                            // That apparently just gives us back whatever we already have in `englishText`, which is "" if
                            // the code did not have a duplicate of the string. We want to support the ideal (in Hatton's mind)
                            // that duplication is just asking for trouble by allowing the code to not know what the English is.
                            // So now, we just do this which just means "ah well, what's the English?".
                            GetSomeTranslation(id, "en", out englishText);
                        }
                    }
                }
                request.ReplyWithJson(new { text = englishText, success = idFound });
            }
        }

        public void HandleI18nUiLang(ApiRequest request)
        {
            request.ReplyWithText(LocalizationManager.UILanguageId);
        }

        static string GetTranslationInLang(string id, List<string> langsToTry, out string langFound)
        {
            var result = LocalizationManager.GetString(
                id,
                "whatever",
                "no comment",
                langsToTry,
                out langFound
            );
            if (langFound == "en")
            {
                // The stupid GetString method refuses to return the English from the XLF, even if
                // we ask for it. Even if we pass null as the English, it asserts rather than getting it from the xlf.
                // Have to try another way.
                return GetLocalizedStringInOneLanguage(id, "en");
            }
            Debug.Assert(!string.IsNullOrEmpty(result), "No localization entry for " + id);
            return result;
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
            langsToTry.AddRange(LocalizationManager.FallbackLanguageIds.Except(new[] { "en" }));
            foreach (var langId in langsToTry)
            {
                if (LocalizationManager.GetIsStringAvailableForLangId(id, langId))
                {
                    val = GetLocalizedStringInOneLanguage(id, langId);
                    return true;
                }
            }

            val = null;
            return false;
        }

        public static string GetTranslation(string id)
        {
            if (LocalizationManager.UILanguageId != "en")
            {
                // Try to get a localization normally, but if we don't find one, it will fall back to
                // the "English" passed as the second argument, rather than the English in the XLF.
                var result = LocalizationManager.GetString(id, "***don'tusethis***");
                if (result != "***don'tusethis***")
                    return result;
            }

            // Use some magic to get around the LM's determination that the default must be the
            // English in the code, and just get what the XLF has for English.
            var result2 = GetLocalizedStringInOneLanguage(id, "en");
            if (string.IsNullOrEmpty(result2))
            {
                return id; // sometimes this might be OK
            }
            return result2;
        }

        private static string GetLocalizedStringInOneLanguage(string id, string langId)
        {
            // tricky. It might be in Bloom, or BloomMediumPriority, or BloomLowPriority.
            // Can't use most of the LocalizationManager methods, because they demand
            // an English default, and we don't have one (and typically don't want to have ... if langId is English,
            // or no other localization is available, we want the English in the XLF). So, we try in one,
            // using null as the English, so we won't get anything if not in that one. Then try the next...
            var localizedString = LocalizationManager.GetDynamicStringOrEnglish(
                "Bloom",
                id,
                null,
                null,
                langId
            );
# if DEBUG
            // Check for an error in the XLF content that can mess up localization.
            // If L10nSharp finds braces in the string such that String.Format can't handle
            // them, it will choke, and return the English. This is meant to defend against
            // translators messing up data placeholders in a way that would cause a crash.
            // However, it can bite us if we wanted to insert something other than placeholders,
            // using braces, like some Markdown formatting, resulting in the English having
            // braces that String.Format doesn't like. So we added this to warn us.
            if (!string.IsNullOrEmpty(localizedString))
            {
                var braceCount = localizedString.Count(c => c == '{');
                try
                {
                    // This is similar to the code that L10NSharp uses itself to check whether
                    // the string is OK.
                    object[] args = Enumerable.Repeat("ar" as object, braceCount).ToArray();
                    string.Format(localizedString, args);
                }
                catch (Exception e)
                {
                    ErrorReport.NotifyUserOfProblem(
                        "Debug Only: '"
                            // This replacement guards against anything going wrong if our error reporting tries
                            // to use String.Format on the message.
                            + localizedString.Replace("{", "(brace)").Replace("}", "(close brace)")
                            + "' uses braces in a way that L10nSharp will not handle. Localization will not happen for it."
                    );
                }
            }
#endif
            if (string.IsNullOrEmpty(localizedString) || localizedString == id)
                localizedString = LocalizationManager.GetDynamicStringOrEnglish(
                    "BloomMediumPriority",
                    id,
                    null,
                    null,
                    langId
                );
            if (string.IsNullOrEmpty(localizedString) || localizedString == id)
                localizedString = LocalizationManager.GetDynamicStringOrEnglish(
                    "BloomLowPriority",
                    id,
                    null,
                    null,
                    langId
                );
            return localizedString;
        }

        private static bool IsTemplateBookKey(string key)
        {
            return key.StartsWith("TemplateBooks.BookName")
                || key.StartsWith("TemplateBooks.PageLabel")
                || key.StartsWith("TemplateBooks.PageDescription");
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
        public static string GetTranslationDefaultMayNotBeEnglish(
            string key,
            string defaultCurrent,
            string comment = null
        )
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
                    translation = LocalizationManager.GetDynamicStringOrEnglish(
                        "Bloom",
                        key,
                        null,
                        null,
                        "en"
                    );
                    if (string.IsNullOrWhiteSpace(translation))
                    {
                        // try medium priority
                        translation = LocalizationManager.GetDynamicStringOrEnglish(
                            "BloomMediumPriority",
                            key,
                            null,
                            null,
                            "en"
                        );
                    }
                    if (string.IsNullOrWhiteSpace(translation))
                    {
                        // try low priority
                        translation = LocalizationManager.GetDynamicStringOrEnglish(
                            "BloomLowPriority",
                            key,
                            null,
                            null,
                            "en"
                        );
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
                LocalizationManager.GetDynamicString(
                    "Bloom",
                    "CopyToDistributionXlf_" + id,
                    englishText,
                    comment
                );

                var longMsg = String.Format(
                    "Dear Developer: Ignore this if you are looking at a 3rd-party book that we don't ship with Bloom. "
                        + "Please add this dynamic string to the english.xlf file: Id=\"{0}\" English =\"{1}\". "
                        + "The code at this time cannot add this for you, but we have created an element in your local xlf which you can copy over. "
                        + "Search for CopyToDistributionXlf_, and remember to remove that from the ID. It needs to be "
                        + "added to the en.xlf.",
                    id,
                    englishText
                );
                NonFatalProblem.Report(
                    ModalIf.None,
                    PassiveIf.Alpha,
                    $"Missing l10n EnglishText='{englishText}', id='{id}'",
                    longMsg,
                    skipSentryReport: true
                );
            }
            // Below, we already only toast if Alpha, but we add the if condition here to prevent logging the error.
            // These tend to overwhelm user logs.
            else if (ApplicationUpdateSupport.ChannelName.Equals("Alpha"))
            {
                NonFatalProblem.Report(
                    ModalIf.None,
                    PassiveIf.Alpha,
                    $"Missing l10n: {englishText}",
                    $"Ignore this if you are looking at a 3rd-party book that does not ship with Bloom directly. Otherwise, please report that {id} needs to be added to the en.xlf.",
                    skipSentryReport: true
                );
            }
        }
    }
}
