using System;
using System.Globalization;
using SIL.WritingSystems;

namespace Bloom.ToPalaso
{
    /// <summary>
    /// Wrappers for the IetfLanguageTag language-name methods that Bloom uses.
    /// </summary>
    /// <remarks>
    /// IetfLanguageTag.GetGeneralCode, which all of these depend on, finds the end of the
    /// language subtag with a culture-sensitive IndexOf("-"). Under a Thai regional format
    /// (th-TH), that search ignores the hyphen and "matches" at position 0, so every tag's
    /// general code comes out as "", CultureInfo.GetCultureInfo("") is the invariant culture,
    /// and every language is named "Invariant Language" (BL-16945). libpalaso also caches
    /// those wrong names, so the wrappers must be used for every lookup, not just some.
    /// Running the lookups with an invariant CurrentCulture makes that search ordinal. Only
    /// CurrentCulture is changed: CultureInfo.DisplayName follows CurrentUICulture, which must
    /// stay as it is. These can go once libpalaso's GetGeneralCode is fixed.
    /// </remarks>
    public static class IetfLanguageTagExtra
    {
        /// <summary>
        /// IetfLanguageTag.GetNativeLanguageNameWithEnglishSubtitle, safe under any regional format.
        /// </summary>
        public static string GetNativeLanguageNameWithEnglishSubtitle(string code)
        {
            return WithInvariantCulture(() =>
                IetfLanguageTag.GetNativeLanguageNameWithEnglishSubtitle(code)
            );
        }

        /// <summary>
        /// IetfLanguageTag.GetLocalizedLanguageName, safe under any regional format.
        /// </summary>
        public static string GetLocalizedLanguageName(string languageTag, string uiLanguageTag)
        {
            return WithInvariantCulture(() =>
                IetfLanguageTag.GetLocalizedLanguageName(languageTag, uiLanguageTag)
            );
        }

        /// <summary>
        /// IetfLanguageTag.GetBestLanguageName, safe under any regional format.
        /// </summary>
        public static bool GetBestLanguageName(string isoCode, out string name)
        {
            string result = null;
            var found = WithInvariantCulture(() =>
                IetfLanguageTag.GetBestLanguageName(isoCode, out result)
            );
            name = result;
            return found;
        }

        /// <summary>
        /// IetfLanguageTag.GetGeneralCode, safe under any regional format.
        /// </summary>
        public static string GetGeneralCode(string code)
        {
            return WithInvariantCulture(() => IetfLanguageTag.GetGeneralCode(code));
        }

        private static T WithInvariantCulture<T>(Func<T> lookup)
        {
            var currentCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                return lookup();
            }
            finally
            {
                CultureInfo.CurrentCulture = currentCulture;
            }
        }
    }
}
