using System;
using System.Collections.Generic;
using System.Xml;
using Chorus.merge.xml.generic;
using SIL.Code;

namespace Bloom_ChorusPlugin
{
	internal class BloomElementToStrategyKeyMapper : IElementToMergeStrategyKeyMapper
	{
		/// <summary>
		/// Get key to use to find ElementStrategy in the collection held by MergeStrategies
		/// </summary>
		/// <param name="keys">The keys in MergeStrategies dictionary.</param>
		/// <param name="element">The element currently being processed, that the key is needed for.</param>
		/// <returns>The key in the MergeStrategies dictionary that is used to look up the ElementStrategy.</returns>
		/// <exception cref="ArgumentNullException">Thrown if <param name="element" /> is null.</exception>
		public string GetKeyFromElement(HashSet<string> keys, XmlNode element)
		{
			Guard.AgainstNull(keys, "keys is null.");
			Guard.AgainstNull(element, "Element is null.");

			if (Matches(element, "self::div[@id='bloomDataDiv']"))
				return "DataDiv";

			if (Matches(element, "self::div[contains(@class,'bloom-page')]"))
				return "PageDiv";

			if (Matches(element, "self::div[@data-collection and @lang]"))
				return "LibraryDataItem";

			if (Matches(element, "self::div[@data-library and @lang]")) //"library" was the pre-version 1 name for what is now "collection"
				return "LibraryDataItem";

			if (Matches(element, "self::div[@data-book and @lang]"))
				return "BookDataItem";

			if (Matches(element, "self::div[contains(@class, 'bloom-translationGroup')]"))
				return "TranslationGroup";

			if (Matches(element, "self::div[@lang and contains(@class, 'bloom-editable')]"))
				return "LangDiv";

			return element.Name;
		}

		private bool Matches(XmlNode element, string xpath)
		{
			return element.SelectSingleNode(xpath) != null;
		}
	}
}