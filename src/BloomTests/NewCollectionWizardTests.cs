using System;
using System.IO;
using System.Windows.Forms;
using Bloom;
using Bloom.NewCollection;
using NUnit.Framework;

namespace BloomTests
{
	public class NewCollectionWizardTests
	{

		[Test, Ignore("by hand")]
		public void Run()
		{
			Application.EnableVisualStyles();

			Browser.SetUpXulRunner();
			using (var dlg = new NewCollectionWizard(DefaultParentDirectoryForLibraries()))
			{
				dlg.ShowDialog();
			}
		}



		private string DefaultParentDirectoryForLibraries()
		{
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Bloom");
		}
	}
}
