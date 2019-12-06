using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.Publish.Android
{
	// This class is used to pass settings from the PublishToAndroidApi to the many and varied
	// places that they pass through before getting to BloomReaderFileMaker.
	// Although there is only one setting as yet, we will in future be able to add more by
	// just extending this class.
	public class AndroidPublishSettings
	{
		public HashSet<string> LanguagesToInclude;

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
