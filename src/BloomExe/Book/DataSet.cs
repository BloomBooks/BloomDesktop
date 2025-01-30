using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SIL.Extensions;
using SIL.Text;

namespace Bloom.Book
{
    /// <summary>
    /// Acts as a cache of values we inject and gather from the document.
    /// </summary>
    public class DataSet
    {
        public DataSet()
        {
            TextVariables = new Dictionary<string, DataSetElementValue>();
            XmatterPageDataAttributeSets =
                new Dictionary<string, ISet<KeyValuePair<string, string>>>();
        }

        public Dictionary<string, DataSetElementValue> TextVariables { get; private set; }

        /// <summary>
        /// The key is the name of the xmatter page (such as frontCover).
        /// The value is a set of data attribute key-value pairs (such as data-backgroundaudio='SoundFile.mp3', data-backgroundaudiovolume='0.5').
        /// These get synchronized between the data div and the actual xmatter pages.
        /// </summary>
        public Dictionary<
            string,
            ISet<KeyValuePair<string, string>>
        > XmatterPageDataAttributeSets { get; }

        public void UpdateGenericLanguageString(string key, XmlString value, bool isCollectionValue)
        {
            var text = new MultiTextBase();
            text.SetAlternative("*", value?.Xml);
            if (TextVariables.ContainsKey(key))
            {
                TextVariables.Remove(key);
            }
            TextVariables.Add(key, new DataSetElementValue(text, isCollectionValue));
        }

        public void UpdateLanguageString(
            string key,
            XmlString value,
            string writingSystemId,
            bool isCollectionValue,
            bool storeEmptyValue = false
        )
        {
            Debug.Assert(
                writingSystemId != "V" && writingSystemId != "N1" && writingSystemId != "N2",
                "UpdateLanguageString may no longer be passed an alias writing system ID"
            );
            DataSetElementValue dataSetElementValue;
            MultiTextBase text;
            if (TextVariables.TryGetValue(key, out dataSetElementValue))
                text = dataSetElementValue.TextAlternatives;
            else
            {
                text = new MultiTextBase();
            }

            // MultiTextBase.SetAlternative() and .SetAnnotationOfAlternativeIsStarred()
            // treat writingSystemId as case insensitive.  This is really bad when we want
            // to store the writing system ID exactly as is to fix a bug that put in an
            // incorrectly capitalized writing system ID.  Getting rid of the alternative
            // form more explicitly works to instantiate the corrected writing system ID.
            // See BL-14038.
            var form = text.Forms.FirstOrDefault(x => x.WritingSystemId == writingSystemId);
            if (form == null)
                text.SetAlternative(writingSystemId, null);

            if (storeEmptyValue && XmlString.IsNullOrEmpty(value))
                text.SetAnnotationOfAlternativeIsStarred(writingSystemId, true); // This allows empty strings to be saved and restored as empty strings.
            else
                text.SetAlternative(writingSystemId, value?.Xml);
            TextVariables.Remove(key);
            if (text.Count > 0)
                TextVariables.Add(key, new DataSetElementValue(text, isCollectionValue));
        }

        public void AddLanguageString(
            string key,
            XmlString value,
            string writingSystemId,
            bool isCollectionValue
        )
        {
            if (!TextVariables.ContainsKey(key))
            {
                var text = new MultiTextBase();
                TextVariables.Add(key, new DataSetElementValue(text, isCollectionValue));
            }
            TextVariables[key].TextAlternatives.SetAlternative(writingSystemId, value?.Xml);
        }

        /// <summary>
        /// Updates (or adds) the set of attributes which is associated with a particular xmatter page.
        /// For example, xmatter page with key "frontCover" has data-backgroundaudio and data-backgroundaudiovolume attributes.
        /// </summary>
        public void UpdateXmatterPageDataAttributeSet(
            string key,
            ISet<KeyValuePair<string, string>> xmatterPageDataAttributeSet
        )
        {
            if (XmatterPageDataAttributeSets.ContainsKey(key))
                XmatterPageDataAttributeSets[key] = xmatterPageDataAttributeSet;
            else
                XmatterPageDataAttributeSets.Add(key, xmatterPageDataAttributeSet);
        }

        // Basically an approximation to Equals, but I don't want to bother with the whole pattern,
        // and for the purposes of this method we can ignore whether writing system aliases are the same.
        public bool SameAs(DataSet other)
        {
            if (this.TextVariables.Count != other.TextVariables.Count)
                return false;
            if (this.XmatterPageDataAttributeSets.Count != other.XmatterPageDataAttributeSets.Count)
                return false;
            foreach (var key in TextVariables.Keys)
            {
                DataSetElementValue otherVal;
                if (!other.TextVariables.TryGetValue(key, out otherVal))
                    return false;
                var ourVal = TextVariables[key];
                if (ourVal.IsCollectionValue != otherVal.IsCollectionValue)
                    return false;
                if (!ourVal.TextAlternatives.Equals(otherVal.TextAlternatives))
                    return false;
                if (!ourVal.AttributeListKeys.SetEquals(otherVal.AttributeListKeys))
                    return false;
                foreach (var lang in ourVal.AttributeListKeys)
                {
                    var ourAttrs = ourVal.GetAttributeList(lang);
                    var otherAttrs = otherVal.GetAttributeList(lang);
                    if (ourAttrs.Count != otherAttrs.Count)
                        return false;
                    var otherDict = new Dictionary<string, XmlString>();
                    // We don't care about the order of the lists, so make a dictionary of one set of tuples,
                    // and use it to see whether the other list has the same keys and values.
                    foreach (var tuple in otherAttrs)
                    {
                        otherDict[tuple.Item1] = tuple.Item2;
                    }

                    foreach (var tuple in ourAttrs)
                    {
                        XmlString otherItem2;
                        if (
                            !otherDict.TryGetValue(tuple.Item1, out otherItem2)
                            || tuple.Item2 != otherItem2
                        )
                            return false;
                    }
                }
            }

            foreach (var key in XmatterPageDataAttributeSets.Keys)
            {
                ISet<KeyValuePair<string, string>> otherVal;
                if (!other.XmatterPageDataAttributeSets.TryGetValue(key, out otherVal))
                    return false;
                if (!XmatterPageDataAttributeSets[key].KeyedSetsEqual(otherVal))
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// The values stored in DataSet.TextVariables. Each instance possibly stores data about multiple
    /// languages. For each language, it stores data taken from some element that has a corresponding
    /// lang attribute (and a data-book attribute, or one of the other data-X attributes, with
    /// a value correspoding to the key under which this element is stored in DataSet.TextVariables).
    /// The value stored in the TextAlternatives is, with some slight adjustments, the InnerXml of
    /// the element. In addition, some of the attribute values of the element may be stored in
    /// AttributeAlternatives under the same language key.
    /// </summary>
    public class DataSetElementValue
    {
        public DataSetElementValue(MultiTextBase text, bool isCollectionValue)
        {
            TextAlternatives = text;
            IsCollectionValue = isCollectionValue;
        }

        public MultiTextBase TextAlternatives;
        public bool IsCollectionValue;

        /// <summary>
        /// Keyed by language code, value is a list of (attribute name, attribute value) pairs.
        /// Assuming these are eventually going to get passed into setAttribute, it would be best if value was a simple string.
        /// </summary>
        private Dictionary<string, List<Tuple<string, XmlString>>> _attributeAlternatives;

        public void SetAttributeList(string lang, List<Tuple<string, XmlString>> alternatives)
        {
            if (_attributeAlternatives == null)
            {
                _attributeAlternatives = new Dictionary<string, List<Tuple<string, XmlString>>>();
            }

            if (alternatives == null)
                _attributeAlternatives.Remove(lang);
            else
                _attributeAlternatives[lang] = alternatives;
        }

        public List<Tuple<string, XmlString>> GetAttributeList(string lang)
        {
            List<Tuple<string, XmlString>> result = null;
            _attributeAlternatives?.TryGetValue(lang, out result);
            return result;
        }

        public HashSet<string> AttributeListKeys
        {
            get
            {
                if (_attributeAlternatives == null)
                    return new HashSet<string>();
                return new HashSet<string>(_attributeAlternatives.Keys);
            }
        }
    }
}
