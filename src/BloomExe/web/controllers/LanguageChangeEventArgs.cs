using System;

namespace Bloom.WebLibraryIntegration
{
    /// <summary>
    /// Class for event args of CollectionSettingsApi.LanguageChange
    /// </summary>
    public class LanguageChangeEventArgs : EventArgs
    {
        // Should be kept in sync with ILanguageData in LanguageChooserDialog.tsx
        public string LanguageTag { get; set; }
        public string DefaultName { get; set; }
        public string DesiredName { get; set; }
        public bool? IsRtl { get; set; }
        public string Country { get; set; }

        /// <summary>
        /// The script code the language chooser settled on (e.g. "Arab"). This is what IsRtl was
        /// derived from, so it is the grouping that tells us whose right-to-left data is wrong.
        /// Analytics only -- nothing in Bloom stores it.
        /// </summary>
        public string Script { get; set; }
    }
}
