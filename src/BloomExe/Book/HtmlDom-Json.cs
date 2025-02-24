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
                    string text = editable.InnerText.Trim();
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

            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            return serializer.Serialize(translationGroups);
        }

        public void ImportJson(string json)
        {
            // json looks like this
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

            // turn this into a dictionary keyed on the "en" text so that we can look up any group by its english text
            var englishToLanguages = new Dictionary<string, Dictionary<string, string>>();
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            var translationGroups = serializer.Deserialize<List<Dictionary<string, string>>>(json);
            if (translationGroups == null)
                return;
            foreach (var group in translationGroups)
            {
                if (!group.ContainsKey("en"))
                    continue;
                var englishText = group["en"].Trim();
                if (englishToLanguages.ContainsKey(englishText))
                    continue;
                group.Remove("en");
                englishToLanguages[englishText] = group;
            }

            foreach (
                SafeXmlElement transGroup in _dom.SafeSelectNodes(
                    "//div[contains(@class, 'bloom-translationGroup')]"
                )
            )
            {
                var englishEditable = transGroup
                    .SafeSelectNodes("./div[contains(@class, 'bloom-editable') and @lang='en']")
                    .FirstOrDefault();
                if (englishEditable == null)
                    continue;
                var englishText = englishEditable.InnerText.Trim();
                // Find matching text in our import data
                var matchingGroup = englishToLanguages[englishText];
                if (matchingGroup == null)
                    continue;

                // Update or create editables for each language in the matching group
                foreach (var kvp in matchingGroup)
                {
                    string lang = kvp.Key;
                    string text = kvp.Value;

                    var editable = transGroup
                        .SafeSelectNodes(
                            $"./div[contains(@class, 'bloom-editable') and @lang='{lang}']"
                        )
                        .FirstOrDefault();

                    if (editable == null)
                    {
                        editable = _dom.CreateElement("div") as SafeXmlElement;
                        editable.SetAttribute("class", "bloom-editable");
                        editable.SetAttribute("lang", lang);
                        transGroup.AppendChild(editable);
                    }

                    editable.InnerText = text;
                }
            }
        }

        // For each element in the json, every bloom-translationGroup, we get the text of the the bloom-editable that matches the keyLanguage.
        public void SetBookTextOfOneLanguageFromJson(
            string keyLanguage,
            string targetLanguage,
            string jsonArrayOfMultilingualTexts
        )
        {
            var texts = DynamicJson.Parse(jsonArrayOfMultilingualTexts);
            var textDictionaries = texts as IList<dynamic>;

            foreach (SafeXmlElement page in GetContentPageElements())
            {
                foreach (
                    SafeXmlElement transGroup in page.SafeSelectNodes(
                        ".//div[contains(@class, 'bloom-translationGroup')]"
                    )
                )
                {
                    // Find the key language editable
                    var keyEditable = transGroup
                        .SafeSelectNodes(
                            $"./div[contains(@class, 'bloom-editable') and @lang='{keyLanguage}']"
                        )
                        .FirstOrDefault();
                    if (keyEditable == null)
                        continue;

                    string keyText = keyEditable.InnerText.Trim();

                    // Find matching text in our import data
                    var matchingDict = textDictionaries?.FirstOrDefault(
                        d =>
                            ((IDictionary<string, object>)d).ContainsKey(keyLanguage)
                            && ((string)d[keyLanguage]).Trim() == keyText
                    );

                    if (
                        matchingDict == null
                        || !((IDictionary<string, object>)matchingDict).ContainsKey(targetLanguage)
                    )
                        continue;

                    string targetText = (string)matchingDict[targetLanguage];
                    if (string.IsNullOrEmpty(targetText))
                        continue;

                    // Find or create target language editable
                    var targetEditable = transGroup
                        .SafeSelectNodes(
                            $"./div[contains(@class, 'bloom-editable') and @lang='{targetLanguage}']"
                        )
                        .FirstOrDefault();
                    if (targetEditable == null)
                    {
                        targetEditable = _dom.CreateElement("div") as SafeXmlElement;
                        targetEditable.SetAttribute("class", "bloom-editable");
                        targetEditable.SetAttribute("lang", targetLanguage);
                        transGroup.AppendChild(targetEditable);
                    }

                    targetEditable.InnerText = targetText;
                }
            }
        }
    }

    // Add new NonEnumerableDynamicObject class to enable proper JSON object serialization without enumeration issues
    public class NonEnumerableDynamicObject : System.Dynamic.DynamicObject
    {
        private readonly Dictionary<string, object> _dict = new Dictionary<string, object>();

        public void Set(string key, object value)
        {
            _dict[key] = value;
        }

        public int DynamicMemberCount => _dict.Count;

        public override bool TryGetMember(System.Dynamic.GetMemberBinder binder, out object result)
        {
            return _dict.TryGetValue(binder.Name, out result);
        }

        public override IEnumerable<string> GetDynamicMemberNames()
        {
            return _dict.Keys;
        }

        // Do not implement IEnumerable to avoid being serialized as a collection of KeyValuePair objects
    }
}
