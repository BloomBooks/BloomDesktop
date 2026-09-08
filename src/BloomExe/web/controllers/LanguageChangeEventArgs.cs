using System;

namespace Bloom.WebLibraryIntegration
{
    /// <summary>
    /// Class for event args of CollectionSettingsApi.LanguageChange
    /// </summary>
    public class LanguageChangeEventArgs : EventArgs
    {
        // Should be kept in sync with ILanguageData in collection/languageData.ts
        public string LanguageTag { get; set; }
        public string DefaultName { get; set; }
        public string DesiredName { get; set; }
        public bool? IsRtl { get; set; }
        public string Country { get; set; }

        /// <summary>
        /// True when the user typed a name of their own rather than accepting the one the language
        /// chooser offered. WritingSystem.SetName needs this so a custom name gets written to the
        /// collection settings and a default one does not.
        /// </summary>
        /// <remarks>
        /// This is the only rule any caller should use. It compares the two names the chooser
        /// itself sent, so it answers the question the user actually decided. Deriving a "default"
        /// name some other way -- e.g. reading WritingSystem.Name back after setting its Tag, which
        /// PublishApi's sign-language handler used to do (BL-16760) -- asks LibPalaso instead, and
        /// LibPalaso and the chooser do not always agree on a language's name.
        /// </remarks>
        public bool IsCustomName => DesiredName != DefaultName;
    }
}
