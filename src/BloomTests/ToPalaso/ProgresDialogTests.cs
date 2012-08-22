using Bloom.Edit;
using Bloom.ToPalaso;
using NUnit.Framework;
using Palaso.CommandLineProcessing;

namespace BloomTests.ToPalaso
{

	[TestFixture]
	public class ProgresDialogTests
	{

		[Test, Ignore("By hand only")]
		public void LaunchDemoDialog()
		{
			using (var dlg = new ProgressDialogBackground())
			{
				dlg.ShowAndDoWork((progress,args) => CommandLineRunner.Run("PalasoUIWindowsForms.TestApp.exe", "CommandLineRunnerTest", null, string.Empty, 60, progress
				,(s)=>
					{
						progress.WriteStatus(s);
						progress.WriteVerbose(s);
					}));
			}
		}

	}

}
