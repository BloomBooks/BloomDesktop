using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Publish;
using BloomTemp;
using Chorus.VcsDrivers.Mercurial;
using LibChorus.TestUtilities;
using NUnit.Framework;
using Palaso.IO;
using Palaso.Progress;

namespace BloomTests
{
	[TestFixture]
	public class SendReceiveTests
	{
		[Test, Ignore("not yet")]
		public void CreateOrLocate_FolderHasAccentedLetter_FindsIt()
		{
			using (var setup = new RepositorySetup("Abé Books"))
			{
				Assert.NotNull(HgRepository.CreateOrUseExisting(setup.Repository.PathToRepo, new ConsoleProgress()));
			}
		}

		[Test, Ignore("not yet")]
		public void CreateOrLocate_FolderHasAccentedLetter2_FindsIt()
		{
			using (var testRoot = new TemporaryFolder("bloom sr test"))
			{
				string path = Path.Combine(testRoot.FolderPath, "Abé Books");
				Directory.CreateDirectory(path);

				Assert.NotNull(HgRepository.CreateOrUseExisting(path, new ConsoleProgress()));
				Assert.NotNull(HgRepository.CreateOrUseExisting(path, new ConsoleProgress()));
			}
		}
	  }
}
