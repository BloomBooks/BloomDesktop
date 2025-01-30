using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SafeXml;
using Bloom.Spreadsheet;
using Bloom.web;

namespace BloomTests.Spreadsheet
{
    /// <summary>
    /// Modifies SpreadsheetImporter by overriding the methods that use a real WebView2Browser
    /// so that we don't actually have to get one. This prevents some weird problems on TeamCity.
    /// </summary>
    internal class TestSpreadsheetImporter : SpreadsheetImporter
    {
        public TestSpreadsheetImporter(
            IBloomWebSocketServer webSocketServer,
            HtmlDom destinationDom,
            string pathToSpreadsheetFolder = null,
            string pathToBookFolder = null,
            CollectionSettings collectionSettings = null
        )
            : base(
                webSocketServer,
                destinationDom,
                pathToSpreadsheetFolder,
                pathToBookFolder,
                collectionSettings
            ) { }

        protected override Task<Browser> GetBrowserAsync()
        {
            throw new ApplicationException("Must not use real browser in unit testing");
        }

        // A dreadfully crude approximation, but good enough for these tests.
        protected override Task<string> GetMd5Async(SafeXmlElement elt)
        {
            return Task.FromResult(elt.InnerText.GetHashCode().ToString());
        }

        // This is also a crude approximation; it won't, for example, handle any sentence-ending punctuation
        // besides period. But it covers the text used in the relevant tests.
        protected override async Task<string[]> GetSentenceFragmentsAsync(string text)
        {
            return GetFrags(text).ToArray();
        }

        IEnumerable<string> GetFrags(string text)
        {
            var sentences = text.Replace("\\'", "'").Split(new char[] { '.', '|' });
            for (int i = 0; i < sentences.Length - 1; i++)
            {
                if (sentences[i].EndsWith(" ") || sentences[i].EndsWith(","))
                    sentences[i] = sentences[i] + '|';
                else
                    sentences[i] = sentences[i] + ".";
            }

            yield return "s" + sentences[0];

            foreach (var sentence in sentences.Skip(1))
            {
                var s1 = sentence.TrimStart();
                if (sentence.Length > s1.Length)
                    yield return " " + sentence.Substring(0, sentence.Length - s1.Length);
                if (s1.Length > 0)
                    yield return "s" + s1;
            }
        }
    }
}
