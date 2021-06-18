using System.Collections.Generic;

namespace Bloom.Publish.Android
{
	// This class is used to pass settings from the PublishToAndroidApi to the many and varied
	// places that they pass through before getting to BloomReaderFileMaker.
	// Although there is only one setting as yet, we will in future be able to add more by
	// just extending this class.
	public class AndroidPublishSettings
	{
		// Specifies the languages whose text should be included in the published book.
		public HashSet<string> LanguagesToInclude;

		// Specifies the languages for which narration audio should not be included, even if their text is include
		// NOTE: It's more natural for consumers to think about what languages they want to EXCLUDE, rather than what languages they want to INCLUDE
		public HashSet<string> AudioLanguagesToExclude;

		public override bool Equals(object obj)
		{
			if (!(obj is AndroidPublishSettings))
				return false;
			var other = (AndroidPublishSettings) obj;
			return LanguagesToInclude.SetEquals(other.LanguagesToInclude);
		}

		public override int GetHashCode()
		{
			return LanguagesToInclude.GetHashCode();
		}
	}
}
