using System.Collections.Generic;
using System.Linq;
using Bloom.SafeXml;
using Newtonsoft.Json;

namespace Bloom.Book
{
    public partial class HtmlDom
    {
        /// <summary>
        /// Gets the text of the book, as a JSON array of objects.  Each object
        /// contains the text for each language in a single block.
        /// </summary>
        //  [
        //    {
        //        "en":"This is a test.",
        //        "es":"Esta es una prueba."
        //    },
        //    {
        //        "en":"This is another test.",
        //        "es":"Esta es otra prueba."
        //    }
        //  ]
        public string GetTextsJson()
        {
            var translationGroups = new List<Dictionary<string, string>>();

            foreach (
                SafeXmlElement transGroup in _dom.SafeSelectNodes(
                    "//div[contains(@class, 'bloom-translationGroup')]"
                )
            )
            {
                var textDict = new Dictionary<string, string>();
                foreach (
                    SafeXmlElement editable in transGroup.SafeSelectNodes(
                        "./div[contains(@class, 'bloom-editable')]"
                    )
                )
                {
                    string lang = editable.GetAttribute("lang");
                    // Select all text nodes that are not descendants of label elements.
                    // i.e. filter out bubble text.
                    var textNodes = editable.SafeSelectNodes(".//text()[not(ancestor::label)]");
                    string text = string.Join("", textNodes.Select(n => n.InnerText)).Trim();
                    if (!string.IsNullOrEmpty(lang) && !string.IsNullOrEmpty(text))
                    {
                        textDict[lang] = text;
                    }
                }
                if (textDict.Count > 0)
                {
                    translationGroups.Add(textDict);
                }
            }

            return JsonConvert.SerializeObject(translationGroups);
        }
    }
}
