using System.Collections.Generic;
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
			Attributes = new Dictionary<string, List<KeyValuePair<string, string>>>();
		}

		/// <summary>
		/// Depending on the context, the correct values for these change. E.g., "V" is the *actual* vernacular when looking at a book in the Vernacular library,
		/// but it should be the national language or UI language when looking a shell in a collection (where we'd want to see, for example, the French title)
		///
		/// Values in use currently are: "V", "N1", "N2"
		/// </summary>
		public Dictionary<string, string> WritingSystemAliases { get; private set; }

		public Dictionary<string, NamedMutliLingualValue> TextVariables { get; private set; }

		public Dictionary<string, List<KeyValuePair<string, string>>> Attributes { get; private set; } 


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