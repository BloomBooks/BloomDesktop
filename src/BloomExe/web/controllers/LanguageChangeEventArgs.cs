using System;
using Bloom.Book;

namespace Bloom.WebLibraryIntegration
{
    /// <summary>
    /// Class for event args of BookDownload.BookDownloaded
    /// </summary>
    public class LanguageChangeEventArgs : EventArgs
    {
        public string LanguageTag { get; set; }
        public string DefaultName { get; set; }
        public string DesiredName { get; set; }
    }
}
