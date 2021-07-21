using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;


namespace Bloom.Publish
{
	/// <summary>
	/// Used to help serialize / deserialize the setting checkbox values for which languages should be publish
	/// </summary>
	public class LangsToPublishSetting
	{
		[JsonProperty("bloomReader")]
		public Dictionary<string, LangToPublishCheckboxValue> ForBloomReader;	// The language codes of the languages that should be published when publishing for Bloom Reader

		[JsonProperty("bloomLibrary")]
		public Dictionary<string, LangToPublishCheckboxValue> ForBloomLibrary;	// The language codes of the languages that should be published when publishing to Bloom Library
	}
}
