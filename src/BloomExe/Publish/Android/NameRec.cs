using System;

namespace Bloom.Publish.Android
{
	// Must correspond to INameRec in Javascript's PublishLanguageGroup
	struct NameRec
	{
		public string code;
		public string name;
		public bool complete;
		public bool includeText;
		public bool includeAudio;
	}
}
