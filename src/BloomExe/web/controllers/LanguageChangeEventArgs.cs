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
        /// This asks "is the name custom?". PublishApi's sign-language handler asks a different
        /// question -- whether the name differs from the one already stored in the collection --
        /// and so can answer differently for the same selection. See
        /// BloomTests/web/controllers/LanguageChangeEventArgsTests, which pins both.
        /// </remarks>
        public bool IsCustomName => DesiredName != DefaultName;
    }
}
