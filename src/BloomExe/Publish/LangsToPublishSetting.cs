using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;


namespace Bloom.Publish
{
	/// <summary>
	/// Used to help serialize / deserialize the setting checkbox values for which languages should be published
	/// </summary>
	public class LangsToPublishSetting
	{
		[JsonProperty("bloomPUB")]
		public Dictionary<string, InclusionSetting> ForBloomPUB;	// The language codes of the languages that should be published when publishing a BloomPUB for Bloom Reader, BloomPUB Viewer, etc

		[JsonProperty("bloomLibrary")]
		public Dictionary<string, InclusionSetting> ForBloomLibrary;	// The language codes of the languages that should be published when publishing to Bloom Library
	}

	public static class LangsToPublishSettingExtensions
	{
		public static IEnumerable<string> IncludedLanguages(this Dictionary<string, InclusionSetting> settings)
		{
			return settings.Where(l => l.Value.IsIncluded()).Select(kvp => kvp.Key);
		}
	}
}
