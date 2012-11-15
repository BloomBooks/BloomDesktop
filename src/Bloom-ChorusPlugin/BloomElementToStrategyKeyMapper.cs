using System;
using System.Collections.Generic;
using System.Xml;
using Chorus.merge.xml.generic;
using Palaso.Code;

namespace Bloom_ChorusPlugin
{
	internal class BloomElementToStrategyKeyMapper : IElementToMergeStrategyKeyMapper
	{
		/// <summary>
		/// Get key to use to find ElementStrategy in the collection held by MergeStrategies
		/// </summary>
		/// <param name="keys">The keys in MergeStrategies dictionary.</param>
		/// <param name="element">The element currently being processed, that the key if needed for.</param>
		/// <returns>The key in the MergeStrategies disctionary that is used to look up the ElementStrategy.</returns>
		/// <exception cref="ArgumentNullException">Thrown if <param name="element" /> is null.</exception>
		public string GetKeyFromElement(HashSet<string> keys, XmlNode element)
		{
			Guard.AgainstNull(keys, "keys is null.");
			Guard.AgainstNull(element, "Element is null.");

			if (Matches(element, "self::div[@id='bloomDataDiv']"))
			{
				return "DataDiv";
			}
			if (Matches(element, "self::div[contains(@class,'bloom-page')]"))
				return "PageDiv";

			if (Matches(element, "self::div[@data-library and @lang]"))
				return "LibraryDataItem";

			if (Matches(element, "self::div[@data-book and @lang]"))
				return "BookDataItem";

			return element.Name;
		}

		private bool Matches(XmlNode element, string xpath)
		{
			return element.SelectSingleNode(xpath) != null;
		}
	}
}