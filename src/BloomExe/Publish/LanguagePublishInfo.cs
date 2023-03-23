using System;

namespace Bloom.Publish
{
	/// <summary>
	/// Must correspond to ILanguagePublishInfo in Javascript's PublishLanguagesGroup
	/// Contains information needed by the Publish tab to render a control allowing the user to select
	/// which languages should be included in the published file.
	/// </summary>
	struct LanguagePublishInfo
	{
		public string code;
		public string name;
		public bool complete;
		public bool includeText;
		public bool containsAnyAudio;
		public bool includeAudio;
		public bool required;
	}
}
