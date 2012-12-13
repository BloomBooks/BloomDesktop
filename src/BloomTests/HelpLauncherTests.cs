
using Bloom;

using NUnit.Framework;

namespace BloomTests
{
	[TestFixture]
	public class HelpLauncherTests
	{
		[Test, Ignore("byhand")]
		public void CurrentSelection_ADifferentBookIsSelected_GoesToFirstPage()
		{
			HelpLauncher.Show(null, "Chorus_Help.chm", "Chorus/Chorus_overview.htm");
		}
	}
}
