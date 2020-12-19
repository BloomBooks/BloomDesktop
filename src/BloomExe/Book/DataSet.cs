using System;
using System.Collections.Generic;
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
			WritingSystemAliases = new Dictionary<string, string>();
			TextVariables = new Dictionary<string, DataSetElementValue>();
			XmatterPageDataAttributeSets = new Dictionary<string, ISet<KeyValuePair<string, string>>>();
		}

		/// <summary>
		/// Depending on the context, the correct values for these change. E.g., "V" is the *actual* vernacular when looking at a book in the Vernacular library,
		/// but it should be the national language or UI language when looking a shell in a collection (where we'd want to see, for example, the French title)
		///
		/// Values in use currently are: "V", "N1", "N2"
		/// </summary>
		public Dictionary<string, string> WritingSystemAliases { get; private set; }

		public Dictionary<string, DataSetElementValue> TextVariables { get; private set; }

		/// <summary>
		/// The key is the name of the xmatter page (such as frontCover).
		/// The value is a set of data attribute key-value pairs (such as data-backgroundaudio='SoundFile.mp3', data-backgroundaudiovolume='0.5').
		/// These get synchronized between the data div and the actual xmatter pages.
		/// </summary>
		public Dictionary<string, ISet<KeyValuePair<string, string>>> XmatterPageDataAttributeSets { get; }

		public void UpdateGenericLanguageString(string key, string value, bool isCollectionValue)
		{
			var text = new MultiTextBase();
			text.SetAlternative("*", value);
			if(TextVariables.ContainsKey(key))
			{
				TextVariables.Remove(key);
			}
			TextVariables.Add(key, new DataSetElementValue(text, isCollectionValue));
		}

		public string GetGenericLanguageString(string key)
		{
			DataSetElementValue dataSetElementValue;
			MultiTextBase text;
			if (TextVariables.TryGetValue(key, out dataSetElementValue))
				text = dataSetElementValue.TextAlternatives;
			else
			{
				text = new MultiTextBase();
			}

			return text.GetExactAlternative("*");
		}

		public void UpdateLanguageString(string key,  string value, string writingSystemId,bool isCollectionValue)
		{
			DataSetElementValue dataSetElementValue;
			MultiTextBase text;
			if(TextVariables.TryGetValue(key,out dataSetElementValue))
				text = dataSetElementValue.TextAlternatives;
			else
			{
				text = new MultiTextBase();
			}
			text.SetAlternative(DealiasWritingSystemId(writingSystemId), value);
			TextVariables.Remove(key);
			if(text.Count>0)
				TextVariables.Add(key, new DataSetElementValue(text, isCollectionValue));
		}

		public string DealiasWritingSystemId(string writingSystemId)
		{
			if (WritingSystemAliases.ContainsKey(writingSystemId))
			{
				writingSystemId = WritingSystemAliases[writingSystemId]; // e.g. convert "V" to the Vernacular
			}
			return writingSystemId;
		}

		public void AddLanguageString(string key, string value, string writingSystemId, bool isCollectionValue)
		{
			if(!TextVariables.ContainsKey(key))
			{
				var text = new MultiTextBase();
				TextVariables.Add(key, new DataSetElementValue(text, isCollectionValue));
			}
			TextVariables[key].TextAlternatives.SetAlternative(writingSystemId,value);
		}

		/// <summary>
		/// Updates (or adds) the set of attributes which is associated with a particular xmatter page.
		/// For example, xmatter page with key "frontCover" has data-backgroundaudio and data-backgroundaudiovolume attributes.
		/// </summary>
		public void UpdateXmatterPageDataAttributeSet(string key, ISet<KeyValuePair<string, string>> xmatterPageDataAttributeSet)
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
					var otherDict = new Dictionary<string, string>();
					// We don't care about the order of the lists, so make a dictionary of one set of tuples,
					// and use it to see whether the other list has the same keys and values.
					foreach (var tuple in otherAttrs)
					{
						otherDict[tuple.Item1] = tuple.Item2;
					}

					foreach (var tuple in ourAttrs)
					{
						string otherItem2;
						if (!otherDict.TryGetValue(tuple.Item1, out otherItem2) || tuple.Item2 != otherItem2)
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
		/// </summary>
		private Dictionary<string, List<Tuple<string, string>>> _attributeAlternatives;

		public void SetAttributeList(string lang, List<Tuple<string, string>> alternatives)
		{
			if (_attributeAlternatives == null)
			{
				_attributeAlternatives = new Dictionary<string, List<Tuple<string, string>>>();
			}

			if (alternatives == null)
				_attributeAlternatives.Remove(lang);
			else
				_attributeAlternatives[lang] = alternatives;
		}

		public List<Tuple<string, string>> GetAttributeList(string lang)
		{
			List<Tuple<string, string>> result = null;
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
