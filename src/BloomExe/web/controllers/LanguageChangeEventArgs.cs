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
        public string Country { get; set; }
    }
}
