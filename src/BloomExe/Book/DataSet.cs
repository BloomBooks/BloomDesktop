using System.Collections.Generic;
using Palaso.Text;

namespace Bloom.Book
{
	/// <summary>
	/// Acts as a cache of values we inject and gather from the document.
	/// </summary>
	public class DataSet
	{
		public DataSet()
		{
			WritingSystemCodes = new Dictionary<string, string>();
			TextVariables = new Dictionary<string, MultiLingualValue>();
		}

		/// <summary>
		/// Depending on the context, the correct values for these change. E.g., "V" is the *actual* vernacular when looking at a book in the Vernacular library,
		/// but it should be the national language or UI language when looking a shell in a collection (where we'd want to see, for example, the French title)
		///
		/// Values in use currently are: "V", "N1", "N2"
		/// </summary>
		public Dictionary<string, string> WritingSystemCodes { get; private set; }

		public Dictionary<string, MultiLingualValue> TextVariables { get; private set; }


		public void UpdateGenericLanguageString(string key, string value, bool isCollectionValue)
		{
			var text = new MultiTextBase();
			text.SetAlternative("*", value);
			if(TextVariables.ContainsKey(key))
			{
				TextVariables.Remove(key);
			}
			TextVariables.Add(key, new MultiLingualValue(text, isCollectionValue));
		}

		public void UpdateLanguageString(string key,  string value, string writingSystemId,bool isCollectionValue)
		{
			MultiLingualValue multiLingualValue;
			MultiTextBase text;
			if(TextVariables.TryGetValue(key,out multiLingualValue))
				text = multiLingualValue.TextAlternatives;
			else
			{
				text = new MultiTextBase();
			}
			text.SetAlternative(writingSystemId, value);
			TextVariables.Remove(key);
			if(text.Count>0)
				TextVariables.Add(key, new MultiLingualValue(text, isCollectionValue));
		}

		public void AddLanguageString(string key, string value, string writingSystemId, bool isCollectionValue)
		{
			if(!TextVariables.ContainsKey(key))
			{
				var text = new MultiTextBase();
				TextVariables.Add(key, new MultiLingualValue(text, isCollectionValue));
			}
			TextVariables[key].TextAlternatives.SetAlternative(writingSystemId,value);
		}
	}

	public class MultiLingualValue
	{
		public MultiLingualValue(MultiTextBase text, bool isCollectionValue)
		{
			TextAlternatives = text;
			IsCollectionValue = isCollectionValue;
		}
		public MultiTextBase TextAlternatives;
		public bool IsCollectionValue;

	}
}