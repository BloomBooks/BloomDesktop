using System.Collections.Generic;
using System.Linq;
using Bloom.Api;
using Bloom.SafeXml;

namespace Bloom.Book
{
    /// <summary>
    /// HtmlDom manages the lower-level operations on a Bloom XHTML DOM.
    /// These doms can be a whole book, or just one page we're currently editing.
    /// They are actually XHTML, though when we save or send to a browser, we always convert to plain html.
    /// May also contain a BookInfo, which for certain operations should be kept in sync with the HTML.
    /// </summary>
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
        public string GetBookTextJson()
        {
            var textBlocks = new List<Dictionary<string, string>>();

            foreach (SafeXmlElement page in GetContentPageElements())
            {
                foreach (
                    SafeXmlElement transGroup in page.SafeSelectNodes(
                        ".//div[contains(@class, 'bloom-translationGroup')]"
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
                        if (!string.IsNullOrEmpty(lang))
                        {
                            textDict[lang] = editable.InnerText.Trim();
                        }
                    }
                    if (textDict.Count > 0)
                    {
                        textBlocks.Add(textDict);
                    }
                }
            }
            return DynamicJson.Serialize(textBlocks);
        }

        // For each element in the json, every bloom-translationGroup, we get the text of the the bloom-editable that matches the keyLanguage.
        public void SetBookTextOfOneLanguageFromJson(string keyLanguage, string targetLanguage, string jsonArrayOfMultilingualTexts)
        {
            var texts = DynamicJson.Parse(jsonArrayOfMultilingualTexts);
            var textDictionaries = texts as IList<dynamic>;

            foreach (SafeXmlElement page in GetContentPageElements())
            {
                foreach (SafeXmlElement transGroup in page.SafeSelectNodes(".//div[contains(@class, 'bloom-translationGroup')]"))
                {
                    // Find the key language editable
                    var keyEditable = transGroup.SafeSelectNodes($"./div[contains(@class, 'bloom-editable') and @lang='{keyLanguage}']").FirstOrDefault();
                    if (keyEditable == null)
                        continue;

                    string keyText = keyEditable.InnerText.Trim();

                    // Find matching text in our json
                    var matchingDict = textDictionaries?.FirstOrDefault(d =>
                        ((IDictionary<string, object>)d).ContainsKey(keyLanguage) &&
                        ((string)d[keyLanguage]).Trim() == keyText);

                    if (matchingDict == null || !((IDictionary<string, object>)matchingDict).ContainsKey(targetLanguage))
                        continue;

                    string targetText = (string)matchingDict[targetLanguage];

                    // Find or create target language editable
                    var targetEditable = transGroup.SafeSelectNodes($"./div[contains(@class, 'bloom-editable') and @lang='{targetLanguage}']").FirstOrDefault();
                    if (targetEditable == null)
                    {
                        // Create new editable div for target language
                        targetEditable = (SafeXmlElement)keyEditable.Clone();
                        targetEditable.SetAttribute("lang", targetLanguage);
                        transGroup.AppendChild(targetEditable);
                    }

                    targetEditable.InnerText = targetText;
                }
            }
        }
    }
}
