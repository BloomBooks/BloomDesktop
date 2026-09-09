using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Book;
using SIL.IO;
using Directory = System.IO.Directory;

namespace Bloom.FontProcessing
{
    /// <summary>
    /// Works out which font families a book names, by reading the book's stylesheets rather than
    /// by rendering it. A font counts as used if a language that has text in the book names it,
    /// whether or not that text is visible.
    /// </summary>
    public static class FontsUsedInBook
    {
        /// <summary>
        /// Examine the stylesheets in the book folder and collect the font families they mention.
        /// Note that the process used by ePub and bloomPub publication to determine fonts is more
        /// complicated, using the DOM in an actual browser.
        /// </summary>
        /// <returns>Enumerable of font names</returns>
        public static IEnumerable<string> GetFontsUsed(string bookPath)
        {
            string bookHtmContent = null;
            string defaultLangStylesPath = null;

            var result = new HashSet<string>();
            // Css for styles are contained in the actual html
            foreach (
                var filePath in Directory
                    .EnumerateFiles(bookPath, "*.*")
                    .Where(f => f.EndsWith(".css") || f.EndsWith(".htm") || f.EndsWith(".html"))
            )
            {
                var fileContents = RobustFile.ReadAllText(filePath, Encoding.UTF8);

                if (filePath.EndsWith(".htm"))
                    bookHtmContent = fileContents;
                else if (filePath.EndsWith("defaultLangStyles.css"))
                {
                    defaultLangStylesPath = filePath;
                    // Delay processing defaultLangStyles to the end when we know we have the htm content.
                    continue;
                }

                HtmlDom.FindFontsUsedInCss(fileContents, result, false);
            }

            ProcessDefaultLangStyles(bookHtmContent, defaultLangStylesPath, result);

            return result;
        }

        /// <summary>
        /// Special processing is needed for defaultLangStyles.css.
        /// This file is designed to hold information about each language seen by this book and its ancestors.
        /// But that means we may have font information for a language not present in this version of the book.
        /// We don't want to include those fonts.
        /// </summary>
        private static void ProcessDefaultLangStyles(
            string bookHtmContent,
            string defaultLangStylesPath,
            HashSet<string> result
        )
        {
            if (bookHtmContent == null || defaultLangStylesPath == null)
                return;
            // Note that this code does not return all the fonts that are served with Bloom
            // (Andika, Andika New Basic, and ABeeZee), but only the ones that are actually
            // used in the book.
            var htmlDom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtml(bookHtmContent, false));
            var langToFont = htmlDom.GetDefaultFontsForLanguages(
                Path.GetDirectoryName(defaultLangStylesPath)
            );
            if (langToFont != null)
            {
                foreach (var pair in langToFont)
                    result.Add(pair.Value);
            }
        }
    }
}
