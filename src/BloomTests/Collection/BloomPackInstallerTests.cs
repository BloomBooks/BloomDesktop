using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bloom.Collection.BloomPack;
using NUnit.Framework;

namespace BloomTests.Collection
{
	[TestFixture]
	public class BloomPackInstallerTests
	{
		[Test, Ignore("By Hand")]
		public void RunIt()
		{
			using (var dlg = new BloomPackInstallDialog(@"C:\Users\John\Desktop\Szi's shells Books.BloomPack"))
			{
				dlg.ShowDialog();
			}
		}
	}
}
