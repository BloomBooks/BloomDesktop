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
			TextVariables = new Dictionary<string, NamedMutliLingualValue>();
			XmatterPageDataAttributeSets = new Dictionary<string, ISet<KeyValuePair<string, string>>>();
		}

		/// <summary>
		/// Depending on the context, the correct values for these change. E.g., "V" is the *actual* vernacular when looking at a book in the Vernacular library,
		/// but it should be the national language or UI language when looking a shell in a collection (where we'd want to see, for example, the French title)
		///
		/// Values in use currently are: "V", "N1", "N2"
		/// </summary>
		public Dictionary<string, string> WritingSystemAliases { get; private set; }

		public Dictionary<string, NamedMutliLingualValue> TextVariables { get; private set; }

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
			TextVariables.Add(key, new NamedMutliLingualValue(text, isCollectionValue));
		}

		public void UpdateLanguageString(string key,  string value, string writingSystemId,bool isCollectionValue)
		{
			NamedMutliLingualValue namedMutliLingualValue;
			MultiTextBase text;
			if(TextVariables.TryGetValue(key,out namedMutliLingualValue))
				text = namedMutliLingualValue.TextAlternatives;
			else
			{
				text = new MultiTextBase();
			}
			text.SetAlternative(DealiasWritingSystemId(writingSystemId), value);
			TextVariables.Remove(key);
			if(text.Count>0)
				TextVariables.Add(key, new NamedMutliLingualValue(text, isCollectionValue));
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
				TextVariables.Add(key, new NamedMutliLingualValue(text, isCollectionValue));
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
				NamedMutliLingualValue otherVal;
				if (!other.TextVariables.TryGetValue(key, out otherVal))
					return false;
				var ourVal = TextVariables[key];
				if (ourVal.IsCollectionValue != otherVal.IsCollectionValue)
					return false;
				if (!ourVal.TextAlternatives.Equals(otherVal.TextAlternatives))
					return false;
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

	public class NamedMutliLingualValue
	{
		public NamedMutliLingualValue(MultiTextBase text, bool isCollectionValue)
		{
			TextAlternatives = text;
			IsCollectionValue = isCollectionValue;
		}
		public MultiTextBase TextAlternatives;
		public bool IsCollectionValue;

	}
}
